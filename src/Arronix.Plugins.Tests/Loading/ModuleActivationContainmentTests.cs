using System.IO;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Plugins.Configuration;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using Arronix.Plugins.Tests.Support;
using FluentAssertions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Arronix.Plugins.Tests.Loading;

/// <summary>
/// What happens when a package's own code fails before it has registered anything.
/// </summary>
/// <remarks>
/// <para>
/// Two places run package code before registration: the entry module's constructor, which reflection invokes
/// and whose failure it wraps, and the identifier getter, which the loader calls directly. Both are
/// package-controlled, so both contain an unfamiliar failure as a quarantine — an allowlist there would let a
/// novel extension bug stop the whole installation — and neither absorbs a condition in which the process is
/// no longer sound.
/// </para>
/// <para>
/// The fixture declares its own exception type, so "unfamiliar" is literal: no policy in the platform names
/// it. The process-fatal cases arrive both wrapped, from the constructor, and direct, from the getter.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class ModuleActivationContainmentTests
{
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
        var home = Directory.CreateTempSubdirectory("arronix-module-activation").FullName;
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

    /// <param name="fault">Where the package throws its own exception type.</param>
    /// <param name="expected">The phrase the operator-facing message must carry.</param>
    [TestCase(EmittedFault.ConstructorThrowsNovel, "threw while being constructed")]
    [TestCase(EmittedFault.IdGetterThrowsNovel, "threw while reporting its identity")]
    public async Task AnUnfamiliarPackageFailureQuarantinesItAndUnrelatedPackagesStillActivate(
        EmittedFault fault,
        string expected)
    {
        Install("broken", "broken", fault);
        Install("bystander", "bystander");

        var results = await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);
        var byId = results.ToDictionary(result => result.Id!.Value.Value);

        using var assertions = new AssertionScope();

        byId["broken"].State.Should().Be(PluginState.Quarantined);
        byId["broken"].ErrorCode.Should().Be(CoreErrorCode.PluginLoadFailure);
        byId["broken"].Message.Should().Contain(expected);
        byId["broken"].Message.Should().Contain(
            "nothing has seen before",
            "the package's own exception type is reported rather than replaced by a platform phrase");

        byId["bystander"].State.Should().Be(
            PluginState.Active,
            "an exception type no allowlist names must not stop the rest of the installation");

        _dependencies.RootedPackages.Select(id => id.Value).Should().Equal("bystander");
    }

    /// <remarks>
    /// Both conditions from both boundaries. The constructor cases arrive wrapped in a reflection failure, so
    /// the filter has to walk the chain — a rule reading only the outer type would absorb an exhausted
    /// process as an ordinary construction defect — and the identifier getter cases arrive direct, where the
    /// outer type is the condition itself.
    /// </remarks>
    /// <param name="fault">Where and how the package fails fatally.</param>
    /// <param name="fatal">The type that must reach the caller, or its wrapper.</param>
    [TestCase(EmittedFault.ConstructorThrowsOutOfMemory, typeof(System.Reflection.TargetInvocationException))]
    [TestCase(EmittedFault.ConstructorThrowsCanceled, typeof(System.Reflection.TargetInvocationException))]
    [TestCase(EmittedFault.IdGetterThrowsOutOfMemory, typeof(OutOfMemoryException))]
    [TestCase(EmittedFault.IdGetterThrowsCanceled, typeof(OperationCanceledException))]
    public async Task AProcessFatalOrCanceledConditionPropagatesRatherThanQuarantining(
        EmittedFault fault,
        Type fatal)
    {
        Install("broken", "broken", fault);

        var load = async () => await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        using var assertions = new AssertionScope();

        (await load.Should().ThrowAsync<Exception>()).Which.Should().BeOfType(fatal);

        _runtime.All.Should().BeEmpty("a propagated fatal condition records no outcome for the package");
        _dependencies.RootedPackages.Should().BeEmpty();
        _dependencies.PreparingPackages.Should().BeEmpty(
            "the attempt still released everything it had taken on the way out");
    }

    /// <remarks>
    /// A module whose identifier getter fails was still constructed, so it is still the package's live
    /// object. The loader keeps it and disposes it before the load context is unloaded, whether the getter's
    /// failure was contained or propagated.
    /// </remarks>
    /// <param name="fault">How the identifier getter fails.</param>
    /// <param name="propagated">The type that reaches the caller, or <see langword="null"/> when contained.</param>
    [TestCase(EmittedFault.IdGetterThrowsNovel, null)]
    [TestCase(EmittedFault.IdGetterThrowsOutOfMemory, typeof(OutOfMemoryException))]
    [TestCase(EmittedFault.IdGetterThrowsCanceled, typeof(OperationCanceledException))]
    public async Task AModuleConstructedBeforeAFailingIdentityGetterIsStillDisposed(
        EmittedFault fault,
        Type? propagated)
    {
        var marker = Path.Combine(_root, "..", "disposed.marker");
        Install("broken", "broken", fault, marker);

        var load = async () => await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        if (propagated is null)
        {
            (await load.Should().NotThrowAsync()).Which.Should().ContainSingle()
                .Which.State.Should().Be(PluginState.Quarantined);
        }
        else
        {
            (await load.Should().ThrowAsync<Exception>()).Which.Should().BeOfType(propagated);
        }

        File.Exists(marker).Should().BeTrue(
            "the module was constructed, so it is the package's live object and is released before its "
            + "context is unloaded");
    }

    private PluginLoader CreateLoader()
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
            new PackageDependencyResolver(),
            _contracts,
            _dependencies,
            mediaTypes: null);

    private void Install(
        string folder,
        string id,
        EmittedFault fault = EmittedFault.None,
        string? disposalMarker = null)
    {
        var directory = Path.Combine(_root, folder);
        var assembly = EmittedPlugin.Write(
            directory,
            id,
            EmittedBehavior.DoNothing,
            $"Emitted.{id}",
            moduleCount: 1,
            fault,
            disposalMarker);

        var contracts =
            $">={PluginLoader.HostContractVersion.Major}.{PluginLoader.HostContractVersion.Minor} "
            + $"<{PluginLoader.HostContractVersion.Major}.{PluginLoader.HostContractVersion.Minor + 1}";

        File.WriteAllText(
            Path.Combine(directory, PluginManifestReader.FileName),
            $$"""
              {
                "schemaVersion": 1,
                "id": "{{id}}",
                "name": "{{id}}",
                "version": "0.1.0",
                "contracts": { "arronix": "{{contracts}}" },
                "entryAssembly": "{{Path.GetFileName(assembly)}}",
                "capabilities": ["storage"]
              }
              """);
    }
}
