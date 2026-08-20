using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Shape;
using Arronix.Host.Configuration;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Host.Media;
using Arronix.Host.Tests.Support;
using FluentAssertions;


namespace Arronix.Host.Tests.DefinitionBinding;

/// <summary>
/// The typed path's admission half: a derived model in, engines bound, one registered media kind out — and
/// downstream of the registry, indistinguishable from an imperative kind.
/// </summary>
/// <remarks>
/// The model is handed over already derived so that a fault can be placed in it deliberately. Every rule
/// under test is a rule about a model rather than about how one was obtained;
/// <see cref="TypedPathEndToEndTests"/> is where a real derivation goes through the real engines.
/// </remarks>
[TestFixture]
internal sealed class DefinitionBinderTests
{
    private static readonly PluginId Plugin = PluginId.FromString("declarative.fixture");

    private static MediaKindRegistry EmptyRegistry()
    {
        var library = new LibraryOptions();
        library.RootFolders.Add("/library");
        return new MediaKindRegistry(TestOptions.Of(library));
    }

    private static DefinitionEngineCatalog CompleteCatalog() => new()
    {
        ItemStore = definition => new FakeItemSource(definition.Kind),
        Matcher = definition => new FakeEngineMatcher(definition.Kind),
        QueryPlanner = definition => new FakeEnginePlanner(definition.Kind),
    };

    private static readonly CapabilitySet Granted = CapabilitySet.Of(
        Capability.MediaKind,
        Capability.Parsing,
        Capability.Matching,
        Capability.Indexing);

    private static bool Register(
        MediaTypeBinder binder,
        out RegisteredMediaKind? registered,
        out IReadOnlyList<ShapeDefect> defects)
        => Register(binder, DefinitionFixtures.Sound(), out registered, out defects);

    private static bool Register(
        MediaTypeBinder binder,
        MediaKindModel model,
        out RegisteredMediaKind? registered,
        out IReadOnlyList<ShapeDefect> defects)
        => binder.TryRegister(
            Plugin,
            "0.1.0",
            Granted,
            new StubMediaType(model),
            out registered,
            out defects);

    [Test]
    public void ASoundModelIsAdmittedWithEverySeamBoundToAnEngine()
    {
        var registry = EmptyRegistry();
        var binder = new MediaTypeBinder(registry, CompleteCatalog());

        Register(binder, out var registered, out var defects)
            .Should().BeTrue(string.Join("; ", defects.Select(defect => $"{defect.Path}: {defect.Message}")));

        registered!.Kind.Should().Be(ShapeFixtures.Kind);
        registered.Items.Should().BeOfType<FakeItemSource>("the item seam is the engine the catalog built");
        registered.Parser.Should().BeOfType<Arronix.Host.Engines.Parsing.TypedReleaseParserAdapter>();
        registered.Matcher.Should().BeOfType<FakeEngineMatcher>();
        registered.QueryPlanner.Should().BeOfType<FakeEnginePlanner>();
        registered.Definition.Should().NotBeNull("a declared kind's behavior stays reviewable as data");
        registered.Definition!.Model.Should().BeSameAs(
            registry.Require(ShapeFixtures.Kind).Definition!.Model);
    }

    [Test]
    public void DownstreamOfTheRegistryTheKindLooksLikeAnyOther()
    {
        var registry = EmptyRegistry();
        var binder = new MediaTypeBinder(registry, CompleteCatalog());

        Register(binder, out _, out _);

        registry.All.Should().ContainSingle();
        var registered = registry.Require(ShapeFixtures.Kind);
        registered.Descriptor.Levels.Should().HaveCount(4, "the wire bundle is built by the same admission path");
        registered.Projection.Name.Should().Be("Fixture");
        registered.Intent.MediaKind.Should().Be(ShapeFixtures.Kind);
    }

    [Test]
    public void AMissingEngineRefusesTheKindRatherThanAdmittingUnexecutableBehavior()
    {
        var registry = EmptyRegistry();
        var catalog = CompleteCatalog();
        catalog.ItemStore = null;
        var binder = new MediaTypeBinder(registry, catalog);

        Register(binder, out var registered, out var defects).Should().BeFalse();

        registered.Should().BeNull();
        defects.Should().Contain(defect => defect.Path == "engines.itemStore");
        registry.All.Should().BeEmpty("admission is all of a media kind or none of it");
    }

    [Test]
    public void EveryMissingEngineIsNamedNotOnlyTheFirst()
    {
        var registry = EmptyRegistry();
        var binder = new MediaTypeBinder(registry, new DefinitionEngineCatalog());

        Register(binder, out _, out var defects).Should().BeFalse();

        defects.Select(defect => defect.Path).Should().BeEquivalentTo(
            "engines.itemStore",
            "engines.matcher",
            "engines.queryPlanner");
    }

    [Test]
    public void TheTypedParserCannotReportAKindDifferentFromItsMediaType()
    {
        var registry = EmptyRegistry();
        var binder = new MediaTypeBinder(registry, CompleteCatalog());

        Register(binder, out var registered, out var defects).Should().BeTrue();

        defects.Should().BeEmpty();
        registered!.Parser.Should().NotBeNull();
        registered.Parser!.MediaKind.Should().Be(ShapeFixtures.Kind);
        registry.All.Should().ContainSingle();
    }

    [Test]
    public void AnInvalidModelNeverReachesTheEngines()
    {
        var registry = EmptyRegistry();
        var built = false;
        var catalog = CompleteCatalog();
        catalog.ItemStore = definition =>
        {
            built = true;
            return new FakeItemSource(definition.Kind);
        };
        var binder = new MediaTypeBinder(registry, catalog);

        var faulty = DefinitionFixtures.Sound() with
        {
            Querying = DefinitionFixtures.Sound().Querying with
            {
                Tiers =
                [
                    DefinitionFixtures.Sound().Querying.Tiers[0] with
                    {
                        SearchKindId = "undeclared-search",
                    },
                ],
            },
        };

        Register(binder, faulty, out _, out var defects).Should().BeFalse();

        defects.Should().Contain(defect => defect.Path == "querying.tiers[0]");
        built.Should().BeFalse("engines are constructed from validated models only");
    }

    [Test]
    public void BothRegistrationPathsFeedOnePipelineSoAKindCannotBeClaimedTwice()
    {
        var registry = EmptyRegistry();

        // The imperative path claims the kind first, exactly as a not-yet-converted plugin does.
        registry.TryRegister(ContributionFixtures.For(ShapeFixtures.Layered()), out _, out _)
            .Should().BeTrue();

        var binder = new MediaTypeBinder(registry, CompleteCatalog());

        Register(binder, out _, out var defects).Should().BeFalse();

        defects.Should().Contain(defect => defect.Path == "kind");
        registry.All.Should().ContainSingle("the imperative claim stands; the typed one was refused");
    }

    [Test]
    public void AnImperativeKindCarriesNoDefinition()
    {
        var registry = EmptyRegistry();

        registry.TryRegister(ContributionFixtures.For(ShapeFixtures.Layered()), out var registered, out _)
            .Should().BeTrue();

        registered!.Definition.Should().BeNull();
    }

    private sealed class FakeEngineMatcher(MediaKindId kind) : IReleaseMatcher
    {
        public MediaKindId MediaKind => kind;

        public Task<MatchOutcome> MatchAsync(MatchRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The binder tests never match.");
    }

    private sealed class FakeEnginePlanner(MediaKindId kind) : IReleaseQueryPlanner
    {
        public MediaKindId MediaKind => kind;

        public Task<ReleaseQueryPlan> PlanAsync(
            AcquisitionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The binder tests never plan.");
    }

    /// <summary>
    /// A derived model the test supplied rather than derived.
    /// </summary>
    /// <remarks>
    /// The binder's admission half reads only the kind, the shape, the intent and the model, so those are
    /// the only members that do anything here. Projection is a derivation concern and is never reached from
    /// admission; a caller that reaches for it is asking this stub the wrong question, which is why it says
    /// so rather than returning an empty view.
    /// </remarks>
    private sealed class StubMediaType(MediaKindModel model) : IMediaTypeRuntime
    {
        public MediaKindId Kind => ShapeFixtures.Kind;

        public Type ItemType => typeof(object);

        public Type TargetType => typeof(object);

        public Type ReleaseType => typeof(object);

        public Type ParserType => typeof(object);

        public IReadOnlyList<Type> GroupTypes => [];

        public MediaShape Shape { get; } = DefinitionFixtures.Shape();

        public PluginIntentSurface Intent { get; } = DefinitionFixtures.Intent();

        public MediaKindModel Model { get; } = model;

        public bool HasReleasePolicy => false;

        public IRelease? Parse(Arronix.Abstractions.Parsing.ReleaseParseContext context) => null;

        public ItemView Project(object item) => throw new NotSupportedException(
            "Admission never projects an item; this stub carries a model, not a catalog.");

        public FieldValue Read(object item, string fieldId) => throw new NotSupportedException(
            "Admission never reads an item; this stub carries a model, not a catalog.");
    }
}
