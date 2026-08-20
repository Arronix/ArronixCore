using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;


namespace Arronix.Host.Providers;

/// <summary>
/// One provider implementation, as the host holds it.
/// </summary>
/// <param name="Id">The host-minted identifier.</param>
/// <param name="Family">Which of the five families it belongs to.</param>
/// <param name="Descriptor">What it declares about itself and its settings.</param>
/// <param name="Provider">The implementation, which is stateless and takes its configuration per call.</param>
/// <param name="Plugin">The extension that contributed it.</param>
/// <param name="MediaItemType">The paired media item type for typed catalogers and curators.</param>
public readonly record struct RegisteredProvider(
    ProviderId Id,
    ProviderFamily Family,
    ProviderDescriptor Descriptor,
    IProvider Provider,
    PluginId Plugin,
    Type? MediaItemType);

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

    /// <summary>
    /// Gets every registered provider, ordered by identifier.
    /// </summary>
    public IReadOnlyList<RegisteredProvider> All
        => [.. _providers.Values.OrderBy(provider => provider.Id.Value, StringComparer.Ordinal)];

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
    /// Looks up a provider.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="provider">The provider when it is registered.</param>
    /// <returns><see langword="true"/> when it is registered.</returns>
    public bool TryGet(ProviderId id, out RegisteredProvider provider)
        => _providers.TryGetValue(id, out provider);

    /// <summary>
    /// Registers a provider.
    /// </summary>
    /// <param name="plugin">The contributing extension.</param>
    /// <param name="family">The family.</param>
    /// <param name="descriptor">The declaration.</param>
    /// <param name="implementation">The implementation.</param>
    /// <param name="mediaItemType">The paired media item type, when the provider is media-shaped.</param>
    /// <returns>The minted identifier.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="descriptor"/> or <paramref name="implementation"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArronixException">
    /// The extension has already registered a provider under the same local name.
    /// </exception>
    public ProviderId Register(
        PluginId plugin,
        ProviderFamily family,
        ProviderDescriptor descriptor,
        IProvider implementation,
        Type? mediaItemType = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(implementation);

        var id = ProviderId.Create(plugin, descriptor.LocalId);

        if (!_providers.TryAdd(id, new RegisteredProvider(id, family, descriptor, implementation, plugin, mediaItemType)))
        {
            throw new ArronixException(
                CoreErrorCode.PluginIdConflict,
                $"Extension '{plugin}' has already registered a provider called '{descriptor.LocalId}'.");
        }

        return id;
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
    public int RemoveByPlugin(PluginId plugin)
    {
        var owned = _providers.Values.Where(provider => provider.Plugin == plugin).Select(p => p.Id).ToList();

        foreach (var id in owned)
        {
            _providers.TryRemove(id, out _);
        }

        return owned.Count;
    }

    /// <summary>
    /// Gets a registered provider narrowed to one contract.
    /// </summary>
    /// <typeparam name="TProvider">The contract wanted.</typeparam>
    /// <param name="id">The identifier.</param>
    /// <param name="provider">The implementation, when it is registered and of that contract.</param>
    /// <returns><see langword="true"/> when it is registered and implements the contract.</returns>
    public bool TryGet<TProvider>(ProviderId id, [NotNullWhen(true)] out TProvider? provider)
        where TProvider : class, IProvider
    {
        if (_providers.TryGetValue(id, out var registered) && registered.Provider is TProvider typed)
        {
            provider = typed;
            return true;
        }

        provider = null;
        return false;
    }

    /// <summary>
    /// Reads external identity markers using the installed catalogers paired with one media item type.
    /// </summary>
    /// <param name="mediaItemType">The exact media-owned item type being interpreted.</param>
    /// <param name="text">The complete release, file, or folder name.</param>
    /// <returns>Distinct recognized identities in provider and source order.</returns>
    public IReadOnlyList<ExternalIdReading> ReadExternalIds(Type mediaItemType, string text)
    {
        ArgumentNullException.ThrowIfNull(mediaItemType);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var readings = new List<ExternalIdReading>();
        var seen = new HashSet<ExternalId>();

        foreach (var registered in All)
        {
            if (registered.Family != ProviderFamily.Cataloger
                || registered.MediaItemType != mediaItemType
                || registered.Provider is not ICataloger cataloger)
            {
                continue;
            }

            foreach (var reading in cataloger.ReadExternalIds(text).OrderBy(static reading => reading.Index))
            {
                if (reading.Index < 0
                    || reading.Index + reading.Marker.Length > text.Length
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
