using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Providers;

namespace Arronix.Client.Catalog;

/// <summary>Finds catalog schemes that this installation can actually route for one kind.</summary>
internal static class CatalogSchemeDiscovery
{
    /// <summary>
    /// Returns only cataloger schemes whose admitted pairing and active configured definition both match
    /// <paramref name="kind"/>.
    /// </summary>
    public static IReadOnlyList<string> ConfiguredFor(
        MediaKindId kind,
        IEnumerable<ProviderCatalogEntry> providers,
        IEnumerable<ProviderDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(definitions);

        var usableProviders = definitions
            .Where(definition => definition.Family == ProviderFamily.Cataloger)
            .Where(definition => definition.Enabled && definition.State == DefinitionState.Active)
            .Where(definition => definition.MediaKinds.Count == 0 || definition.MediaKinds.Contains(kind))
            .Select(definition => definition.Provider)
            .ToHashSet();

        return
        [
            .. providers
                .Where(provider => provider.Family == ProviderFamily.Cataloger)
                .Where(provider => provider.PairedMediaKind == kind)
                .Where(provider => provider.CatalogScheme is { Length: > 0 })
                .Where(provider => usableProviders.Contains(provider.Provider))
                .Select(provider => provider.CatalogScheme!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }
}
