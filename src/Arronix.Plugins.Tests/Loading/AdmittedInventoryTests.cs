using System.IO;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
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
/// The post-admission half of the pipeline, driven by what a host admitted rather than by a declaration.
/// </summary>
/// <remarks>
/// <para>
/// The extension is a real emitted assembly loaded through a real load context; the host is a stub, because
/// what is under test is the loader's side of the seam. A stub host lets an inventory be stated exactly —
/// two kinds with different vocabularies, or a kind an empty manifest never mentioned — which a real host
/// could only produce by shipping fixture media kinds nobody would install.
/// </para>
/// <para>
/// Every case here would have passed vacuously before the inventory existed, because the loader could not
/// see a typed kind at all. That is the defect these tests exist to keep closed.
/// </para>
/// </remarks>
[TestFixture]
public sealed class AdmittedInventoryTests
{
    private static readonly MediaKindId Films = MediaKindId.FromString("films");
    private static readonly MediaKindId Serials = MediaKindId.FromString("serials");

    private string _home = string.Empty;
    private string _root = string.Empty;
    private PluginRuntimeOptions _options = new();
    private PluginRuntimeRegistry _registry = new();
    private TokenRegistry _tokens = new();

    [SetUp]
    public void SetUp()
    {
        _home = Directory.CreateTempSubdirectory("arronix-admitted").FullName;
        _root = Path.Combine(_home, "plugins");
        Directory.CreateDirectory(_root);

        _options = new PluginRuntimeOptions { RootFolder = _root, StateFolder = Path.Combine(_home, "state") };
        _registry = new PluginRuntimeRegistry();
        _tokens = new TokenRegistry();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Test]
    public void TwoAdmittedKindsEachOwnTheirOwnTokensAndNotEachOthers()
    {
        Install("emitted");

        var admission = StubAdmission.Admitting(
            new AdmittedMediaKind(Films, [Token("{Film Title}"), Token("{Film Year}")]),
            new AdmittedMediaKind(Serials, [Token("{Serial Title}")]));

        var result = CreateLoader().LoadAll(admission).Should().ContainSingle().Which;

        result.State.Should().Be(PluginState.Active);
        _tokens.Claims.Should().HaveCount(
            3,
            "three derived tokens produce three claims; the cross product of two kinds and three tokens would produce six");

        _tokens.Claims
            .Where(claim => claim.MediaKind == Films)
            .Select(claim => claim.Token.Name)
            .Should().BeEquivalentTo(["{Film Title}", "{Film Year}"]);

        _tokens.Claims
            .Where(claim => claim.MediaKind == Serials)
            .Select(claim => claim.Token.Name)
            .Should().Equal("{Serial Title}");
    }

    [Test]
    public void ADeclarationThatRestatesNoDerivableFactStillOwnsWhatWasAdmitted()
    {
        Install("emitted");

        var admission = StubAdmission.Admitting(new AdmittedMediaKind(Films, [Token("{Film Title}")]));

        var result = CreateLoader().LoadAll(admission).Should().ContainSingle().Which;

        result.State.Should().Be(
            PluginState.Active,
            "media kinds and tokens are derived from the extension's own types, so a manifest that omits "
            + "them has stated everything it owns");
        result.Admitted.Kinds.Should().Equal(Films);
        _tokens.Claims.Should().ContainSingle().Which.Token.Name.Should().Be("{Film Title}");
    }

    [Test]
    public void ADeclaredTokenTheAdmittedKindDoesNotDefineIsRefused()
    {
        Install("emitted", tokens: """{ "name": "{Film Runtime}", "description": "invented" }""");

        var admission = StubAdmission.Admitting(new AdmittedMediaKind(Films, [Token("{Film Title}")]));

        var result = CreateLoader().LoadAll(admission).Should().ContainSingle().Which;

        result.State.Should().Be(PluginState.Quarantined);
        result.ErrorCode.Should().Be(CoreErrorCode.PluginPolicyDeclarationInvalid);
        result.Defects.Should().Contain(defect => defect.Contains("{Film Runtime}", StringComparison.Ordinal));
        result.Defects.Should().Contain(defect => defect.Contains("{Film Title}", StringComparison.Ordinal));
        _tokens.Claims.Should().BeEmpty();
    }

    /// <remarks>
    /// Two extensions admitted for one kind. The second passes token ownership, because its vocabulary does
    /// not collide, and is refused by the identity step — which reads what each extension was admitted for
    /// rather than what its manifest claimed. Everything it had already taken is given back.
    /// </remarks>
    [Test]
    public void AKindAnotherActiveExtensionWasAdmittedForIsRefusedAndItsTokensAreGivenBack()
    {
        Install("first");
        Install("second", assemblyName: "Emitted.Second");

        var admission = StubAdmission
            .For("first", new AdmittedMediaKind(Films, [Token("{Film Title}")]))
            .And("second", new AdmittedMediaKind(Films, [Token("{Second Title}")]));

        var results = CreateLoader().LoadAll(admission);

        results.Should().HaveCount(2);
        results.Single(result => result.Id == PluginId.FromString("first")).State.Should().Be(PluginState.Active);

        var refused = results.Single(result => result.Id == PluginId.FromString("second"));
        refused.State.Should().Be(PluginState.Quarantined);
        refused.ErrorCode.Should().Be(CoreErrorCode.MediaKindConflict);
        refused.Defects.Should().Contain(defect => defect.Contains("films", StringComparison.Ordinal));

        _tokens.Claims.Should().ContainSingle(
            "the refused extension gives back the token it had already claimed before the identity step ran")
            .Which.Token.Name.Should().Be("{Film Title}");
    }

    private static NamingToken Token(string name) => new(name, "derived", string.Empty);

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
        NullLogger<PluginLoader>.Instance);

    private void Install(
        string pluginId,
        string capabilities = "\"storage\"",
        string mediaKinds = "",
        string tokens = "",
        string assemblyName = "Emitted.Plugin")
    {
        var folder = Path.Combine(_root, pluginId);
        var assembly = EmittedPlugin.Write(folder, pluginId, assemblyName: assemblyName);
        var version = PluginLoader.HostContractVersion;

        File.WriteAllText(
            Path.Combine(folder, PluginManifestReader.FileName),
            $$"""
              {
                "schemaVersion": 0,
                "id": "{{pluginId}}",
                "name": "Emitted",
                "version": "0.1.0",
                "contracts": { "arronix": ">={{version.Major}}.{{version.Minor}} <{{version.Major}}.{{version.Minor + 1}}" },
                "entryAssembly": "{{Path.GetFileName(assembly)}}",
                "capabilities": [{{capabilities}}],
                "mediaKinds": [{{mediaKinds}}],
                "tokens": [{{tokens}}]
              }
              """);
    }

    /// <summary>A host that admits whatever it is told to, so the loader's own steps can be isolated.</summary>
    private sealed class StubAdmission : IPluginAdmissionCheck
    {
        private readonly Dictionary<string, AdmittedInventory> _byPlugin = [];
        private readonly AdmittedInventory _fallback;

        private StubAdmission(AdmittedInventory fallback) => _fallback = fallback;

        public static StubAdmission Admitting(params AdmittedMediaKind[] kinds)
            => new(new AdmittedInventory(kinds));

        public static StubAdmission For(string plugin, params AdmittedMediaKind[] kinds)
            => new StubAdmission(AdmittedInventory.Empty).And(plugin, kinds);

        public StubAdmission And(string plugin, params AdmittedMediaKind[] kinds)
        {
            _byPlugin[plugin] = new AdmittedInventory(kinds);
            return this;
        }

        public PluginAdmissionResult Admit(ValidatedManifest manifest, PluginRegistrationLedger ledger)
            => PluginAdmissionResult.Admitted(
                _byPlugin.TryGetValue(manifest.Id.Value, out var inventory) ? inventory : _fallback);
    }
}
