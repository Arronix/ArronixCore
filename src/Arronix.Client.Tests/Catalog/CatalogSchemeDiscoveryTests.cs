using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Client.Catalog;
using FluentAssertions;

namespace Arronix.Client.Tests.Catalog;

/// <summary>What a generic catalog workspace may offer before it ever sends a search.</summary>
[TestFixture]
internal sealed class CatalogSchemeDiscoveryTests
{
    private static readonly MediaKindId Movies = MediaKindId.FromString("movies");
    private static readonly MediaKindId Books = MediaKindId.FromString("books");

    [Test]
    public void OffersOnlyConfiguredActiveCatalogersPairedWithTheSelectedKind()
    {
        var matching = Entry("tmdb", Movies, "tmdb");
        var foreign = Entry("books", Books, "goodreads");
        var unpaired = Entry("generic", null, null);
        var disabled = Entry("disabled", Movies, "disabled");

        var schemes = CatalogSchemeDiscovery.ConfiguredFor(
            Movies,
            [matching, foreign, unpaired, disabled],
            [
                Definition(matching.Provider, enabled: true, DefinitionState.Active, []),
                Definition(foreign.Provider, enabled: true, DefinitionState.Active, [Books]),
                Definition(unpaired.Provider, enabled: true, DefinitionState.Active, []),
                Definition(disabled.Provider, enabled: false, DefinitionState.Active, []),
            ]);

        schemes.Should().Equal("tmdb");
    }

    private static ProviderCatalogEntry Entry(string localId, MediaKindId? pairedKind, string? scheme)
        => new(
            ProviderId.Create(PluginId.FromString($"example.{localId}"), localId),
            ProviderFamily.Cataloger,
            new ProviderDescriptor { LocalId = localId, Name = localId, Settings = [] },
            pairedKind,
            scheme);

    private static ProviderDefinition Definition(
        ProviderId provider,
        bool enabled,
        DefinitionState state,
        IReadOnlyList<MediaKindId> kinds)
        => new()
        {
            Id = 1,
            Provider = provider,
            Family = ProviderFamily.Cataloger,
            Name = provider.Value,
            Enabled = enabled,
            State = state,
            Settings = new Dictionary<string, string>(),
            MediaKinds = kinds,
        };
}
