using System.IO;
using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;
using Arronix.Host.Composition;
using Arronix.Host.Media;
using Arronix.Plugin.Movies;
using Arronix.Plugins.Registration;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Plugin, shape and typed-media contracts are experimental; this test drives all three end to end.
#pragma warning disable ARX0013
#pragma warning disable ARX0014
#pragma warning disable ARX0020

namespace Arronix.Host.Tests.DefinitionBinding;

/// <summary>
/// The typed path with nothing stubbed: the real extension module, the real registration gate, the
/// composition root's own engine catalog, and a real release title parsed by what comes out.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DefinitionBinderTests"/> proves the binder's rules against a model the test supplied, which is
/// the right way to assert refusal. It cannot prove the two things that matter most: that this build
/// actually registers engines, and that a media kind nobody wrote those engines against survives contact
/// with them. That is what these cases are for.
/// </para>
/// <para>
/// Nothing here constructs a shape, an intent surface or a model. <see cref="MoviesPluginModule"/> names two
/// types; everything the assertions below read was derived by the host from <see cref="Movie"/>'s properties
/// and attributes and from replaying <see cref="Movies.Configure"/>. A fixture that supplied any part of it
/// would be asserting against itself.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class TypedPathEndToEndTests
{
    private string _root = string.Empty;
    private ServiceProvider? _provider;

    /// <summary>
    /// The capabilities <c>Arronix.Plugin.Movies</c>'s own manifest declares.
    /// </summary>
    /// <remarks>
    /// Written out rather than read from the file so that a manifest edit that widens the grant shows up
    /// here as a failing capability assertion rather than as a silently wider test.
    /// </remarks>
    private static CapabilitySet MoviesCapabilities => CapabilitySet.Of(
        Capability.MediaKind,
        Capability.Parsing,
        Capability.Matching,
        Capability.Indexing,
        Capability.Quality,
        Capability.Renaming,
        Capability.Metadata,
        Capability.Notification);

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "arronix-typed-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arronix:Host:ExtensionFolder"] = Path.Combine(_root, "extensions"),
                ["Arronix:Plugins:RootFolder"] = Path.Combine(_root, "extensions"),
                ["Arronix:Library:RootFolders:0"] = _root,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddArronixHost(configuration);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [TearDown]
    public void TearDown()
    {
        _provider?.Dispose();
        _provider = null;

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Runs the extension's own configure method against a real registry, exactly as the loader does.
    /// </summary>
    /// <param name="granted">What the manifest grants it.</param>
    /// <returns>The ledger of what it registered.</returns>
    private PluginRegistrationLedger Configure(CapabilitySet? granted = null)
    {
        var module = new MoviesPluginModule();
        var ledger = new PluginRegistrationLedger(module.Id);
        var registry = new PluginRegistry(
            module.Id,
            granted ?? MoviesCapabilities,
            ledger,
            _provider!.GetRequiredService<IMediaTypeCapabilityReader>());

        module.Configure(new StubPluginContext(module.Id, registry));
        registry.Seal();

        return ledger;
    }

    private TypedContribution Contribution()
    {
        var registration = Configure().Single<IMediaTypeRegistration>();

        registration.Should().NotBeNull("the extension's whole contribution is one typed media kind");

        return new TypedContribution
        {
            Plugin = PluginId.FromString("movies"),
            PluginVersion = "0.1.0",
            Capabilities = MoviesCapabilities,
            Registration = registration!,
        };
    }

    [Test]
    public void TheCompositionRootFillsEveryEngineSlotTheBinderRequires()
    {
        var catalog = _provider!.GetRequiredService<DefinitionEngineCatalog>();

        using var scope = new AssertionScope();
        catalog.ItemStore.Should().NotBeNull("the binder refuses a kind whose item store is empty");
        catalog.Parser.Should().NotBeNull();
        catalog.Matcher.Should().NotBeNull();
        catalog.QueryPlanner.Should().NotBeNull();
        catalog.Quality.Should().NotBeNull("a declared ladder is behavior, not decoration");
        catalog.Naming.Should().NotBeNull("declared templates are behavior, not decoration");
    }

    /// <summary>
    /// The extension registers its kind by naming two types, and the registry admits it.
    /// </summary>
    [Test]
    public void TheRealExtensionModuleRegistersItsKindThroughTheOrdinaryRegistry()
    {
        var ledger = Configure();

        using var scope = new AssertionScope();
        ledger.Count.Should().Be(1, "a typed media kind is the whole of what this extension contributes");
        ledger.Single<IMediaTypeRegistration>()!.Kind.Should().Be(Movies.Kind);
        ledger.Single<IMediaTypeRegistration>()!.ItemType.Should().Be<Movie>();
    }

    /// <summary>
    /// The kind's sections are priced against the manifest, by deriving them rather than trusting them.
    /// </summary>
    /// <remarks>
    /// The reverse half of the bidirectional capability check, on the path that replaced the declarative
    /// one. Metadata is the interesting grant: it is demanded by the catalog section alone, which is the
    /// section scheduled to leave with the cataloger milestone — when it does, this case should start
    /// failing and the manifest should lose the grant.
    /// </remarks>
    [Test]
    public void RegisteringTheKindDemandsExactlyTheCapabilitiesItsSectionsAccountFor()
    {
        var ledger = Configure();

        ledger.TryVerifyDeclaredCapabilities(MoviesCapabilities, out var unsatisfied)
            .Should().BeTrue(
                "the manifest declares only what the kind's sections contribute; unaccounted for: "
                + string.Join(", ", unsatisfied));
    }

    [Test]
    public void AKindWhoseManifestWithholdsASectionsCapabilityIsRefused()
    {
        var withheld = CapabilitySet.Of(
            Capability.MediaKind,
            Capability.Parsing,
            Capability.Matching,
            Capability.Indexing,
            Capability.Quality,
            Capability.Renaming,
            Capability.Notification);

        var register = () => Configure(withheld);

        register.Should().Throw<PluginCapabilityException>()
            .Which.Required.Should().Be(
                Capability.Metadata,
                "the catalog section demands it, and a section is a contribution");
    }

    /// <summary>
    /// The derivation's output passes the host's own gate, with no fixture supplying anything.
    /// </summary>
    [Test]
    public void TheDerivedKindRegistersThroughTheHostsOwnEngines()
    {
        var binder = _provider!.GetRequiredService<MediaTypeBinder>();

        var admitted = binder.TryRegister(Contribution(), out var registered, out var defects);

        admitted.Should().BeTrue(
            "the typed path is meant to work end to end; the binder reported: "
            + string.Join("; ", defects.Select(defect => $"{defect.Path}: {defect.Message}")));

        registered!.Kind.Should().Be(Movies.Kind);
        registered.Definition.Should().NotBeNull("a declared kind's behavior stays reviewable as data");
    }

    [Test]
    public void EverySeamOnTheAdmittedKindIsALiveHostEngineForThatKind()
    {
        var binder = _provider!.GetRequiredService<MediaTypeBinder>();
        binder.TryRegister(Contribution(), out var registered, out _).Should().BeTrue();

        using var scope = new AssertionScope();
        registered!.Items.MediaKind.Should().Be(Movies.Kind);
        registered.Parser!.MediaKind.Should().Be(Movies.Kind);
        registered.Matcher!.MediaKind.Should().Be(Movies.Kind);
        registered.QueryPlanner!.MediaKind.Should().Be(Movies.Kind);
        registered.Naming!.MediaKind.Should().Be(Movies.Kind);

        // The rung-shaped quality seam is deliberately empty here, and its absence is the assertion. This
        // kind's family reads its files onto typed axes, and a seam whose whole vocabulary is a rung name
        // cannot serve one — so the host declines to build it rather than building one that answers wrongly.
        // What reads quality for this kind is the family's own model, hanging off the derived structure.
        registered.Quality.Should().BeNull();
        registered.Shape.Declaration.FormatFamilies.Should().OnlyContain(family => family.Quality != null);
    }

    /// <summary>
    /// The proof that the engines execute the derived model rather than merely being constructible from it.
    /// </summary>
    [Test]
    public void ARealReleaseTitleParsesThroughTheEngineTheDerivationBuilt()
    {
        var binder = _provider!.GetRequiredService<MediaTypeBinder>();
        binder.TryRegister(Contribution(), out var registered, out _).Should().BeTrue();

        var parsed = registered!.Parser!.Parse("Blade.Runner.2049.2017.1080p.BluRay.x264-SPARKS");

        parsed.Should().NotBeNull("the declared title patterns are what read a release, and they are live here");

        using var scope = new AssertionScope();
        parsed!.Title.Should().Be("Blade Runner 2049");
        parsed.Year.Should().Be("2017");
        parsed.ReleaseGroup.Should().Be("SPARKS");
        parsed.MediaKind.Should().Be(Movies.Kind);
    }

    /// <summary>
    /// The structure the browser renders was derived from the entity, not written down.
    /// </summary>
    [Test]
    public void TheAdmittedKindPublishesAShapeDerivedFromTheItemType()
    {
        var binder = _provider!.GetRequiredService<MediaTypeBinder>();
        binder.TryRegister(Contribution(), out var registered, out _).Should().BeTrue();

        var level = registered!.Shape.Declaration.Levels[0];

        using var scope = new AssertionScope();
        level.Fields.Should().Contain(
            field => field.FieldId == "title",
            "the entity's [Title] property is where the title field comes from");
        registered.Shape.Declaration.GroupingAxes.Should().ContainSingle(
            "Movie declares exactly one group reference, and an axis is derived from it");
    }

    /// <summary>
    /// The kind lands in the registry the imperative path uses, not in a parallel one.
    /// </summary>
    [Test]
    public void TheAdmittedKindIsVisibleThroughTheOrdinaryRegistry()
    {
        var binder = _provider!.GetRequiredService<MediaTypeBinder>();
        binder.TryRegister(Contribution(), out _, out _).Should().BeTrue();

        var registry = _provider!.GetRequiredService<IMediaKindRegistry>();

        registry.All.Should().ContainSingle(kind => kind.Kind == Movies.Kind);
    }
}
