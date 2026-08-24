using System.IO;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Configuration;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using Arronix.Plugins.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;


namespace Arronix.Plugins.Tests.Loading;

/// <summary>
/// The pipeline, step by step, against declarations on disk.
/// </summary>
/// <remarks>
/// <para>
/// The property every one of these cases is really asserting is that an extension never partially
/// activates. Whatever step fails, the extension ends quarantined with a failure class an operator can act
/// on, and nothing it declared is left half-applied.
/// </para>
/// <para>
/// The fixture for the isolation case is this test assembly, which references the loader and is therefore
/// exactly the shape of an assembly that must be refused. The fixture for a well-formed entry assembly is
/// the contract assembly, which references nothing blocked but exposes no entry module — so it proves the
/// load context, the load and the module search all ran, and stopped where they should.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PluginLoaderPipelineTests
{
    private string _root = string.Empty;
    private string _state = string.Empty;
    private PluginRuntimeOptions _options = new();
    private PluginPublicationGate _publication = new();
    private LoaderAuthorities _authorities = new(new PluginPublicationGate());
    private PluginRuntimeRegistry _registry = new();
    private TokenRegistry _tokens = new();

    [SetUp]
    public void SetUp()
    {
        var home = Directory.CreateTempSubdirectory("arronix-loader").FullName;
        _root = Path.Combine(home, "plugins");
        _state = Path.Combine(home, "state");
        Directory.CreateDirectory(_root);

        _options = new PluginRuntimeOptions { RootFolder = _root, StateFolder = _state };
        _publication = new PluginPublicationGate();
        _authorities = new LoaderAuthorities(_publication);
        _registry = new PluginRuntimeRegistry(_publication);
        _tokens = new TokenRegistry(_publication);
    }

    [TearDown]
    public void TearDown()
    {
        var home = Path.GetDirectoryName(_root);
        if (home is not null && Directory.Exists(home))
        {
            Directory.Delete(home, recursive: true);
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

    private string Install(string folderName, string manifestJson, string? entryAssemblySource = null)
    {
        var folder = Path.Combine(_root, folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.json"), manifestJson);

        if (entryAssemblySource is not null)
        {
            File.Copy(entryAssemblySource, Path.Combine(folder, Path.GetFileName(entryAssemblySource)), overwrite: true);
        }

        return folder;
    }

    private static string Manifest(
        string id,
        string range = ">=0.8 <0.9",
        string entry = "Arronix.Abstractions.dll",
        string capabilities = "\"parsing\"")
        => $$"""
             {
               "schemaVersion": 0,
               "id": "{{id}}",
               "name": "{{id}}",
               "version": "0.1.0",
               "contracts": { "arronix": "{{range}}" },
               "entryAssembly": "{{entry}}",
               "capabilities": [{{capabilities}}]
             }
             """;

    private static string ContractAssemblyPath => typeof(IPluginModule).Assembly.Location;

    private static string LoaderReferencingAssemblyPath => typeof(PluginLoaderPipelineTests).Assembly.Location;

    [Test]
    public void DiscoveryFindsOneDeclarationPerFolderAndDoesNotRecurse()
    {
        Install("alpha", Manifest("alpha"));
        Install("beta", Manifest("beta"));
        Directory.CreateDirectory(Path.Combine(_root, "gamma", "nested"));
        File.WriteAllText(Path.Combine(_root, "gamma", "nested", "plugin.json"), Manifest("gamma"));

        var candidates = CreateLoader().Discover(out var rejected);

        candidates.Select(candidate => candidate.Manifest.Id).Should().BeEquivalentTo(["alpha", "beta"]);
        rejected.Should().BeEmpty();
    }

    [Test]
    public async Task AnUnreadableDeclarationIsQuarantinedRatherThanFailingDiscovery()
    {
        Install("good", Manifest("good"));
        Install("broken", "{ this is not json");

        var candidates = CreateLoader().Discover(out var rejected);

        candidates.Should().ContainSingle();
        rejected.Should().ContainSingle();
        rejected[0].State.Should().Be(PluginState.Quarantined);
        rejected[0].ErrorCode.Should().Be(CoreErrorCode.PluginManifestInvalid);
    }

    [Test]
    public async Task NothingIsLoadedWhenLoadingIsDisabled()
    {
        Install("alpha", Manifest("alpha"));
        _options.Enabled = false;

        (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().BeEmpty();
    }

    [Test]
    public async Task AnAbsentExtensionFolderIsNotAnError()
    {
        Directory.Delete(_root, recursive: true);

        (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().BeEmpty();
    }

    [Test]
    public async Task AnInvalidDeclarationIsQuarantinedWithEveryDefectListed()
    {
        Install("bad", Manifest("Not An Id", range: "^0.3.0"));

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.State.Should().Be(PluginState.Quarantined);
        result.ErrorCode.Should().Be(CoreErrorCode.PluginManifestInvalid);
        result.Defects.Should().HaveCountGreaterThan(1);
        result.Id.Should().BeNull("the declaration was too broken to yield an identifier");
    }

    [Test]
    public async Task TwoExtensionsClaimingOneIdentifierAreBothQuarantined()
    {
        Install("first", Manifest("duplicate"));
        Install("second", Manifest("duplicate"));

        var results = await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(result => result.ErrorCode == CoreErrorCode.PluginIdConflict);
    }

    [Test]
    public async Task ADisabledExtensionIsStillReportedRatherThanSilentlyIgnored()
    {
        Install("alpha", Manifest("alpha"));
        _options.Disabled.Add("alpha");

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.State.Should().Be(PluginState.Quarantined);
        result.ErrorCode.Should().Be(CoreErrorCode.PluginDisabled);
        result.Manifest.Should().NotBeNull("an operator asking what is installed must still be told");
    }

    [Test]
    public async Task ARangeThisHostDoesNotSatisfyIsQuarantined()
    {
        Install("alpha", Manifest("alpha", range: ">=99.0 <99.1"));

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.ErrorCode.Should().Be(CoreErrorCode.PluginContractMismatch);
        result.Message.Should().Contain(PluginLoader.HostContractVersion.ToString());
    }

    [Test]
    public async Task ACompatibleRangeIsNotRejectedBecauseItSpansLaterMinorVersions()
    {
        var host = PluginLoader.HostContractVersion;
        Install(
            "alpha",
            Manifest("alpha", range: $">={host.Major}.{host.Minor} <{host.Major + 1}.0"),
            ContractAssemblyPath);

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.ErrorCode.Should().Be(CoreErrorCode.PluginLoadFailure);
        result.Message.Should().Contain("no public parameterless entry module");
    }

    [Test]
    public async Task AnAssemblyReferencingAHostAssemblyIsQuarantinedBeforeAnyContextIsCreated()
    {
        Install(
            "alpha",
            Manifest("alpha", entry: Path.GetFileName(LoaderReferencingAssemblyPath)),
            LoaderReferencingAssemblyPath);

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.ErrorCode.Should().Be(CoreErrorCode.PluginIsolationViolation);
        result.Defects.Should().Contain("Arronix.Plugins");
        result.LoadContext.Should().BeNull("nothing was loaded, so nothing has to be unloaded");
    }

    [Test]
    public async Task AMissingEntryAssemblyIsQuarantined()
    {
        Install("alpha", Manifest("alpha"));

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.ErrorCode.Should().Be(CoreErrorCode.PluginLoadFailure);
    }

    [Test]
    public async Task AnAdmissibleAssemblyWithNoEntryModuleIsQuarantinedAfterBeingLoaded()
    {
        Install("alpha", Manifest("alpha"), ContractAssemblyPath);

        var result = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.ErrorCode.Should().Be(CoreErrorCode.PluginLoadFailure);
        result.Message.Should().Contain("no public parameterless entry module");
        result.Manifest.Should().NotBeNull("the declaration was valid; it was the assembly that was not");
    }

    [Test]
    public async Task EveryOutcomeIsRecordedInTheRegistryIncludingTheFailures()
    {
        Install("alpha", Manifest("alpha"));
        Install("broken", "{ not json");

        await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        _registry.All.Should().HaveCount(2);
        _registry.Active.Should().BeEmpty();
        _registry.TryGet(PluginId.FromString("alpha"), out var alpha).Should().BeTrue();
        alpha!.State.Should().Be(PluginState.Quarantined);
    }

    [Test]
    public async Task TheRegistrySnapshotPublishesTheGrantedCapabilitiesRatherThanTheDeclaredOnes()
    {
        Install("alpha", Manifest("alpha", capabilities: "\"indexing\""));
        _options.Disabled.Add("alpha");

        await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        var view = _registry.Snapshot().Should().ContainSingle().Which;
        view.Id.Should().Be("alpha");
        view.Capabilities.Should().BeEquivalentTo([CapabilityNames.Indexing, CapabilityNames.Network]);
        view.State.Should().Be(nameof(PluginState.Quarantined));
        view.ErrorCode.Should().Be((int)CoreErrorCode.PluginDisabled);
    }

    [Test]
    public async Task AnExtensionTooBrokenToNameItselfIsStillRecordedUnderWhereItWasFound()
    {
        var folder = Install("broken", "{ not json");

        await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        _registry.All.Should().ContainSingle()
            .Which.Source.Should().Be(Path.Combine(folder, "plugin.json"));
    }

    [Test]
    public void TheReservedTokensCannotBeRedefinedByAnExtension()
    {
        var claimed = _tokens.TryClaimAll(
            PluginId.FromString("alpha"),
            [
                new TokenClaimRequest(
                    Abstractions.Identity.MediaKindId.FromString("example"),
                    [new NamingToken("{Quality}", string.Empty, string.Empty)])
            ],
            out var defects);

        claimed.Should().BeFalse();
        defects.Should().ContainSingle().Which.Code.Should().Be(CoreErrorCode.PluginTokenConflict);
    }

    [Test]
    public void TheReservedMediaInformationFamilyIsReservedAsAFamily()
        => TokenRegistry.IsReserved("{MediaInfo VideoCodec}").Should().BeTrue();

    [TestCase("{QUALITY}")]
    [TestCase("{Quality.Full}")]
    [TestCase("{media_info-video_codec}")]
    public void AReservedTokenCannotBeEscapedWithAnEquivalentSpelling(string spelling)
        => TokenRegistry.IsReserved(spelling).Should().BeTrue();

    [Test]
    public void TheSameTokenForTwoDifferentMediaKindsIsAllowed()
    {
        var token = new NamingToken("{Title}", string.Empty, string.Empty);

        _tokens.TryClaimAll(
                PluginId.FromString("alpha"),
                [new TokenClaimRequest(Abstractions.Identity.MediaKindId.FromString("one"), [token])],
                out _)
            .Should().BeTrue();
        _tokens.TryClaimAll(
                PluginId.FromString("beta"),
                [new TokenClaimRequest(Abstractions.Identity.MediaKindId.FromString("two"), [token])],
                out var defects)
            .Should().BeTrue("a stable meaning per token within a media context is the stated goal, not across the platform");

        defects.Should().BeEmpty();
        _tokens.Claims.Should().HaveCount(2);
    }

    [Test]
    public void TheSameTokenForTheSameMediaKindIsRefused()
    {
        var token = new NamingToken("{Title}", string.Empty, string.Empty);
        var kind = Abstractions.Identity.MediaKindId.FromString("one");

        _tokens.TryClaimAll(PluginId.FromString("alpha"), [new TokenClaimRequest(kind, [token])], out _)
            .Should().BeTrue();
        _tokens.TryClaimAll(PluginId.FromString("beta"), [new TokenClaimRequest(kind, [token])], out var defects)
            .Should().BeFalse();

        defects.Should().ContainSingle().Which.Message.Should().Contain("alpha");
    }

    [Test]
    public void EquivalentTokenSpellingsForTheSameMediaKindCollide()
    {
        var kind = Abstractions.Identity.MediaKindId.FromString("one");

        _tokens.TryClaimAll(
                PluginId.FromString("alpha"),
                [new TokenClaimRequest(kind, [new NamingToken("{Series Title}", string.Empty, string.Empty)])],
                out _)
            .Should().BeTrue();
        _tokens.TryClaimAll(
                PluginId.FromString("beta"),
                [new TokenClaimRequest(kind, [new NamingToken("{series.title}", string.Empty, string.Empty)])],
                out var defects)
            .Should().BeFalse();

        defects.Should().ContainSingle().Which.Message.Should().Contain("alpha");
    }

    [Test]
    public void AClaimIsAllOrNothing()
    {
        var kind = Abstractions.Identity.MediaKindId.FromString("one");

        _tokens.TryClaimAll(
                PluginId.FromString("alpha"),
                [
                    new TokenClaimRequest(
                        kind,
                        [
                            new NamingToken("{Title}", string.Empty, string.Empty),
                            new NamingToken("{Quality}", string.Empty, string.Empty)
                        ])
                ],
                out _)
            .Should().BeFalse();

        _tokens.Claims.Should().BeEmpty(
            "a partial claim would leave an extension owning some of its tokens while quarantined for the rest");
    }

    [Test]
    public void AQuarantinedExtensionGivesItsTokensBack()
    {
        var alpha = PluginId.FromString("alpha");
        var kind = Abstractions.Identity.MediaKindId.FromString("one");
        _tokens.TryClaimAll(
                alpha,
                [new TokenClaimRequest(kind, [new NamingToken("{Title}", string.Empty, string.Empty)])],
                out _)
            .Should().BeTrue();

        _tokens.Release(alpha);

        _tokens.Claims.Should().BeEmpty();
        _tokens.TryClaimAll(
                PluginId.FromString("beta"),
                [new TokenClaimRequest(kind, [new NamingToken("{Title}", string.Empty, string.Empty)])],
                out _)
            .Should().BeTrue();
    }

    [Test]
    public async Task AHostMissingAPlatformServiceFailsAsAHostProblemRatherThanAnExtensionDefect()
    {
        Install("alpha", Manifest("alpha"), ContractAssemblyPath);

        var loader = new PluginLoader(
            Options.Create(_options),
            new PluginPlatformServices(new StubJsonSerializer(), TimeProvider.System),
            _registry,
            _tokens,
            TimeProvider.System,
            NullLogger<PluginLoader>.Instance,
            _publication,
            _authorities.Graph,
            _authorities.Contracts,
            _authorities.Dependencies);

        var result = (await loader.LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;

        result.ErrorCode.Should().Be(CoreErrorCode.PluginLoadFailure);
        result.Message.Should().Contain("This host cannot activate extensions");
    }
}
