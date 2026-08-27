using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Arronix.Common.Lifetimes;
using Arronix.Host.Media.Catalog;

namespace Arronix.Host.Providers;

/// <summary>One catalog item with the reference the host holds it under.</summary>
/// <typeparam name="TItem">The media-owned item type.</typeparam>
/// <param name="Reference">The host-assigned reference: kind, level and durable identity.</param>
/// <param name="CatalogId">The identifier of the record it was materialized from, in its catalog's scheme.</param>
/// <param name="Item">The exact media-owned item, as its cataloger returned it.</param>
/// <param name="CuratedEntryId">The curator's entry identifier when materialized from a curated list.</param>
public sealed record MaterializedItem<TItem>(
    MediaItemRef Reference,
    ExternalId CatalogId,
    TItem Item,
    CuratedEntryId? CuratedEntryId = null)
    where TItem : class, IMediaItem;

/// <summary>
/// Resolves catalog references to items, and gives each item the durable identity the platform holds it
/// under.
/// </summary>
/// <remarks>
/// <para>
/// Routing is by external identifier scheme. A reference carries a scheme and a value and nothing about who
/// produced it, so an installation with two implementations of one catalog, or with one replaced, resolves
/// the same references without rewriting them.
/// </para>
/// <para>
/// A cataloger is the authority on what an item is; this is the authority on what the platform calls it. The
/// two never overlap: no identity the host assigns crosses a provider contract, and no identifier a provider
/// supplies is treated as a durable key.
/// </para>
/// </remarks>
/// <param name="providers">Where implementations are looked up.</param>
/// <param name="definitions">Where configured definitions are read from.</param>
/// <param name="status">Which definitions are currently in service.</param>
/// <param name="tests">Where invocations are built.</param>
/// <param name="identity">Host identity state, which outlives any extension reload.</param>
public sealed class CatalogDispatcher(
    ProviderRegistry providers,
    ProviderDefinitionStore definitions,
    ProviderStatusStore status,
    ProviderTestService tests,
    CatalogIdentity identity)
{
    private readonly ProviderRegistry _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    private readonly ProviderDefinitionStore _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ProviderStatusStore _status = status ?? throw new ArgumentNullException(nameof(status));
    private readonly ProviderTestService _tests = tests ?? throw new ArgumentNullException(nameof(tests));
    private readonly CatalogIdentity _identity = identity ?? throw new ArgumentNullException(nameof(identity));

    /// <summary>
    /// Lists the configured catalogers that are the authority for one scheme and supply this kind's item
    /// type, best priority first.
    /// </summary>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="scheme">The external identifier scheme.</param>
    /// <returns>The definitions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="scheme"/> is not in canonical form.</exception>
    public IReadOnlyList<ProviderDefinition> AuthoritiesFor(IMediaTypeRuntime kind, string scheme)
    {
        ArgumentNullException.ThrowIfNull(kind);
        RequireCanonicalScheme(scheme, nameof(scheme));

        return
        [
            .. _definitions.Query(ProviderFamily.Cataloger, kind.Kind, enabledOnly: true)
                .Where(definition => _status.IsAvailable(definition.Id))
                .Where(definition => _providers.TryGet(definition.Provider, out var registered)
                    && string.Equals(registered.CatalogScheme, scheme, StringComparison.Ordinal)
                    && registered.PairedMediaKind == kind.Kind)
                .OrderBy(static definition => definition.Priority)
                .ThenBy(static definition => definition.Id),
        ];
    }

    /// <summary>
    /// Fetches one item by catalog identity and gives it its durable identity.
    /// </summary>
    /// <typeparam name="TItem">The media-owned item type.</typeparam>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="catalogId">The identifier to resolve. It may be an alias the catalog redirects.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The materialized item, or <see langword="null"/> when no owning catalog holds one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TItem"/> is not the kind's item type, or <paramref name="catalogId"/> is malformed.
    /// </exception>
    /// <exception cref="ArronixException">
    /// No installed cataloger owns the scheme, or the item that came back does not state exactly one
    /// identifier in its cataloger's own scheme.
    /// </exception>
    public async Task<MaterializedItem<TItem>?> FetchAsync<TItem>(
        IMediaTypeRuntime kind,
        ExternalId catalogId,
        CancellationToken cancellationToken = default)
        where TItem : class, IMediaItem
    {
        ArgumentNullException.ThrowIfNull(kind);
        RequireItemType<TItem>(kind);

        var found = await FetchAsync(kind, catalogId, cancellationToken).ConfigureAwait(false);
        return found is null ? null : Typed<TItem>(found);
    }

    /// <summary>
    /// Fetches one item by catalog identity and gives it its durable identity, without naming its type.
    /// </summary>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="catalogId">The identifier to resolve. It may be an alias the catalog redirects.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The materialized item, or <see langword="null"/> when no owning catalog holds one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="catalogId"/> is malformed.</exception>
    /// <exception cref="ArronixException">
    /// No installed cataloger owns the scheme, or the item that came back does not state exactly one
    /// identifier in its cataloger's own scheme.
    /// </exception>
    /// <remarks>
    /// The form a kind-blind caller uses: an HTTP route cannot write the item type, and resolving a generic
    /// method against one at run time would be the reflection this platform does not do. The item's exact
    /// type is intact on the value that comes back.
    /// </remarks>
    public async Task<MaterializedItem<IMediaItem>?> FetchAsync(
        IMediaTypeRuntime kind,
        ExternalId catalogId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        RequireWellFormed(catalogId, nameof(catalogId));

        var binding = RequireBinding(kind);

        foreach (var definition in RequireAuthorities(kind, catalogId.Scheme))
        {
            if (!_providers.TryLease(definition.Provider, out var leased))
            {
                continue;
            }

            // Held across the fetch: teardown waits for this call rather than disposing the cataloger and
            // unloading its code while it is running.
            using var held = leased;

            if (binding.Bind(held.Value.Provider) is not { } cataloger
                || held.Value.CatalogScheme is not { } catalogScheme)
            {
                continue;
            }

            IMediaItem? item;

            try
            {
                item = await cataloger
                    .GetAsync(_tests.Invocation(definition), catalogId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception failure) when (Contained(failure, cancellationToken))
            {
                // A cataloger is a package's own code and may throw anything of its own. Whatever it threw,
                // the platform's answer is that the catalog did not answer — which is a different thing from
                // a malformed request, and must not reach a caller as one.
                throw new ArronixException(
                    CoreErrorCode.CatalogerConnectionFailed,
                    $"The cataloger for '{catalogScheme}' failed while resolving "
                    + $"'{catalogId.Scheme}:{catalogId.Value}'.",
                    failure);
            }

            if (item is null)
            {
                continue;
            }

            return Materialize(kind, catalogScheme, item, catalogId);
        }

        return null;
    }

    /// <summary>
    /// Searches one catalog and gives every result its durable identity.
    /// </summary>
    /// <typeparam name="TItem">The media-owned item type.</typeparam>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="scheme">The catalog to search.</param>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The results, in the order the catalog returned them.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TItem"/> is not the kind's item type, or <paramref name="scheme"/> is not in
    /// canonical form.
    /// </exception>
    /// <exception cref="ArronixException">
    /// No installed cataloger owns the scheme, or a result does not state exactly one identifier in that
    /// scheme.
    /// </exception>
    public async Task<IReadOnlyList<MaterializedItem<TItem>>> SearchAsync<TItem>(
        IMediaTypeRuntime kind,
        string scheme,
        CatalogQuery query,
        CancellationToken cancellationToken = default)
        where TItem : class, IMediaItem
    {
        ArgumentNullException.ThrowIfNull(kind);
        RequireItemType<TItem>(kind);

        var found = await SearchAsync(kind, scheme, query, cancellationToken).ConfigureAwait(false);
        return [.. found.Select(Typed<TItem>)];
    }

    /// <summary>
    /// Searches one catalog and gives every result its durable identity, without naming the item type.
    /// </summary>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="scheme">The catalog to search.</param>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The results, in the order the catalog returned them.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="scheme"/> is not in canonical form.</exception>
    /// <exception cref="ArronixException">
    /// No installed cataloger owns the scheme, or a result does not state exactly one identifier in it.
    /// </exception>
    /// <remarks>
    /// A hit is given identity and nothing else. Searching a catalog is not adding to a library, so nothing
    /// here materializes a durable record.
    /// </remarks>
    public async Task<IReadOnlyList<MaterializedItem<IMediaItem>>> SearchAsync(
        IMediaTypeRuntime kind,
        string scheme,
        CatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(query);
        RequireCanonicalScheme(scheme, nameof(scheme));

        if (query.Id is { } queryId)
        {
            RequireWellFormed(queryId, nameof(query));
        }

        var binding = RequireBinding(kind);
        var found = new List<MaterializedItem<IMediaItem>>();

        // One entity, one result. Two configured catalogers for one scheme legitimately answer for the same
        // records, and the identity the host assigns is what says so — two catalog identifiers that
        // converged are one item too. The authorities are consulted best priority first, so the first
        // answer for an entity is the one the operator ranked highest; a later one is dropped rather than
        // merged, because merging two catalogers' items would produce a record neither of them stated.
        var claimed = new HashSet<MediaItemRef>();

        foreach (var definition in RequireAuthorities(kind, scheme))
        {
            if (!_providers.TryLease(definition.Provider, out var leased))
            {
                continue;
            }

            using var held = leased;

            if (binding.Bind(held.Value.Provider) is not { } cataloger
                || held.Value.CatalogScheme is not { } catalogScheme)
            {
                continue;
            }

            IReadOnlyList<IMediaItem> results;

            try
            {
                // Materialized inside the lease and into a host-owned list, so nothing the cataloger
                // returned is enumerated after its ticket is released.
                results = PluginBoundary.Snapshot(
                    await cataloger
                        .SearchAsync(_tests.Invocation(definition), query, cancellationToken)
                        .ConfigureAwait(false));
            }
            catch (Exception failure) when (Contained(failure, cancellationToken))
            {
                throw new ArronixException(
                    CoreErrorCode.CatalogerSearchFailed,
                    $"The cataloger for '{catalogScheme}' failed while searching it.",
                    failure);
            }

            foreach (var item in results)
            {
                var materialized = Materialize(kind, catalogScheme, item, catalogId: null);

                if (claimed.Add(materialized.Reference))
                {
                    found.Add(materialized);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Materializes a curated list by fetching each reference from the catalog that owns it.
    /// </summary>
    /// <typeparam name="TItem">The media-owned item type.</typeparam>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="list">The list as its curator returned it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The items that resolved, in the curator's own order and retaining its entry identifiers.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TItem"/> is not the kind's item type.</exception>
    /// <exception cref="ArronixException">
    /// No installed cataloger owns a scheme the list references. Ownership is checked for every reference
    /// before any of them is fetched, so a list is either resolvable as a whole or refused as a whole.
    /// </exception>
    public async Task<IReadOnlyList<MaterializedItem<TItem>>> MaterializeAsync<TItem>(
        IMediaTypeRuntime kind,
        CuratedListFetch<TItem> list,
        CancellationToken cancellationToken = default)
        where TItem : class, IMediaItem
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(list);
        RequireItemType<TItem>(kind);

        foreach (var reference in list.Items)
        {
            RequireWellFormed(reference.CatalogId, nameof(list));
            RequireAuthorities(kind, reference.CatalogId.Scheme);
        }

        var found = new List<MaterializedItem<TItem>>();

        foreach (var reference in list.Items)
        {
            var item = await FetchAsync<TItem>(kind, reference.CatalogId, cancellationToken).ConfigureAwait(false);

            if (item is not null)
            {
                found.Add(item with { CuratedEntryId = reference.EntryId });
            }
        }

        return found;
    }

    private static void RequireWellFormed(ExternalId catalogId, string parameterName)
        => CatalogIdentity.RequireWellFormed(catalogId, parameterName);

    private static void RequireCanonicalScheme(string? scheme, string parameterName)
    {
        if (!CatalogIdentity.IsCanonicalScheme(scheme))
        {
            throw new ArgumentException(
                "A catalog scheme is non-empty, lower-case and contains no white space and no ':'.",
                parameterName);
        }
    }

    private static void RequireItemType<TItem>(IMediaTypeRuntime kind)
        where TItem : class, IMediaItem
    {
        if (kind.ItemType != typeof(TItem))
        {
            throw new ArgumentException(
                $"Media kind '{kind.Kind}' supplies '{kind.ItemType.FullName}', not '{typeof(TItem).FullName}'.",
                nameof(kind));
        }
    }

    private IReadOnlyList<ProviderDefinition> RequireAuthorities(IMediaTypeRuntime kind, string scheme)
    {
        var authorities = AuthoritiesFor(kind, scheme);

        return authorities.Count > 0
            ? authorities
            : throw new ArronixException(
                CoreErrorCode.CatalogSchemeUnowned,
                $"No installed cataloger is the authority for '{scheme}' in media kind '{kind.Kind}', so a "
                + "reference in that scheme cannot be resolved.");
    }

    /// <summary>
    /// Reads the item's own catalog identity and binds it, the identifier asked for and every other
    /// identifier the item carries to one reference.
    /// </summary>
    private MaterializedItem<IMediaItem> Materialize(
        IMediaTypeRuntime kind,
        string catalogScheme,
        IMediaItem item,
        ExternalId? catalogId)
    {
        var carried = item.ExternalIds.Values;

        if (!CatalogScheme.TryIdentityIn(catalogScheme, carried, out var canonical))
        {
            throw new ArronixException(
                CoreErrorCode.CatalogIdentityInvalid,
                $"A cataloger for '{catalogScheme}' returned an item stating "
                + $"{carried.Count(id => string.Equals(id.Scheme, catalogScheme, StringComparison.Ordinal))} "
                + $"identifiers in that scheme; exactly one is its identity for the item.");
        }

        // The identifier asked for is bound alongside the one the catalog answered with, so a redirect and
        // the record it lands on are one item, and so is the same record reached through another catalog.
        var converging = catalogId is { } requested
            ? (IReadOnlyCollection<ExternalId>)[canonical, requested, .. carried]
            : [canonical, .. carried];

        var reference = _identity.Identify(kind.Kind, ItemLevelOf(kind), converging);
        return new MaterializedItem<IMediaItem>(reference, canonical, item);
    }

    /// <summary>Restates one materialized item at the exact type its caller named.</summary>
    private static MaterializedItem<TItem> Typed<TItem>(MaterializedItem<IMediaItem> found)
        where TItem : class, IMediaItem
        => new(found.Reference, found.CatalogId, (TItem)found.Item, found.CuratedEntryId);

    /// <summary>
    /// The runtime's own ability to recognize a cataloger closed over its item type.
    /// </summary>
    /// <exception cref="ArronixException">
    /// The runtime does not supply one, which means it was not built by the host's model factory. Refused
    /// rather than worked around: constructing the closed contract by name at run time is the reflection
    /// this boundary exists to avoid.
    /// </exception>
    private static ICatalogerBinding RequireBinding(IMediaTypeRuntime kind)
        => kind as ICatalogerBinding
            ?? throw new ArronixException(
                CoreErrorCode.PluginProviderContractInvalid,
                $"The runtime for media kind '{kind.Kind}' cannot bind a cataloger to its own item type "
                + $"'{kind.ItemType.FullName}', so no catalog work can be routed to it.");

    /// <summary>
    /// Whether a failure a cataloger raised is one the platform may turn into its own answer.
    /// </summary>
    /// <remarks>
    /// Everything except the caller's own cancellation and the conditions under which the process is no
    /// longer sound. A cataloger's own exception type means nothing outside its package, so it becomes the
    /// platform's statement that the catalog did not answer, with the original kept as the cause.
    /// </remarks>
    private static bool Contained(Exception failure, CancellationToken cancellationToken)
        => !(failure is OperationCanceledException && cancellationToken.IsCancellationRequested)
            && !ProcessFailure.IsFatal(failure);

    /// <summary>The level a kind's items sit at, which is the key space their identifiers occupy.</summary>
    private static MediaLevelId ItemLevelOf(IMediaTypeRuntime kind) => kind.Shape.Levels[0].Id;
}
