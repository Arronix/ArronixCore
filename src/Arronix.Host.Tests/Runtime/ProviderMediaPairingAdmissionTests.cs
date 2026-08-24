using System.IO;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Host.Composition;
using Arronix.Host.Runtime;
using Arronix.Host.Tests.DefinitionBinding;
using Arronix.Host.Tests.TypedMedia;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// What Host does with a provider whose closed item type nothing in the installation supplies.
/// </summary>
/// <remarks>
/// <para>
/// A package dependency and a media pairing are different claims. The dependency check proves a package
/// with a given identifier is installed and compatible; it cannot prove that some media kind in the
/// installation is closed over the exact CLR type this provider's contract names. That second claim is the
/// one a typed cataloger is entirely built on, so Host checks it, and checks it before it constructs
/// anything: an extension whose contribution cannot be used has not earned a constructor call.
/// </para>
/// <para>
/// The kind and the provider here are the media-neutral <c>Works</c> fixture rather than Movies, so a
/// passing case cannot be an accident of the movie package being installed alongside.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class ProviderMediaPairingAdmissionTests
{
    private static readonly PluginId Package = PluginId.FromString("pairing.fixture");

    private string _root = string.Empty;
    private ServiceProvider? _provider;

    [SetUp]
    public void SetUp()
    {
        WorkCataloger.Constructions = 0;
        UnpairedCataloger.Constructions = 0;

        _root = Path.Combine(Path.GetTempPath(), "arronix-pairing-tests", Guid.NewGuid().ToString("N"));
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
    /// The kind the provider pairs with arrives in the same attempt, and both are prepared together.
    /// </summary>
    /// <remarks>
    /// Preparation is where the item types become comparable at all, so a provider shipped in the same
    /// package as its kind must be admitted against what that attempt just derived rather than against what
    /// the installation had published before it started.
    /// </remarks>
    [Test]
    public void AProviderIsAdmittedAgainstAKindPreparedInTheSameAttempt()
    {
        var result = Prepare(registry => registry
            .AddMediaType<Works>()
            .AddCataloger<WorkCataloger>(Descriptor("works-catalog")));

        using var assertions = new AssertionScope();

        result.IsAdmitted.Should().BeTrue(string.Join(" | ", result.Defects));
        WorkCataloger.Constructions.Should().Be(1);

        result.Attempt!.Rollback();
    }

    /// <summary>
    /// No installed kind supplies the item type, so the provider is refused and never constructed.
    /// </summary>
    [Test]
    public void AProviderWhoseItemTypeNoInstalledKindSuppliesIsRefusedBeforeItIsConstructed()
    {
        var result = Prepare(registry => registry.AddCataloger<UnpairedCataloger>(Descriptor("unpaired")));

        using var assertions = new AssertionScope();

        result.IsAdmitted.Should().BeFalse();
        result.ErrorCode.Should().Be(CoreErrorCode.PluginMediaPairingUnsatisfied);

        result.Defects.Should().ContainSingle().Which.Should().Match(
            "*unpaired*UnpairedCataloger*Cataloger*UnpairedItem*no active media kind supplies*");

        UnpairedCataloger.Constructions.Should().Be(
            0,
            "a contribution the installation has already decided it cannot use must not run extension code");
    }

    /// <summary>
    /// One unresolvable pairing stops the whole package's providers being constructed.
    /// </summary>
    /// <remarks>
    /// The refusal is transactional either way, so the difference is only whether extension constructors
    /// ran before it. Checking every registration first is what makes the answer "none of them".
    /// </remarks>
    [Test]
    public void OneUnresolvablePairingStopsEveryProviderInThePackageBeingConstructed()
    {
        var result = Prepare(registry => registry
            .AddMediaType<Works>()
            .AddCataloger<WorkCataloger>(Descriptor("works-catalog"))
            .AddCataloger<UnpairedCataloger>(Descriptor("unpaired")));

        using var assertions = new AssertionScope();

        result.IsAdmitted.Should().BeFalse();
        result.ErrorCode.Should().Be(CoreErrorCode.PluginMediaPairingUnsatisfied);
        WorkCataloger.Constructions.Should().Be(0);
        UnpairedCataloger.Constructions.Should().Be(0);
    }

    /// <summary>
    /// The diagnostic names the kinds that are installed, so an operator can act on it.
    /// </summary>
    [Test]
    public void TheRefusalNamesTheItemTypesTheInstallationDoesSupply()
    {
        var result = Prepare(registry => registry
            .AddMediaType<Works>()
            .AddCataloger<UnpairedCataloger>(Descriptor("unpaired")));

        result.Defects.Should().ContainSingle().Which.Should().Match("*'works'*Work*");
    }

    private static ProviderDescriptor Descriptor(string localId) => new()
    {
        LocalId = localId,
        Name = localId,
        Settings = [],
    };

    /// <summary>Runs one extension's registration and Host's preparation, and nothing else.</summary>
    private PluginAdmissionResult Prepare(Action<IPluginRegistry> configure)
    {
        var ledger = new PluginRegistrationLedger(Package);
        var registry = new PluginRegistry(
            Package,
            CapabilitySet.Of(
                Capability.MediaKind,
                Capability.Parsing,
                Capability.Matching,
                Capability.Indexing,
                Capability.Metadata,
                Capability.Notification,
                Capability.Quality,
                Capability.Renaming),
            ledger,
            _provider!.GetRequiredService<IMediaTypeCapabilityReader>());

        configure(registry);
        ledger.ActivationContext = new StubPluginContext(Package, registry);
        registry.Seal();

        var bootstrapper = _provider!.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();
        return bootstrapper.Admission.Prepare(Manifest(), ledger);
    }

    /// <summary>Proves a declaration the way the loader does, from a candidate found in a folder.</summary>
    private static ValidatedManifest Manifest()
    {
        var manifest = new PluginManifest
        {
            SchemaVersion = PluginManifestValidator.SupportedSchemaVersion,
            Id = Package.Value,
            Name = "Pairing fixture",
            Version = "0.1.0",
            Contracts = new ContractRequirements { Arronix = ">=0.8 <0.9" },
            EntryAssembly = "Arronix.Host.Tests.dll",
            Capabilities =
            [
                "media-kind", "parsing", "matching", "indexing", "metadata", "notification", "quality",
                "renaming",
            ],
        };

        PluginManifestValidator.TryValidate(
            new PluginCandidate(Path.Combine(Path.GetTempPath(), "arronix-pairing", "plugin.json"), manifest),
            PackageAvailability.Available,
            out var validated,
            out var defects).Should().BeTrue(string.Join(" | ", defects.Select(defect => defect.Message)));

        return validated!;
    }

    /// <summary>A cataloger paired with the fixture kind, counting the constructions Host performs.</summary>
    private sealed class WorkCataloger : ICataloger<Work>
    {
        public WorkCataloger() => Constructions++;

        internal static int Constructions { get; set; }

        public CatalogerCapabilities Capabilities => CatalogerCapabilities.Search;

        public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text) => [];

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

    /// <summary>An item type deliberately supplied by no media kind anywhere in the solution.</summary>
    private sealed class UnpairedItem : IMediaItem
    {
        public required Abstractions.Identity.MediaItemId Key { get; init; }

        public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

        public CatalogRecordState CatalogState { get; init; }

        public required string Title { get; init; }

        public Language? TitleLanguage { get; init; }

        public string? Overview { get; init; }

        public ArtworkSet Artwork { get; init; } = ArtworkSet.Empty;
    }

    /// <summary>A cataloger closed over that item type. Registering it is the whole of the defect.</summary>
    private sealed class UnpairedCataloger : ICataloger<UnpairedItem>
    {
        public UnpairedCataloger() => Constructions++;

        internal static int Constructions { get; set; }

        public CatalogerCapabilities Capabilities => CatalogerCapabilities.Search;

        public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text) => [];

        public Task<IReadOnlyList<UnpairedItem>> SearchAsync(
            ProviderInvocation invocation,
            CatalogQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnpairedItem>>([]);

        public Task<UnpairedItem?> GetAsync(
            ProviderInvocation invocation,
            ExternalId id,
            CancellationToken cancellationToken = default) => Task.FromResult<UnpairedItem?>(null);

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
