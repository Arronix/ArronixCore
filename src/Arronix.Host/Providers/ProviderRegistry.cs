using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Common.Contributions;
using Arronix.Plugins.Registry;


namespace Arronix.Host.Providers;

/// <summary>
/// One provider implementation, as the host holds it.
/// </summary>
public sealed class RegisteredProvider
{
    internal RegisteredProvider(
        ProviderId id,
        ProviderFamily family,
        ProviderDescriptor descriptor,
        IProvider provider,
        PluginId plugin,
        MediaKindId? pairedMediaKind,
        IInvocationLifetime? lifetime = null)
    {
        Lifetime = lifetime;
        Id = id;
        Family = family;
        Descriptor = descriptor;
        Provider = provider;
        Plugin = plugin;
        PairedMediaKind = pairedMediaKind;
        CatalogScheme = family == ProviderFamily.Cataloger && provider is ICataloger cataloger
            ? cataloger.CatalogScheme
            : null;
        Catalog = new ProviderCatalogEntry(id, family, descriptor, PairedMediaKind, CatalogScheme);
    }

    /// <summary>Gets the host-minted identifier.</summary>
    public ProviderId Id { get; }

    /// <summary>Gets which provider family this implementation belongs to.</summary>
    public ProviderFamily Family { get; }

    /// <summary>Gets what the provider declares about itself and its settings.</summary>
    public ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Gets the stateless implementation.
    /// </summary>
    /// <remarks>
    /// Internal, and reachable only from a leased handle, because calling it without the contributing
    /// extension's ticket lets teardown dispose the object and unload its code while the call is running.
    /// Consumers outside the host read <see cref="Descriptor"/>, <see cref="Catalog"/> and
    /// <see cref="Id"/>, which are declarations rather than executable code.
    /// </remarks>
    internal IProvider Provider { get; }

    /// <summary>Gets the contributing extension's licence to be called, when an extension contributed it.</summary>
    internal IInvocationLifetime? Lifetime { get; }

    /// <summary>Gets the extension which contributed the implementation.</summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Gets the one media kind paired with this provider's closed contract.
    /// </summary>
    /// <remarks>
    /// Admission resolves the extension-owned closed item type to this semantic identifier before this
    /// registration is created. Retaining the identifier rather than the CLR type avoids pinning an
    /// unloadable extension context and keeps routing independent of an implementation artifact.
    /// </remarks>
    public MediaKindId? PairedMediaKind { get; }

    /// <summary>Gets the external identifier scheme a cataloger declared it is the authority for.</summary>
    /// <remarks>
    /// Read once, from the implementation's own declaration, because only the cataloger knows it. Catalog
    /// work routes by this rather than by <see cref="Id"/> or implementation type.
    /// </remarks>
    public string? CatalogScheme { get; }

    /// <summary>Gets the entry a consumer configures this provider from.</summary>
    /// <remarks>
    /// Built here because both of the facts a consumer cannot derive — the qualified identifier and the
    /// family — are host-owned, and the third is the extension's own declaration.
    /// </remarks>
    public ProviderCatalogEntry Catalog { get; }
}

/// <summary>
/// Every provider implementation a loaded extension contributed.
/// </summary>
/// <remarks>
/// <para>
/// Identity is minted here, by qualifying the extension's own local name with the extension's identifier.
/// One surveyed application resolves implementations by type name, compared case-insensitively — a fragile
/// identity that works only because that application has exactly one extension. A unified host will have
/// name collisions across extensions on its first day.
/// </para>
/// <para>
/// Registration takes the declaration and the implementation together and holds no per-definition state.
/// Providers are stateless by contract: what would have been mutable per-instance configuration arrives as
/// an argument on every call. That is the direct fix for a surveyed pattern that assigns a definition onto a
/// container-resolved singleton before each use, which is racy under a unified host and would make
/// capability gating racy with it.
/// </para>
/// </remarks>
public sealed class ProviderRegistry
{
    private readonly ConcurrentDictionary<ProviderId, RegisteredProvider> _providers = new();
    private readonly PluginPublicationGate _publication;

    /// <summary>Creates a standalone provider registry with its own publication boundary.</summary>
    public ProviderRegistry()
        : this(new PluginPublicationGate())
    {
    }

    /// <summary>Creates a provider registry participating in one publication boundary.</summary>
    public ProviderRegistry(PluginPublicationGate publication)
    {
        _publication = publication ?? throw new ArgumentNullException(nameof(publication));
    }

    /// <summary>Gets the publication boundary this registry participates in.</summary>
    internal PluginPublicationGate PublicationGate => _publication;

    /// <summary>
    /// Gets every registered provider, ordered by identifier.
    /// </summary>
    public IReadOnlyList<RegisteredProvider> All
    {
        get
        {
            using var publication = _publication.EnterRead();
            return [.. _providers.Values.OrderBy(provider => provider.Id.Value, StringComparer.Ordinal)];
        }
    }

    /// <summary>
    /// Lists the declarations of one family, or of every family.
    /// </summary>
    /// <param name="family">The family, or <see langword="null"/> for all of them.</param>
    /// <returns>The declarations, ordered by identifier.</returns>
    public IReadOnlyList<RegisteredProvider> OfFamily(ProviderFamily? family)
        => family is { } wanted
            ? [.. All.Where(provider => provider.Family == wanted)]
            : All;

    /// <summary>
    /// Looks up a provider's declaration.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="provider">The provider when it is registered.</param>
    /// <returns><see langword="true"/> when it is registered.</returns>
    /// <remarks>
    /// Reads identity, family and descriptor. The implementation itself is not reachable from here; a
    /// caller that is going to invoke it takes <c>TryLease</c> instead.
    /// </remarks>
    public bool TryGet(ProviderId id, [NotNullWhen(true)] out RegisteredProvider? provider)
    {
        using var publication = _publication.EnterRead();
        return _providers.TryGetValue(id, out provider);
    }

    /// <summary>
    /// Takes a provider together with its contributing extension's lease.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="leased">The provider and its ticket. Dispose it when the call has finished.</param>
    /// <returns>
    /// <see langword="false"/> when nothing is registered under that identifier, or the extension that
    /// contributed it is being withdrawn.
    /// </returns>
    /// <remarks>
    /// Selection and the ticket are taken under one publication read lease, so a provider cannot be
    /// withdrawn between being found and being called.
    /// </remarks>
    internal bool TryLease(ProviderId id, [NotNullWhen(true)] out Leased<RegisteredProvider>? leased)
    {
        using var publication = _publication.EnterRead();

        if (_providers.TryGetValue(id, out var registered))
        {
            leased = new Leased<RegisteredProvider>(registered, Ticket(registered));
            return true;
        }

        leased = null;
        return false;
    }

    /// <summary>
    /// Takes a provider narrowed to one contract, together with its contributing extension's lease.
    /// </summary>
    /// <typeparam name="TProvider">The contract wanted.</typeparam>
    /// <param name="id">The identifier.</param>
    /// <param name="leased">The implementation and its ticket.</param>
    /// <returns><see langword="true"/> when it is registered, of that contract, and still callable.</returns>
    internal bool TryLease<TProvider>(ProviderId id, [NotNullWhen(true)] out Leased<TProvider>? leased)
        where TProvider : class, IProvider
    {
        using var publication = _publication.EnterRead();

        if (_providers.TryGetValue(id, out var registered) && registered.Provider is TProvider typed)
        {
            leased = new Leased<TProvider>(typed, Ticket(registered));
            return true;
        }

        leased = null;
        return false;
    }

    /// <summary>
    /// Takes every registered provider together with the leases that keep them callable.
    /// </summary>
    /// <param name="family">The family, or <see langword="null"/> for all of them.</param>
    /// <returns>
    /// The set, ordered by identifier. Dispose the set itself: releasing element by element inside a loop
    /// leaks every lease after one whose callback threw, and a leaked lease is an extension that can never
    /// be torn down.
    /// </returns>
    internal LeasedSet<RegisteredProvider> LeaseAll(ProviderFamily? family = null)
    {
        var leased = new List<Leased<RegisteredProvider>>();
        var set = new LeasedSet<RegisteredProvider>(leased);

        try
        {
            using var publication = _publication.EnterRead();

            foreach (var registered in _providers.Values
                         .Where(provider => family is not { } wanted || provider.Family == wanted)
                         .OrderBy(provider => provider.Id.Value, StringComparer.Ordinal))
            {
                leased.Add(new Leased<RegisteredProvider>(registered, Ticket(registered)));
            }
        }
        catch
        {
            set.Dispose();
            throw;
        }

        return set;
    }

    /// <summary>
    /// Takes the ticket a published provider's extension must still be able to give.
    /// </summary>
    /// <remarks>
    /// Withdrawal removes the provider and closes its extension's lifetime inside one publication write
    /// lease, so a provider that is still published while its lifetime refuses is a lifecycle defect: it
    /// would let teardown dispose an object a caller is about to invoke. Reported rather than skipped,
    /// because skipping it silently turns a broken invariant into a missing search result.
    /// </remarks>
    private static IDisposable? Ticket(RegisteredProvider registered)
    {
        if (registered.Lifetime is not { } lifetime)
        {
            // Registered by the host itself: no extension runtime behind it, and nothing to wait for.
            return null;
        }

        if (lifetime.TryEnter(out var ticket))
        {
            return ticket;
        }

        throw new InvalidOperationException(
            $"Provider '{registered.Id}' is still published while extension '{registered.Plugin}' is closed "
            + "to invocation. Removing a contribution and closing its runtime are one transition under the "
            + "publication write gate, so this is a lifecycle defect rather than an ordinary race.");
    }

    /// <summary>
    /// Registers a provider.
    /// </summary>
    /// <param name="plugin">The contributing extension.</param>
    /// <param name="family">The family.</param>
    /// <param name="descriptor">The declaration.</param>
    /// <param name="implementation">The implementation.</param>
    /// <param name="pairedMediaKind">The paired media kind, when the provider is media-shaped.</param>
    /// <returns>The minted identifier.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="descriptor"/> or <paramref name="implementation"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArronixException">
    /// The extension has already registered a provider under the same local name.
    /// </exception>
    internal ProviderId Register(
        PluginId plugin,
        ProviderFamily family,
        ProviderDescriptor descriptor,
        IProvider implementation,
        MediaKindId? pairedMediaKind = null)
    {
        if (!TryPrepare(plugin, family, descriptor, implementation, pairedMediaKind, out var candidate, out var error))
        {
            throw new ArronixException(CoreErrorCode.PluginIdConflict, error!);
        }

        if (!TryPublish(candidate, out error))
        {
            throw new ArronixException(CoreErrorCode.PluginIdConflict, error!);
        }

        return candidate.Id;
    }

    /// <summary>Builds and validates one provider candidate without publishing it.</summary>
    internal bool TryPrepare(
        PluginId plugin,
        ProviderFamily family,
        ProviderDescriptor descriptor,
        IProvider implementation,
        MediaKindId? pairedMediaKind,
        out RegisteredProvider candidate,
        out string? error,
        IInvocationLifetime? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(implementation);

        var id = ProviderId.Create(plugin, descriptor.LocalId);

        // Copied here, at admission, because the declaration is retained for as long as the provider is
        // published and is serialized to every consumer that lists providers. A plugin-defined collection
        // left in it would run extension code, and pin its context, long after any invocation lease.
        candidate = new RegisteredProvider(
            id,
            family,
            Media.PluginBoundary.Snapshot(descriptor),
            implementation,
            plugin,
            pairedMediaKind,
            lifetime);

        using var publication = _publication.EnterRead();
        if (_providers.ContainsKey(id))
        {
            error = $"Extension '{plugin}' has already registered a provider called '{descriptor.LocalId}'.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Publishes one already-built provider candidate.</summary>
    internal bool TryPublish(RegisteredProvider candidate, [NotNullWhen(false)] out string? error)
    {
        using var publication = _publication.EnterWrite();
        if (!_providers.TryAdd(candidate.Id, candidate))
        {
            error = $"Extension '{candidate.Plugin}' has already registered provider '{candidate.Id}'.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Removes exactly one provider candidate and never a later replacement.</summary>
    internal bool Remove(RegisteredProvider candidate)
    {
        using var publication = _publication.EnterWrite();
        return ((ICollection<KeyValuePair<ProviderId, RegisteredProvider>>)_providers)
            .Remove(new KeyValuePair<ProviderId, RegisteredProvider>(candidate.Id, candidate));
    }

    /// <summary>
    /// Withdraws every provider an extension contributed.
    /// </summary>
    /// <param name="plugin">The extension.</param>
    /// <returns>How many providers were withdrawn.</returns>
    /// <remarks>
    /// Withdrawing an implementation never deletes the definitions configured against it. Those are marked
    /// orphaned and kept, which is the deliberate inversion of a surveyed behavior that deletes stored
    /// configuration whose implementation has gone missing — under an extension model that means
    /// uninstalling an extension destroys the operator's configuration.
    /// </remarks>
    internal int RemoveByPlugin(PluginId plugin)
    {
        using var publication = _publication.EnterWrite();
        var owned = _providers.Values.Where(provider => provider.Plugin == plugin).Select(p => p.Id).ToList();

        foreach (var id in owned)
        {
            _providers.TryRemove(id, out _);
        }

        return owned.Count;
    }

    /// <summary>
    /// Reads external identity markers using the installed catalogers paired with one media kind.
    /// </summary>
    /// <param name="mediaKind">The exact media kind being interpreted.</param>
    /// <param name="text">The complete release, file, or folder name.</param>
    /// <returns>Distinct recognized identities in provider and source order.</returns>
    /// <exception cref="InvalidOperationException">
    /// A cataloger returns a malformed marker or an identifier outside its declared scheme.
    /// </exception>
    public IReadOnlyList<ExternalIdReading> ReadExternalIds(MediaKindId mediaKind, string text)
    {
        if (string.IsNullOrWhiteSpace(mediaKind.Value))
        {
            throw new ArgumentException("A media kind is required.", nameof(mediaKind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var readings = new List<ExternalIdReading>();
        var seen = new HashSet<ExternalId>();

        // One using around the whole loop: a cataloger that throws must not leave the leases of the
        // catalogers after it held forever, which would make teardown wait for a call nobody is making.
        using var catalogers = LeaseAll(ProviderFamily.Cataloger);

        foreach (var registered in catalogers)
        {
            if (registered.PairedMediaKind != mediaKind
                || registered.Provider is not ICataloger cataloger
                || registered.CatalogScheme is not { } catalogScheme)
            {
                continue;
            }

            // Copied out of the cataloger's own collection before it is enumerated, so a lazy sequence
            // cannot call back into the extension part-way through the checks below.
            var read = Media.PluginBoundary.Snapshot(cataloger.ReadExternalIds(text));

            foreach (var reading in read.OrderBy(static reading => reading.Index))
            {
                if (!string.Equals(reading.Id.Scheme, catalogScheme, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(reading.Id.Value))
                {
                    throw new InvalidOperationException(
                        $"Cataloger '{registered.Id}' declared scheme '{catalogScheme}' but returned an "
                        + $"external-id marker for '{reading.Id.Scheme}'.");
                }

                if (string.IsNullOrEmpty(reading.Marker)
                    || reading.Index < 0
                    || reading.Marker.Length > text.Length - reading.Index
                    || !string.Equals(
                        text.Substring(reading.Index, reading.Marker.Length),
                        reading.Marker,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cataloger '{registered.Id}' returned an external-id marker outside the supplied text.");
                }

                if (seen.Add(reading.Id))
                {
                    readings.Add(reading);
                }
            }
        }

        return readings;
    }
}
