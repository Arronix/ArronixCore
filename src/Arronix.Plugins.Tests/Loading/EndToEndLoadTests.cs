using System.IO;
using System.Runtime.Loader;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Configuration;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using Arronix.Plugins.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;


namespace Arronix.Plugins.Tests.Loading;

/// <summary>
/// The whole pipeline against a real, compiled extension.
/// </summary>
/// <remarks>
/// This is where the isolation design is actually proved. The extension is a genuine assembly on disk, it
/// is loaded into its own load context, and the host casts what it finds to its own interface. That cast
/// succeeds only if the contract assembly unified — which is the failure every other test in the suite can
/// only assert indirectly, because a fixture constructed inside the host's own context cannot fail it.
/// </remarks>
[TestFixture]
public sealed class EndToEndLoadTests
{
    private string _home = string.Empty;
    private string _root = string.Empty;
    private PluginRuntimeOptions _options = new();
    private PluginPublicationGate _publication = new();
    private LoaderAuthorities _authorities = new(new PluginPublicationGate());
    private PluginRuntimeRegistry _registry = new();
    private TokenRegistry _tokens = new();

    [SetUp]
    public void SetUp()
    {
        _home = Directory.CreateTempSubdirectory("arronix-endtoend").FullName;
        _root = Path.Combine(_home, "plugins");
        Directory.CreateDirectory(_root);

        _options = new PluginRuntimeOptions
        {
            RootFolder = _root,
            StateFolder = Path.Combine(_home, "state")
        };
        _publication = new PluginPublicationGate();
        _authorities = new LoaderAuthorities(_publication);
        _registry = new PluginRuntimeRegistry(_publication);
        _tokens = new TokenRegistry(_publication);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private PluginLoader CreateLoader() => new(
        Options.Create(_options),
        new PluginPlatformServices(
            new StubJsonSerializer(),
            TimeProvider.System,
            new RecordingCacheProvider(),
            new RecordingTelemetryEmitter(),
            new RecordingEventPublisher(),
            new StubHostRuntimeInfo(),
            new StubOperatingSystemInfo()),
        _registry,
        _tokens,
        TimeProvider.System,
        NullLogger<PluginLoader>.Instance,
        _publication,
        _authorities.Graph,
        _authorities.Contracts,
        _authorities.Dependencies);

    /// <summary>
    /// Installs a compiled extension declaring one privilege that no registration can account for, so that
    /// the forward check has nothing to object to and the pipeline runs to the end.
    /// </summary>
    private string Install(
        string pluginId,
        EmittedBehavior behavior = EmittedBehavior.DoNothing,
        string capabilities = "\"storage\"",
        int moduleCount = 1)
    {
        var folder = Path.Combine(_root, pluginId);
        var assembly = EmittedPlugin.Write(folder, pluginId, behavior, moduleCount: moduleCount);

        File.WriteAllText(
            Path.Combine(folder, PluginManifestReader.FileName),
            $$"""
              {
                "schemaVersion": 0,
                "id": "{{pluginId}}",
                "name": "Emitted",
                "version": "0.1.0",
                "contracts": { "arronix": ">={{PluginLoader.HostContractVersion.Major}}.{{PluginLoader.HostContractVersion.Minor}} <{{PluginLoader.HostContractVersion.Major}}.{{PluginLoader.HostContractVersion.Minor + 1}}" },
                "entryAssembly": "{{Path.GetFileName(assembly)}}",
                "capabilities": [{{capabilities}}]
              }
              """);

        return folder;
    }

    [Test]
    public async Task ARealExtensionReachesActiveAndItsModuleCastSucceeded()
    {
        Install("emitted");

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.State.Should().Be(
            PluginState.Active,
            "reaching Active means the module was cast to the host's own interface, which only works if the contract assembly unified");
        result.ErrorCode.Should().BeNull();
        result.Id.Should().Be(PluginId.FromString("emitted"));
        result.Ledger.Should().NotBeNull();
        result.LoadContext.Should().NotBeNull();
    }

    [Test]
    public async Task TheExtensionIsLoadedIntoItsOwnContextAndTheContractIsNot()
    {
        Install("emitted");

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;
        var context = result.LoadContext!;

        context.Name.Should().Be("arronix-plugin:emitted");
        AssemblyLoadContext.GetLoadContext(typeof(IPluginModule).Assembly).Should().BeSameAs(
            AssemblyLoadContext.Default,
            "the contract assembly stays in the default context so that both sides speak about the same types");
    }

    [Test]
    public async Task ItsOwnFoldersExistBeforeItWasActivated()
    {
        Install("emitted");

        await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        var home = Path.Combine(_options.StateFolder, "emitted");
        Directory.Exists(Path.Combine(home, "data")).Should().BeTrue();
        Directory.Exists(Path.Combine(home, "cache")).Should().BeTrue();
        Directory.Exists(Path.Combine(home, "temp")).Should().BeTrue();
    }

    [Test]
    public async Task ReachingForAPrivilegeItDidNotDeclareQuarantinesIt()
    {
        Install("emitted", EmittedBehavior.ReachForTheNetwork);

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.State.Should().Be(PluginState.Quarantined);
        result.ErrorCode.Should().Be(CoreErrorCode.PluginCapabilityMissing);
        result.Message.Should().Contain(CapabilityNames.Network);
    }

    [Test]
    public async Task AnExtensionThatThrowsQuarantinesItselfAndNeverTheHost()
    {
        Install("emitted", EmittedBehavior.Throw);

        var load = async () => await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        var result = (await load.Should().NotThrowAsync()).Which.Should().ContainSingle().Which;
        result.State.Should().Be(PluginState.Quarantined);
        result.ErrorCode.Should().Be(CoreErrorCode.PluginLoadFailure);
        result.Message.Should().Contain("threw while registering");
    }

    [Test]
    public async Task TwoEntryModulesInOneAssemblyAreAmbiguousAndRefused()
    {
        Install("emitted", moduleCount: 2);

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.ErrorCode.Should().Be(CoreErrorCode.PluginLoadFailure);
        result.Message.Should().Contain("exactly one");
    }

    [Test]
    public async Task AModuleThatIdentifiesItselfDifferentlyFromItsDeclarationIsRefused()
    {
        var folder = Path.Combine(_root, "emitted");
        EmittedPlugin.Write(folder, "someone.else");
        File.WriteAllText(
            Path.Combine(folder, PluginManifestReader.FileName),
            $$"""
              {
                "schemaVersion": 0,
                "id": "emitted",
                "name": "Emitted",
                "version": "0.1.0",
                "contracts": { "arronix": ">={{PluginLoader.HostContractVersion.Major}}.{{PluginLoader.HostContractVersion.Minor}} <{{PluginLoader.HostContractVersion.Major}}.{{PluginLoader.HostContractVersion.Minor + 1}}" },
                "entryAssembly": "Emitted.Plugin.dll",
                "capabilities": ["storage"]
              }
              """);

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.ErrorCode.Should().Be(CoreErrorCode.PluginLoadFailure);
        result.Message.Should().Contain("must agree");
    }

    [Test]
    public void ACompiledExtensionReferencesTheContractAssemblyAndNothingBlocked()
    {
        var folder = Install("emitted");

        var report = PluginReferenceInspector.Inspect(Path.Combine(folder, "Emitted.Plugin.dll"));

        report.IsAdmissible.Should().BeTrue();
        report.References.Should().Contain("Arronix.Abstractions");
    }

    [Test]
    public async Task AnActiveExtensionIsPublishedAsActive()
    {
        Install("emitted");

        await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        _registry.Active.Should().ContainSingle();
        var view = _registry.Snapshot().Should().ContainSingle().Which;
        view.State.Should().Be(nameof(PluginState.Active));
        view.ErrorCode.Should().BeNull();
        view.Capabilities.Should().Equal(CapabilityNames.Storage);
    }
}
