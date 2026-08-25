using System.IO;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Configuration;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using Arronix.Plugins.Tests.Support;
using Arronix.Plugins.Versioning;
using FluentAssertions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// The load pipeline over a graph with edges, against real assemblies on disk.
/// </summary>
/// <remarks>
/// What is proved here is everything downstream of where a dependency is declared: the order packages are
/// admitted in, that a dependant of a broken package is never attempted, that a package holds its
/// dependencies from before it reads a byte until after its code is gone, and that a graph the loader
/// cannot act on admits nothing at all.
/// </remarks>
[TestFixture]
internal sealed class PackageDependencyPipelineTests
{
    private static readonly VersionRange Any = VersionRangeParser.Parse(">=0.1");

    private string _root = string.Empty;
    private PluginRuntimeOptions _options = new();
    private PluginPublicationGate _publication = new();
    private PluginRuntimeRegistry _runtime = new();
    private TokenRegistry _tokens = new();
    private PackageDependencyRegistry _dependencies = null!;
    private SharedContractStore _contracts = null!;

    [SetUp]
    public void SetUp()
    {
        var home = Directory.CreateTempSubdirectory("arronix-dependency-pipeline").FullName;
        _root = Path.Combine(home, "plugins");
        Directory.CreateDirectory(_root);

        _options = new PluginRuntimeOptions { RootFolder = _root, StateFolder = Path.Combine(home, "state") };
        _publication = new PluginPublicationGate();
        _runtime = new PluginRuntimeRegistry(_publication);
        _tokens = new TokenRegistry(_publication);
        _dependencies = new PackageDependencyRegistry(_publication);
        _contracts = new SharedContractStore();
    }

    [TearDown]
    public void TearDown()
    {
        _contracts.TryRequestUnload(out _);

        var home = Path.GetDirectoryName(_root);

        if (home is not null && Directory.Exists(home))
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Test]
    public async Task PackagesAreAdmittedInTheResolvedOrderRatherThanTheOrderTheFoldersSort()
    {
        // The folder names sort the other way round deliberately: discovery order must not decide it.
        Install("z-core", "core");
        Install("a-app", "app", ("core", ">=0.1 <1.0"));

        var results = await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        using var assertions = new AssertionScope();
        results.Select(result => result.Id!.Value.Value).Should().Equal("core", "app");
        results.Should().OnlyContain(result => result.State == PluginState.Active);
        _dependencies.RootedPackages.Select(id => id.Value).Should().Equal("core", "app");
    }

    [Test]
    public async Task ADependantOfAFailedPackageIsNeverAttemptedAndUnrelatedPackagesStillActivate()
    {
        Install("core", "core", loadable: false);
        Install("app", "app", ("core", ">=0.1 <1.0"));
        Install("bystander", "bystander");

        var results = await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);
        var byId = results.ToDictionary(result => result.Id!.Value.Value);

        using var assertions = new AssertionScope();
        byId["core"].State.Should().Be(PluginState.Quarantined);
        byId["app"].State.Should().Be(PluginState.Quarantined);
        byId["app"].Message.Should().Contain("was not attempted");
        byId["app"].Defects.Should().ContainSingle().Which.Should().Contain(
            "requires 'core'",
            "the chain stays walkable one link at a time rather than restating a root cause at every level");
        byId["bystander"].State.Should().Be(
            PluginState.Active,
            "one broken closure quarantines its closure and nothing else");
        _dependencies.RootedPackages.Select(id => id.Value).Should().Equal("bystander");
        _dependencies.Snapshot().Should().BeEmpty();
    }

    [Test]
    public async Task APackageTheResolverRefusedKeepsItsOwnDiagnosisAndIsNotLoaded()
    {
        Install("app", "app");

        const string Reason = "app requires core >=2.0, and the installed core is 1.0.0.";

        var source = new RefusingGraphSource(installed => new PackageResolutionRefusal(
            installed[0].Id,
            CoreErrorCode.PluginContractMismatch,
            Reason,
            [new ManifestDefect(
                "dependencies[0].range",
                "install a version the range admits, or widen the range.",
                CoreErrorCode.PluginDependencyUnsatisfied)],
            copies: [installed[0]]));

        var result = (await CreateLoader(source).LoadAllAsync(NoOpAdmission.Instance))
            .Should().ContainSingle().Which;

        using var assertions = new AssertionScope();
        result.State.Should().Be(PluginState.Quarantined);
        result.ErrorCode.Should().Be(CoreErrorCode.PluginContractMismatch);
        result.Message.Should().Be(Reason);
        result.Defects.Should().ContainSingle().Which.Should().Contain("widen the range");
        _dependencies.RootedPackages.Should().BeEmpty();
    }

    /// <remarks>
    /// The hold is taken before the package's own load context exists and given up after its code is gone,
    /// so a quarantine leaves the contract context holding nothing on that package's behalf while an active
    /// package keeps its hold.
    /// </remarks>
    [Test]
    public async Task AContractHoldIsTakenBeforeCodeLoadsAndAQuarantineGivesItUp()
    {
        Install("core", "core");
        Install("broken", "broken", behavior: EmittedBehavior.Throw);

        var results = await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);
        var byId = results.ToDictionary(result => result.Id!.Value.Value);

        using var assertions = new AssertionScope();
        byId["core"].State.Should().Be(PluginState.Active);
        byId["broken"].State.Should().Be(PluginState.Quarantined);
        _contracts.Holders.Select(id => id.Value).Should().BeEquivalentTo(
            ["core"],
            "an active package keeps its hold and a quarantined attempt gives its own up");
    }

    /// <remarks>
    /// The adversary runs at the one moment that would otherwise be unguarded: after the dependant resolved
    /// its dependency and before its edge exists. Its module has run by then, so a withdrawal that succeeded
    /// would leave the dependant executing against a package the platform had released.
    /// </remarks>
    [Test]
    public async Task ADependencyCannotBeWithdrawnWhileItsDependantIsBeingPrepared()
    {
        Install("core", "core");
        Install("app", "app", ("core", ">=0.1 <1.0"));

        var refusedDuringPreparation = new List<PluginId>();

        var admission = new AdversarialAdmission(manifest =>
        {
            if (manifest.Id.Value != "app")
            {
                return;
            }

            _dependencies.TryGetRooted(PluginId.FromString("core"), out var core).Should().BeTrue();

            if (!_dependencies.BeginWithdrawal(core!, out var blocked))
            {
                refusedDuringPreparation.AddRange(blocked);
            }
        });

        var results = await CreateLoader().LoadAllAsync(admission);

        using var assertions = new AssertionScope();
        refusedDuringPreparation.Should().Equal(
            [PluginId.FromString("app")],
            "the pin is taken before the dependant reads or runs anything, so the withdrawal is refused");
        results.Should().OnlyContain(result => result.State == PluginState.Active);
        _dependencies.DependantsOf(PluginId.FromString("core")).Should().Equal(PluginId.FromString("app"));
    }

    /// <remarks>
    /// The mirror case: a package whose identifier is held by a retained attempt is refused before its
    /// module is instantiated, because that attempt's code may still be resident.
    /// </remarks>
    [Test]
    public async Task ARetainedIdentifierRefusesAReplacementBeforeItsModuleIsInstantiated()
    {
        Install("core", "core");

        var loader = CreateLoader();
        var loaded = (await loader.LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        _dependencies.TryGetRooted(PluginId.FromString("core"), out var incumbent).Should().BeTrue();
        _dependencies.BeginWithdrawal(incumbent!, out _).Should().BeTrue();

        // The exact lifetime that loaded it, which is what a real failed release would hand over.
        _dependencies.RetainFailedAttempt(incumbent!, loaded.PackageLease!);

        var counted = new CountingAdmission();
        var results = await CreateLoader(admissionCheck: counted).LoadAllAsync(counted);

        using var assertions = new AssertionScope();
        results.Should().ContainSingle().Which.State.Should().Be(PluginState.Quarantined);
        results[0].Message.Should().Contain("dependencies changed while it was being prepared");
        results[0].Defects.Should().ContainSingle().Which.Should().Contain("retained");
        counted.Prepared.Should().BeEmpty(
            "refusal precedes the run: a replacement never executes against an identifier whose code may "
            + "still be resident");
    }

    [Test]
    public async Task AGraphThatDoesNotAnswerForExactlyTheInstalledPackagesFailsComposition()
    {
        Install("core", "core");
        Install("app", "app");

        var cases = new (string Because, Func<IReadOnlyList<InstalledPackage>, ResolvedPackageGraph> Build, string Expected)[]
        {
            ("a package it never answered for",
                installed => new ResolvedPackageGraph([installed[0]]),
                "installed, but the graph neither admits nor refuses it."),
            ("a package that is not installed",
                installed => new ResolvedPackageGraph(
                    installed,
                    [
                        new PackageResolutionRefusal(
                            PluginId.FromString("ghost"),
                            CoreErrorCode.PluginLoadFailure,
                            "not installed"),
                    ]),
                "package[ghost]: refused, but it is not installed."),
            ("a package whose identity it rewrote",
                installed => new ResolvedPackageGraph(
                [
                    installed[0],
                    new InstalledPackage(
                        installed[1].Id,
                        SemanticVersion.Parse("9.9.9"),
                        installed[1].Source,
                        installed[1].Folder),
                ]),
                "admitted as a different object"),
        };

        using var assertions = new AssertionScope();

        foreach (var (because, build, expected) in cases)
        {
            var load = async () => await CreateLoader(new BuildingGraphSource(build))
                .LoadAllAsync(NoOpAdmission.Instance);

            (await load.Should().ThrowAsync<InvalidOperationException>(because))
                .Which.Message.Should().Contain(expected);
        }

        _dependencies.RootedPackages.Should().BeEmpty(
            "a graph the loader cannot act on admits nothing rather than admitting what it can make sense of");
        _runtime.All.Should().BeEmpty();
    }

    [Test]
    public async Task AHostWithNoUsableResolutionAuthorityFailsDeterministically()
    {
        Install("core", "core");

        var load = async () => await CreateLoader(new BuildingGraphSource(_ => null!))
            .LoadAllAsync(NoOpAdmission.Instance);

        (await load.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("answered with no graph");
    }

    /// <remarks>
    /// A canceled host start is not an extension defect. The token is observed between packages, so the
    /// second one is never attempted and nothing is left holding preparation pins.
    /// </remarks>
    [Test]
    public async Task CancellationOfTheCallersTokenEscapesRatherThanQuarantiningTheExtension()
    {
        Install("a-core", "core");
        Install("z-app", "app");

        using var cancellation = new CancellationTokenSource();
        var admission = new AdversarialAdmission(_ => cancellation.Cancel());

        var load = async () => await CreateLoader(admissionCheck: admission)
            .LoadAllAsync(admission, cancellation.Token);

        await load.Should().ThrowAsync<OperationCanceledException>();

        using var assertions = new AssertionScope();
        _runtime.All.Select(result => result.Id!.Value.Value).Should().BeEquivalentTo(
            ["app"],
            "the package that had already committed keeps its outcome; the next one, which the graph orders "
            + "after it, is never attempted");
        _dependencies.PreparingPackages.Should().BeEmpty(
            "every attempt released what it had taken before the cancellation reached the caller");
    }

    private PluginLoader CreateLoader(
        IPackageGraphSource? graph = null,
        IPluginAdmissionCheck? admissionCheck = null)
        => new(
            Options.Create(_options),
            new PluginPlatformServices(
                new StubJsonSerializer(),
                TimeProvider.System,
                cache: new RecordingCacheProvider(),
                telemetry: new RecordingTelemetryEmitter(),
                events: new RecordingEventPublisher(),
                runtime: new StubHostRuntimeInfo(),
                operatingSystem: new StubOperatingSystemInfo()),
            _runtime,
            _tokens,
            TimeProvider.System,
            NullLogger<PluginLoader>.Instance,
            _publication,
            graph ?? new PackageDependencyResolver(),
            _contracts,
            _dependencies,
            mediaTypes: null);

    /// <summary>Installs a real compiled extension whose declaration states the given dependencies.</summary>
    private void Install(
        string folder,
        string id,
        params (string Package, string Range)[] dependencies)
        => Install(folder, id, loadable: true, EmittedBehavior.DoNothing, dependencies);

    private void Install(
        string folder,
        string id,
        bool loadable = true,
        EmittedBehavior behavior = EmittedBehavior.DoNothing,
        params (string Package, string Range)[] dependencies)
    {
        var directory = Path.Combine(_root, folder);
        var assembly = EmittedPlugin.Write(directory, id, behavior, $"Emitted.{id}");
        var entry = loadable ? Path.GetFileName(assembly) : "Absent.Assembly.dll";
        var contracts =
            $">={PluginLoader.HostContractVersion.Major}.{PluginLoader.HostContractVersion.Minor} "
            + $"<{PluginLoader.HostContractVersion.Major}.{PluginLoader.HostContractVersion.Minor + 1}";
        var declared = string.Join(
            ",",
            dependencies.Select(entry => $$"""{ "package": "{{entry.Package}}", "range": "{{entry.Range}}" }"""));

        File.WriteAllText(
            Path.Combine(directory, PluginManifestReader.FileName),
            $$"""
              {
                "schemaVersion": 1,
                "id": "{{id}}",
                "name": "{{id}}",
                "version": "0.1.0",
                "contracts": { "arronix": "{{contracts}}" },
                "entryAssembly": "{{entry}}",
                "dependencies": [{{declared}}],
                "capabilities": ["storage"]
              }
              """);
    }

    /// <summary>A resolution authority that refuses one package and admits nothing.</summary>
    private sealed class RefusingGraphSource(
        Func<IReadOnlyList<InstalledPackage>, PackageResolutionRefusal> refuse) : IPackageGraphSource
    {
        public ResolvedPackageGraph Resolve(IReadOnlyList<InstalledPackage> installed)
            => new([], [refuse(installed)]);
    }

    /// <summary>A resolution authority whose answer the test builds from what it was asked about.</summary>
    private sealed class BuildingGraphSource(
        Func<IReadOnlyList<InstalledPackage>, ResolvedPackageGraph> build) : IPackageGraphSource
    {
        public ResolvedPackageGraph Resolve(IReadOnlyList<InstalledPackage> installed) => build(installed);
    }

    /// <summary>Runs an adversary at the moment Host prepares one package.</summary>
    private sealed class AdversarialAdmission(Action<ValidatedManifest> onPrepare) : IPluginAdmissionCheck
    {
        public PluginAdmissionResult Prepare(ValidatedManifest manifest, PluginRegistrationLedger ledger)
        {
            onPrepare(manifest);
            return NoOpAdmission.Instance.Prepare(manifest, ledger);
        }

        public bool MayPublish(PluginId package, out string? refusal)
        {
            refusal = null;
            return true;
        }
    }

    /// <summary>Records which packages reached Host preparation, and therefore ran their own code.</summary>
    private sealed class CountingAdmission : IPluginAdmissionCheck
    {
        public List<string> Prepared { get; } = [];

        public PluginAdmissionResult Prepare(ValidatedManifest manifest, PluginRegistrationLedger ledger)
        {
            Prepared.Add(manifest.Id.Value);
            return NoOpAdmission.Instance.Prepare(manifest, ledger);
        }

        public bool MayPublish(PluginId package, out string? refusal)
        {
            refusal = null;
            return true;
        }
    }
}
