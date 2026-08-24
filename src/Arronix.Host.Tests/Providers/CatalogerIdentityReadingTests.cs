using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Host.Providers;
using Arronix.Host.Tests.TypedMedia;
using FluentAssertions;


namespace Arronix.Host.Tests.Providers;

[TestFixture]
internal sealed class CatalogerIdentityReadingTests
{
    [Test]
    public void ReadsMarkersOnlyFromCatalogersPairedWithTheRequestedItemType()
    {
        const string text = "Example Work {catalog-42}";
        var expected = new ExternalIdReading(
            ExternalId.Of("catalog", "42"),
            "{catalog-42}",
            text.IndexOf("{catalog-42}", StringComparison.Ordinal));
        var registry = new ProviderRegistry();

        registry.Register(
            PluginId.FromString("example.catalog"),
            ProviderFamily.Cataloger,
            Descriptor("matching"),
            new ReadingCataloger(expected),
            typeof(Work));
        registry.Register(
            PluginId.FromString("example.foreign"),
            ProviderFamily.Cataloger,
            Descriptor("foreign"),
            new ReadingCataloger(new ExternalIdReading(
                ExternalId.Of("foreign", "9"),
                "Example",
                0)),
            typeof(string));

        registry.ReadExternalIds(typeof(Work), text).Should().Equal(expected);
    }

    [Test]
    public void RejectsAMarkerThatTheCatalogerCannotPointToInTheSuppliedText()
    {
        var registry = new ProviderRegistry();
        registry.Register(
            PluginId.FromString("example.catalog"),
            ProviderFamily.Cataloger,
            Descriptor("catalog"),
            new ReadingCataloger(new ExternalIdReading(
                ExternalId.Of("catalog", "42"),
                "{catalog-42}",
                100)),
            typeof(Work));

        var act = () => registry.ReadExternalIds(typeof(Work), "Example Work");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*returned an external-id marker outside the supplied text*");
    }

    [Test]
    public void RejectsAMarkerFromANameOtherThanTheCatalogersDeclaredScheme()
    {
        const string text = "{catalog-42} {foreign-9}";
        var registry = new ProviderRegistry();
        registry.Register(
            PluginId.FromString("example.catalog"),
            ProviderFamily.Cataloger,
            Descriptor("catalog"),
            new ReadingCataloger(
                new ExternalIdReading(ExternalId.Of("catalog", "42"), "{catalog-42}", 0),
                new ExternalIdReading(
                    ExternalId.Of("foreign", "9"),
                    "{foreign-9}",
                    text.IndexOf("{foreign-9}", StringComparison.Ordinal))),
            typeof(Work));

        var act = () => registry.ReadExternalIds(typeof(Work), text);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*declared scheme 'catalog'*marker for 'foreign'*");
    }

    private static ProviderDescriptor Descriptor(string localId) => new()
    {
        LocalId = localId,
        Name = localId,
        Settings = []
    };

    private sealed class ReadingCataloger(params ExternalIdReading[] readings) : ICataloger<Work>
    {
        public string CatalogScheme => readings.Length > 0 ? readings[0].Id.Scheme : "reading";

        public CatalogerCapabilities Capabilities => CatalogerCapabilities.Search;

        public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text) => readings;

        public Task<IReadOnlyList<Work>> SearchAsync(
            ProviderInvocation invocation,
            CatalogQuery query,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Work>>([]);

        public Task<Work?> GetAsync(
            ProviderInvocation invocation,
            ExternalId id,
            CancellationToken cancellationToken = default) => Task.FromResult<Work?>(null);

        public Task<IReadOnlyList<ExternalId>> ChangedSinceAsync(
            ProviderInvocation invocation,
            DateTimeOffset since,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExternalId>>([]);

        public Task<ValidationOutcome> TestAsync(
            ProviderInvocation invocation,
            CancellationToken cancellationToken = default) => Task.FromResult(ValidationOutcome.Success);

        public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
            ProviderInvocation invocation,
            string optionSourceId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FacetValue>>([]);
    }
}
