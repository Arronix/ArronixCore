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
        FloorOnlyCataloger.Constructions = 0;

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
    /// Routing compares a cataloger's declared scheme ordinally, so a scheme in any other spelling would
    /// route to nothing. It is refused at activation rather than left to fail silently later.
    /// </summary>
    [Test]
    public void ACatalogerDeclaringASchemeThatIsNotInCanonicalFormIsRefused()
    {
        var result = Prepare(registry => registry
            .AddMediaType<Works>()
            .AddCataloger<ShoutingCataloger>(Descriptor("shouting")));

        using var assertions = new AssertionScope();

        result.IsAdmitted.Should().BeFalse();
        result.Defects.Should().ContainSingle().Which.Should().Match(
            "*shouting*lower-case*Work Cataloger*");
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

    /// <summary>
    /// A registration whose contract no implementation backs is refused, and nothing is constructed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defense-in-depth case. <see cref="ProviderTypeRegistration"/> derives the contract from the
    /// implementation's own interface list, so a registration built through the registry cannot say this —
    /// which is why the ledger is written directly here. Host is the boundary that constructs, so Host
    /// checks the whole relationship rather than trusting what arrived.
    /// </para>
    /// <para>
    /// A sound provider is registered alongside it, and its constructor count is the assertion that
    /// matters: the structural check runs across the package before any activation, so an unsound sibling
    /// costs the whole package its constructor calls rather than only itself.
    /// </para>
    /// </remarks>
    [Test]
    public void AContractNoImplementationBacksIsRefusedBeforeAnythingInThePackageIsConstructed()
    {
        var result = Prepare(
            registry => registry
                .AddMediaType<Works>()
                .AddCataloger<WorkCataloger>(Descriptor("works-catalog")),
            ledger => ledger.RecordProvider(
                new ProviderTypeRegistration
                {
                    Descriptor = Descriptor("claimed"),
                    Family = ProviderFamily.Cataloger,
                    ContractType = typeof(ICataloger<Work>),
                    ImplementationType = typeof(FloorOnlyCataloger),
                    MediaItemType = typeof(Work),
                },
                Capability.Metadata));

        using var assertions = new AssertionScope();

        result.IsAdmitted.Should().BeFalse();
        result.ErrorCode.Should().Be(
            CoreErrorCode.PluginProviderContractInvalid,
            "the registration does not describe one relationship, which is not a version mismatch");
        result.Defects.Should().ContainSingle().Which.Should().Match(
            "*claimed*FloorOnlyCataloger*implements no*ICataloger`1*claim rather than a relationship*");

        WorkCataloger.Constructions.Should().Be(0);
        FloorOnlyCataloger.Constructions.Should().Be(0);
    }

    /// <summary>A contract and an item type that disagree are refused before construction.</summary>
    /// <remarks>
    /// The implementation really does implement the contract here, so an assignability check alone would
    /// pass this. What is wrong is the type argument: the erased item type Host would pair on is not the one
    /// the contract is closed over, and every later lookup keyed on it would resolve the wrong kind.
    /// </remarks>
    [Test]
    public void AContractAndItemTypeThatDisagreeAreRefusedBeforeConstruction()
    {
        var result = Prepare(
            registry => registry.AddMediaType<Works>(),
            ledger => ledger.RecordProvider(
                new ProviderTypeRegistration
                {
                    Descriptor = Descriptor("crossed"),
                    Family = ProviderFamily.Cataloger,
                    ContractType = typeof(ICataloger<Work>),
                    ImplementationType = typeof(WorkCataloger),
                    MediaItemType = typeof(UnpairedItem),
                },
                Capability.Metadata));

        using var assertions = new AssertionScope();

        result.IsAdmitted.Should().BeFalse();
        result.ErrorCode.Should().Be(CoreErrorCode.PluginProviderContractInvalid);
        result.Defects.Should().ContainSingle().Which.Should().Match(
            "*crossed*not closed over the recorded item type*UnpairedItem*");
        WorkCataloger.Constructions.Should().Be(0);
    }

    /// <summary>A family with no media pairing may not carry one.</summary>
    /// <remarks>
    /// The check reads in both directions. An indexer that arrived carrying an item type has had a pairing
    /// invented for it somewhere, and a silent one is worse than a refused one.
    /// </remarks>
    [Test]
    public void ANonMediaFamilyCarryingAnItemTypeIsRefusedBeforeConstruction()
    {
        var result = Prepare(
            registry => registry.AddMediaType<Works>(),
            ledger => ledger.RecordProvider(
                new ProviderTypeRegistration
                {
                    Descriptor = Descriptor("indexer"),
                    Family = ProviderFamily.Indexer,
                    ContractType = typeof(ICataloger<Work>),
                    ImplementationType = typeof(WorkCataloger),
                    MediaItemType = typeof(Work),
                },
                Capability.Indexing));

        using var assertions = new AssertionScope();

        result.IsAdmitted.Should().BeFalse();
        result.ErrorCode.Should().Be(CoreErrorCode.PluginProviderContractInvalid);
        result.Defects.Should().ContainSingle().Which.Should().Match(
            "*indexer*has no media pairing*Work*");
        WorkCataloger.Constructions.Should().Be(0);
    }

    /// <summary>
    /// The two provider refusals are different diagnoses and carry different codes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both refusals concern one cataloger and one item type, which is exactly why they are worth telling
    /// apart. "This registration does not describe one relationship" is a defect in the extension's own
    /// code; "this relationship is fine, and nothing installed supplies that item type" is a defect in the
    /// installation, and the operator fixes it by installing a package. An operator handed one code for
    /// both learns which of the two it is by reading prose.
    /// </para>
    /// <para>
    /// Neither is a contract <i>version</i> mismatch, which is what
    /// <see cref="CoreErrorCode.PluginContractMismatch"/> means everywhere else in the loader — the
    /// declared contract range, and shared-contract assembly identity. That code is asserted absent here
    /// rather than merely unmentioned, because reusing it is the mistake this case exists to prevent.
    /// </para>
    /// </remarks>
    [Test]
    public void AnIncoherentRegistrationAndAnUnsuppliedItemTypeAreDifferentDiagnoses()
    {
        var incoherent = Prepare(
            registry => registry.AddMediaType<Works>(),
            ledger => ledger.RecordProvider(
                new ProviderTypeRegistration
                {
                    Descriptor = Descriptor("incoherent"),
                    Family = ProviderFamily.Cataloger,
                    ContractType = typeof(ICataloger<Work>),
                    ImplementationType = typeof(FloorOnlyCataloger),
                    MediaItemType = typeof(Work),
                },
                Capability.Metadata));

        var unsupplied = Prepare(registry => registry.AddCataloger<UnpairedCataloger>(Descriptor("unsupplied")));

        using var assertions = new AssertionScope();

        incoherent.ErrorCode.Should().Be(CoreErrorCode.PluginProviderContractInvalid);
        unsupplied.ErrorCode.Should().Be(CoreErrorCode.PluginMediaPairingUnsatisfied);
        incoherent.ErrorCode.Should().NotBe(unsupplied.ErrorCode);

        new[] { incoherent.ErrorCode, unsupplied.ErrorCode }.Should().NotContain(
            CoreErrorCode.PluginContractMismatch,
            "that code means a contract version or assembly identity mismatch, which neither of these is");

        FloorOnlyCataloger.Constructions.Should().Be(0);
        UnpairedCataloger.Constructions.Should().Be(0);
    }

    /// <summary>Two kinds in one attempt cannot own one item type.</summary>
    [Test]
    public void TwoKindsInOneAttemptCannotShareAnItemType()
    {
        var result = Prepare(registry => registry
            .AddMediaType<Works>()
            .AddMediaType<RivalWorks>()
            .AddCataloger<WorkCataloger>(Descriptor("works-catalog")));

        using var assertions = new AssertionScope();

        result.IsAdmitted.Should().BeFalse();
        result.ErrorCode.Should().Be(CoreErrorCode.MediaItemTypeConflict);
        result.Defects.Should().ContainSingle().Which.Should().Match(
            "*kind[rival-works]*already supplied by media kind 'works'*");
        WorkCataloger.Constructions.Should().Be(
            0,
            "the ambiguity is in the kinds, so it is refused before a provider is even considered");
    }

    /// <summary>A kind cannot take an item type an already-active kind owns.</summary>
    /// <remarks>
    /// The same invariant across the publication boundary. The incumbent is named as the owner whichever
    /// order the attempt's own kinds arrive in, because the active kinds are walked first and in registry
    /// order.
    /// </remarks>
    [Test]
    public void AKindCannotTakeAnItemTypeAnActiveKindAlreadyOwns()
    {
        var first = Prepare(registry => registry.AddMediaType<Works>());
        first.IsAdmitted.Should().BeTrue(string.Join(" | ", first.Defects));
        first.Attempt!.TryCommit(out _, out _).Should().BeTrue();

        try
        {
            var second = Prepare(registry => registry.AddMediaType<RivalWorks>());

            using var assertions = new AssertionScope();

            second.IsAdmitted.Should().BeFalse();
            second.ErrorCode.Should().Be(CoreErrorCode.MediaItemTypeConflict);
            second.Defects.Should().ContainSingle().Which.Should().Match(
                "*kind[rival-works]*already supplied by media kind 'works'*");
        }
        finally
        {
            first.Attempt!.Rollback();
        }
    }

    private static ProviderDescriptor Descriptor(string localId) => new()
    {
        LocalId = localId,
        Name = localId,
        Settings = [],
    };

    /// <summary>Runs one extension's registration and Host's preparation, and nothing else.</summary>
    /// <param name="configure">What the extension registers through the ordinary gate.</param>
    /// <param name="inject">
    /// What is written straight into the ledger, bypassing the registry. Only an adversarial case uses it:
    /// it is how a registration the registry would refuse to build is put in front of Host.
    /// </param>
    private PluginAdmissionResult Prepare(
        Action<IPluginRegistry> configure,
        Action<PluginRegistrationLedger>? inject = null)
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
        inject?.Invoke(ledger);
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

    /// <summary>A cataloger whose declared scheme is not in the canonical form the host routes by.</summary>
    private sealed class ShoutingCataloger : WorkCataloger
    {
        public override string CatalogScheme => "Work Cataloger";
    }

    /// <summary>A cataloger paired with the fixture kind, counting the constructions Host performs.</summary>
    private class WorkCataloger : ICataloger<Work>
    {
        public virtual string CatalogScheme => "work-cataloger";

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

    /// <summary>The non-generic floor and the family marker, with no closed contract behind either.</summary>
    private sealed class FloorOnlyCataloger : IClosedCataloger
    {
        public string CatalogScheme => "floor-only-cataloger";

        public FloorOnlyCataloger() => Constructions++;

        internal static int Constructions { get; set; }

        public IReadOnlyList<ExternalIdReading> ReadExternalIds(string text) => [];

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
        public string CatalogScheme => "unpaired-cataloger";

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
