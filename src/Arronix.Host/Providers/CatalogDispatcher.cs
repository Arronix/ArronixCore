using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Common.Lifetimes;
using Arronix.Host.Media;
using Arronix.Host.Media.Catalog;

namespace Arronix.Host.Providers;

/// <summary>One catalog result the platform has not materialized.</summary>
/// <typeparam name="TItem">The media-owned item type.</typeparam>
/// <param name="CatalogId">The item's identity in its own cataloger's scheme.</param>
/// <param name="Item">The exact media-owned item, as its cataloger returned it.</param>
/// <param name="Held">The reference the platform already holds it under. Read, never assigned.</param>
/// <param name="RequestedId">The identifier asked for, when the catalog redirected. Bound with the identity.</param>
/// <param name="CuratedEntryId">The curator's entry identifier when it came from a curated list.</param>
public sealed record CatalogCandidate<TItem>(
    ExternalId CatalogId,
    TItem Item,
    MediaItemRef? Held,
    ExternalId? RequestedId = null,
    CuratedEntryId? CuratedEntryId = null)
    where TItem : class, IMediaItem;

/// <summary>One catalog item with the reference the host holds it under.</summary>
/// <typeparam name="TItem">The media-owned item type.</typeparam>
/// <param name="Reference">The host-assigned reference: kind, level and durable identity.</param>
/// <param name="CatalogId">The identifier of the record it was materialized from, in its catalog's scheme.</param>
/// <param name="Item">The exact media-owned item, as its cataloger returned it.</param>
/// <param name="CuratedEntryId">The curator's entry identifier when materialized from a curated list.</param>
/// <remarks>
/// Host-internal: naming a record and recording that the library holds it are one transaction, and this
/// milestone has only the first half.
/// </remarks>
internal sealed record MaterializedItem<TItem>(
    MediaItemRef Reference,
    ExternalId CatalogId,
    TItem Item,
    CuratedEntryId? CuratedEntryId = null)
    where TItem : class, IMediaItem;

/// <summary>What one catalog fetch established about a record.</summary>
/// <remarks>
/// Only <see cref="NotHeld"/> is a statement about the record, and it is fail-closed: one authority that did
/// not answer leaves the question open rather than settling it as absence. A scheme no installed cataloger
/// owns is refused with <see cref="CoreErrorCode.CatalogSchemeUnowned"/> before anything is asked.
/// </remarks>
public enum CatalogFetchOutcome
{
    /// <summary>An authority answered with the record.</summary>
    Found = 0,

    /// <summary>Every authority answered, and none holds the record.</summary>
    NotHeld = 1,

    /// <summary>Every installed authority for the scheme is backed off, so none was asked.</summary>
    AuthorityUnavailable = 2,

    /// <summary>At least one authority did not answer, so nothing is established about the record.</summary>
    NotAnswered = 3
}

/// <summary>The answer to one catalog fetch.</summary>
/// <typeparam name="TItem">The media-owned item type.</typeparam>
/// <param name="Candidate">The candidate, populated only for <see cref="CatalogFetchOutcome.Found"/>.</param>
/// <param name="Outcome">What the fetch established.</param>
/// <param name="Reason">Operator-visible detail for an outcome that is not a record.</param>
public sealed record CatalogFetch<TItem>(
    CatalogCandidate<TItem>? Candidate,
    CatalogFetchOutcome Outcome,
    string? Reason = null)
    where TItem : class, IMediaItem;

/// <summary>What one or more catalogs answered.</summary>
/// <typeparam name="TItem">The media-owned item type.</typeparam>
/// <param name="Candidates">The results, in the order the catalogs returned them.</param>
/// <param name="IsPartialResult">Whether an authority that was asked for did not contribute.</param>
/// <param name="Warnings">Operator-visible warnings naming each authority that did not contribute.</param>
/// <remarks>
/// An empty answer and an incomplete one are different — the shape <see cref="ReleaseQueryResult"/> already
/// gives an indexer search.
/// </remarks>
public sealed record CatalogAnswer<TItem>(
    IReadOnlyList<CatalogCandidate<TItem>> Candidates,
    bool IsPartialResult,
    IReadOnlyList<string> Warnings)
    where TItem : class, IMediaItem;

/// <summary>
/// Resolves catalog references to candidates, and assigns durable identity when one is taken in.
/// </summary>
/// <remarks>
/// <para>
/// Routing is by external identifier scheme. A reference carries a scheme and a value and nothing about who
/// produced it, so an installation with two implementations of one catalog, or with one replaced, resolves
/// the same references without rewriting them. A cataloger is the authority on what an item is; this is the
/// authority on what the platform calls it, and no identity the host assigns crosses a provider contract.
/// </para>
/// <para>
/// The public surface is read-only: searching, fetching and resolving allocate nothing and report an
/// already-held reference by looking it up. Assignment is host-internal, because naming a record and
/// recording that the library holds it belong in one transaction the durable store owns.
/// </para>
/// </remarks>
/// <param name="providers">Where implementations are looked up.</param>
/// <param name="definitions">Where configured definitions are read from.</param>
/// <param name="status">Which definitions are currently in service, and where their outcomes are recorded.</param>
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

    /// <summary>The read half of host identity state. Nothing reached through it can allocate.</summary>
    private ICatalogIdentityReader Reader => _identity;

    /// <summary>The assigning half, reached only from the internal materializing member.</summary>
    private ICatalogIdentityAssignment Assignment => _identity;

    /// <summary>
    /// Lists the configured catalogers that are the authority for one scheme, supply this kind's item type
    /// and are in service, best priority first.
    /// </summary>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="scheme">The external identifier scheme.</param>
    /// <returns>The definitions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="scheme"/> is not in canonical form.</exception>
    public IReadOnlyList<ProviderDefinition> AuthoritiesFor(IMediaTypeRuntime kind, string scheme)
        => [.. ConfiguredAuthoritiesFor(kind, scheme).Where(definition => _status.IsAvailable(definition.Id))];

    /// <summary>Fetches one catalog record. The answer is a candidate, or the reason there is none.</summary>
    /// <typeparam name="TItem">The media-owned item type.</typeparam>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="catalogId">The identifier to resolve. It may be an alias the catalog redirects.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fetch, whose outcome distinguishes a missing record from an unanswered call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TItem"/> is not the kind's item type, or <paramref name="catalogId"/> is malformed.
    /// </exception>
    /// <exception cref="ArronixException">
    /// No installed cataloger owns the scheme, or the item that came back does not state exactly one
    /// identifier in its cataloger's own scheme.
    /// </exception>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A cataloger's own code may throw anything, so the boundary contains everything except a process that is no longer sound; the failure is recorded against its definition and the next authority is asked, because it must not be mistaken for the catalog saying the record is gone. Cancellation raised by the provider rather than by the caller's token is one such failure, so only a signaled caller token propagates.")]
    public async Task<CatalogFetch<TItem>> FetchAsync<TItem>(
        IMediaTypeRuntime kind,
        ExternalId catalogId,
        CancellationToken cancellationToken = default)
        where TItem : class, IMediaItem
    {
        ArgumentNullException.ThrowIfNull(kind);
        RequireItemType<TItem>(kind);
        RequireWellFormed(catalogId, nameof(catalogId));
        cancellationToken.ThrowIfCancellationRequested();

        var authorities = RequireAuthorities(kind, catalogId.Scheme);

        if (authorities.Available.Count == 0)
        {
            return new CatalogFetch<TItem>(
                null,
                CatalogFetchOutcome.AuthorityUnavailable,
                $"Every installed cataloger for '{catalogId.Scheme}' in media kind '{kind.Kind}' is out of "
                + "service: "
                + string.Join(", ", authorities.Unavailable.Select(static definition => $"'{definition.Name}'"))
                + ". This is a back-off, not a missing installation.");
        }

        // An owner that was not asked cannot contribute to settling absence.
        var silent = authorities.Unavailable.Select(NotAsked).ToList();

        foreach (var definition in authorities.Available)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_providers.TryLease(definition.Provider, out var leased))
            {
                silent.Add($"'{definition.Name}' is in service but could not be leased.");
                continue;
            }

            // Held across the fetch: teardown waits for this call rather than disposing the cataloger and
            // unloading its code while it is running.
            using var held = leased;

            if (held.Value.Provider is not ICataloger<TItem> cataloger
                || held.Value.CatalogScheme is not { } catalogScheme)
            {
                silent.Add($"'{definition.Name}' does not supply this kind's items.");
                continue;
            }

            TItem? item;

            try
            {
                item = await cataloger
                    .GetAsync(_tests.Invocation(definition), catalogId, cancellationToken)
                    .ConfigureAwait(false);
            }
            // Only the caller's own. A provider raising this from its own timeout has failed to answer,
            // and is recorded as such rather than aborting the dispatch.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                _status.RecordFailure(definition.Id);
                silent.Add($"'{definition.Name}' did not answer: {failure.Message}");
                continue;
            }

            // A cataloger may ignore its token. An answer produced after withdrawal is neither a record
            // nor a contribution towards absence.
            cancellationToken.ThrowIfCancellationRequested();
            _status.RecordSuccess(definition.Id);

            if (item is null)
            {
                continue;
            }

            return new CatalogFetch<TItem>(
                Candidate(kind, catalogScheme, item, catalogId, curatedEntryId: null),
                CatalogFetchOutcome.Found);
        }

        // Absence is a claim about the record, so every authority that could have held it has to have said
        // so. One that failed, or could not be leased, leaves the question open: reporting it as absent
        // would let a transport fault look like a withdrawal, and a withdrawal is durable.
        return silent.Count == 0
            ? new CatalogFetch<TItem>(
                null,
                CatalogFetchOutcome.NotHeld,
                $"Every cataloger for '{catalogId.Scheme}' answered, and none holds '{catalogId.Value}'.")
            : new CatalogFetch<TItem>(
                null,
                CatalogFetchOutcome.NotAnswered,
                string.Join(" ", silent));
    }

    /// <summary>Searches one catalog, answering with candidates rather than with library items.</summary>
    /// <typeparam name="TItem">The media-owned item type.</typeparam>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="scheme">The catalog to search.</param>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The candidates, and whether every authority contributed.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TItem"/> is not the kind's item type, or <paramref name="scheme"/> is not in
    /// canonical form.
    /// </exception>
    /// <exception cref="ArronixException">
    /// No installed cataloger owns the scheme, or a result does not state exactly one identifier in that
    /// scheme.
    /// </exception>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A cataloger's own code may throw anything, so the boundary contains everything except a process that is no longer sound; the failure is recorded against its definition and reported as a warning, and the search proceeds with the authorities that answered. Cancellation raised by the provider rather than by the caller's token is one such failure, so only a signaled caller token propagates.")]
    public async Task<CatalogAnswer<TItem>> SearchAsync<TItem>(
        IMediaTypeRuntime kind,
        string scheme,
        CatalogQuery query,
        CancellationToken cancellationToken = default)
        where TItem : class, IMediaItem
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(query);
        RequireItemType<TItem>(kind);
        RequireCanonicalScheme(scheme, nameof(scheme));
        cancellationToken.ThrowIfCancellationRequested();

        if (query.Id is { } queryId)
        {
            RequireWellFormed(queryId, nameof(query));
        }

        var authorities = RequireAuthorities(kind, scheme);
        var found = new List<CatalogCandidate<TItem>>();
        var warnings = authorities.Unavailable.Select(NotAsked).ToList();

        foreach (var definition in authorities.Available)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_providers.TryLease(definition.Provider, out var leased))
            {
                warnings.Add($"'{definition.Name}' is in service but could not be leased.");
                continue;
            }

            using var held = leased;

            if (held.Value.Provider is not ICataloger<TItem> cataloger
                || held.Value.CatalogScheme is not { } catalogScheme)
            {
                warnings.Add($"'{definition.Name}' does not supply this kind's items.");
                continue;
            }

            IReadOnlyList<TItem> results;

            // Calling a cataloger and reading what it returned are both its own code, so both sit inside
            // the same containment: a collection that throws as it is enumerated is that authority failing,
            // not the search failing.
            try
            {
                var answered = await cataloger
                    .SearchAsync(_tests.Invocation(definition), query, cancellationToken)
                    .ConfigureAwait(false);

                // Before reading it: copying a page is work done for a caller who may have gone.
                cancellationToken.ThrowIfCancellationRequested();

                // Copied inside the lease and into a host-owned list, so nothing the cataloger returned is
                // enumerated after its ticket is released.
                results = PluginBoundary.Snapshot(answered);

                // Again, for a withdrawal that landed during the copy: the last point this answer can be
                // dropped without having contributed.
                cancellationToken.ThrowIfCancellationRequested();
            }
            // Only the caller's own. A provider raising this from its own timeout, or from enumerating what
            // it returned, has failed to answer and is recorded as such rather than aborting the dispatch.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                _status.RecordFailure(definition.Id);
                warnings.Add($"'{definition.Name}' did not answer: {failure.Message}");
                continue;
            }

            _status.RecordSuccess(definition.Id);
            found.AddRange(results.Select(item =>
                Candidate(kind, catalogScheme, item, requestedId: null, curatedEntryId: null)));
        }

        return new CatalogAnswer<TItem>(found, warnings.Count > 0, warnings);
    }

    /// <summary>
    /// Resolves a curated list by fetching each reference from the catalog that owns it.
    /// </summary>
    /// <typeparam name="TItem">The media-owned item type.</typeparam>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="list">The list as its curator returned it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The candidates that resolved, in the curator's own order and retaining its entry identifiers, and the
    /// reason for every entry that did not.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TItem"/> is not the kind's item type.</exception>
    /// <exception cref="ArronixException">
    /// No installed cataloger owns a scheme the list references. Ownership is checked for every reference
    /// before any of them is fetched, so a list is either resolvable as a whole or refused as a whole.
    /// </exception>
    public async Task<CatalogAnswer<TItem>> ResolveAsync<TItem>(
        IMediaTypeRuntime kind,
        CuratedListFetch<TItem> list,
        CancellationToken cancellationToken = default)
        where TItem : class, IMediaItem
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(list);
        RequireItemType<TItem>(kind);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var reference in list.Items)
        {
            RequireWellFormed(reference.CatalogId, nameof(list));
            RequireAuthorities(kind, reference.CatalogId.Scheme);
        }

        var found = new List<CatalogCandidate<TItem>>();
        var warnings = new List<string>();

        foreach (var reference in list.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fetch = await FetchAsync<TItem>(kind, reference.CatalogId, cancellationToken).ConfigureAwait(false);

            if (fetch is { Outcome: CatalogFetchOutcome.Found, Candidate: { } candidate })
            {
                found.Add(candidate with { CuratedEntryId = reference.EntryId });
                continue;
            }

            warnings.Add(
                $"'{reference.CatalogId.Scheme}:{reference.CatalogId.Value}' did not resolve "
                + $"({fetch.Outcome}): {fetch.Reason}");
        }

        return new CatalogAnswer<TItem>(found, warnings.Count > 0, warnings);
    }

    /// <summary>
    /// Assigns the durable identity one candidate is held under.
    /// </summary>
    /// <typeparam name="TItem">The media-owned item type.</typeparam>
    /// <param name="kind">The media kind's runtime.</param>
    /// <param name="candidate">The candidate, as a catalog call produced it.</param>
    /// <returns>The materialized item.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TItem"/> is not the kind's item type, or the candidate's identifier is malformed.
    /// </exception>
    /// <remarks>
    /// The one member that allocates, and internal because only the identity half of taking an item in
    /// exists here. Repeating it is idempotent: the identity, the identifier asked for and every other
    /// identifier the item states converge on the assignment made first.
    /// </remarks>
    internal MaterializedItem<TItem> Materialize<TItem>(IMediaTypeRuntime kind, CatalogCandidate<TItem> candidate)
        where TItem : class, IMediaItem
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(candidate);
        RequireItemType<TItem>(kind);
        RequireWellFormed(candidate.CatalogId, nameof(candidate));

        var reference = Assignment.Identify(kind.Kind, ItemLevelOf(kind), Converging(candidate));

        return new MaterializedItem<TItem>(
            reference,
            candidate.CatalogId,
            candidate.Item,
            candidate.CuratedEntryId);
    }

    private static void RequireWellFormed(ExternalId catalogId, string parameterName)
        => CatalogIdentity.RequireWellFormed(catalogId, parameterName);

    private static void RequireCanonicalScheme(string? scheme, string parameterName)
    {
        if (!CatalogIdentity.IsCanonicalScheme(scheme))
        {
            throw new ArgumentException(
                "A catalog scheme is non-empty, lower-case and contains no white space.",
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

    /// <summary>Every identifier a candidate binds: its identity, the alias asked for, and the rest.</summary>
    /// <remarks>The one set <see cref="Held"/> also reads, so the two cannot disagree.</remarks>
    private static IReadOnlyCollection<ExternalId> Converging<TItem>(CatalogCandidate<TItem> candidate)
        where TItem : class, IMediaItem
        => candidate.RequestedId is { } requested
            ? [candidate.CatalogId, requested, .. candidate.Item.ExternalIds.Values]
            : [candidate.CatalogId, .. candidate.Item.ExternalIds.Values];

    /// <summary>The level a kind's items sit at, which is the key space their identifiers occupy.</summary>
    private static MediaLevelId ItemLevelOf(IMediaTypeRuntime kind) => kind.Shape.Levels[0].Id;

    /// <summary>
    /// Lists the configured catalogers that are the authority for one scheme and supply this kind's item
    /// type, whether or not they are in service.
    /// </summary>
    private IReadOnlyList<ProviderDefinition> ConfiguredAuthoritiesFor(IMediaTypeRuntime kind, string scheme)
    {
        ArgumentNullException.ThrowIfNull(kind);
        RequireCanonicalScheme(scheme, nameof(scheme));

        return
        [
            .. _definitions.Query(ProviderFamily.Cataloger, kind.Kind, enabledOnly: true)
                .Where(definition => _providers.TryGet(definition.Provider, out var registered)
                    && string.Equals(registered.CatalogScheme, scheme, StringComparison.Ordinal)
                    && registered.MediaItemType == kind.ItemType)
                .OrderBy(static definition => definition.Priority)
                .ThenBy(static definition => definition.Id),
        ];
    }

    /// <summary>Establishes who may be asked, and who owns the scheme but is backed off.</summary>
    /// <exception cref="ArronixException">
    /// No installed cataloger owns the scheme — the only negative that is an installation problem.
    /// </exception>
    /// <remarks>
    /// Backed-off owners are retained, not filtered away: they were not asked, so they cannot contribute to
    /// establishing absence, and a caller must be able to see that an authority is missing from the answer.
    /// </remarks>
    private Authorities RequireAuthorities(IMediaTypeRuntime kind, string scheme)
    {
        var configured = ConfiguredAuthoritiesFor(kind, scheme);

        if (configured.Count == 0)
        {
            throw new ArronixException(
                CoreErrorCode.CatalogSchemeUnowned,
                $"No installed cataloger is the authority for '{scheme}' in media kind '{kind.Kind}', so a "
                + "reference in that scheme cannot be resolved.");
        }

        var available = new List<ProviderDefinition>();
        var unavailable = new List<ProviderDefinition>();

        foreach (var definition in configured)
        {
            // One read each: asking twice lets a status change between the answers put an owner in both
            // sets or in neither.
            (_status.IsAvailable(definition.Id) ? available : unavailable).Add(definition);
        }

        return new Authorities(available, unavailable);
    }

    /// <summary>Why one authority contributed nothing, when it was never asked.</summary>
    private static string NotAsked(ProviderDefinition definition)
        => $"'{definition.Name}' is out of service and was not asked.";

    /// <summary>Reads the item's catalog identity and the reference the platform holds it under, if any.</summary>
    private CatalogCandidate<TItem> Candidate<TItem>(
        IMediaTypeRuntime kind,
        string catalogScheme,
        TItem item,
        ExternalId? requestedId,
        CuratedEntryId? curatedEntryId)
        where TItem : class, IMediaItem
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

        // The catalog followed its own alias when the identifier asked for is not the one it answered with.
        // Both name this record, so the candidate carries both and binds both if it is ever materialized.
        var alias = requestedId is { } requested && requested != canonical ? requested : (ExternalId?)null;
        var candidate = new CatalogCandidate<TItem>(canonical, item, Held: null, alias, curatedEntryId);

        return candidate with { Held = Held(kind, Converging(candidate)) };
    }

    /// <summary>Finds the reference the platform already holds a record under, from its own identifiers.</summary>
    /// <remarks>
    /// Every identifier is inspected and the lowest surviving assignment wins, which is the rule assignment
    /// applies. Stopping at the first match would make the answer depend on the order a cataloger listed
    /// them in, and would disagree with what materializing the same candidate produces.
    /// </remarks>
    private MediaItemRef? Held(IMediaTypeRuntime kind, IReadOnlyCollection<ExternalId> converging)
    {
        var level = ItemLevelOf(kind);
        MediaItemRef? survivor = null;

        foreach (var candidate in converging)
        {
            if (!Reader.TryFind(kind.Kind, level, candidate, out var found))
            {
                continue;
            }

            var resolved = Reader.Canonical(found);

            if (survivor is not { } best || resolved.Id.Value < best.Id.Value)
            {
                survivor = resolved;
            }
        }

        return survivor;
    }

    /// <summary>Who owns a scheme, split by whether they may be asked right now.</summary>
    /// <param name="Available">The authorities in service, best priority first.</param>
    /// <param name="Unavailable">The authorities that own the scheme and are backed off.</param>
    private readonly record struct Authorities(
        IReadOnlyList<ProviderDefinition> Available,
        IReadOnlyList<ProviderDefinition> Unavailable);
}
