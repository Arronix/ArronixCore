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
using FluentAssertions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;


namespace Arronix.Plugins.Tests.Loading;

/// <summary>
/// The loader side of the Host admission transaction, driven by Host-prepared inventory rather than by a
/// duplicate declaration.
/// </summary>
/// <remarks>
/// <para>
/// The extension is a real emitted assembly loaded through a real load context; the host is a stub, because
/// what is under test is the loader's side of the seam. A stub host lets an inventory be stated exactly —
/// two kinds with different vocabularies, or a kind an empty manifest never mentioned — which a real Host
/// could only prepare by shipping fixture media kinds nobody would install.
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
    private PluginPublicationGate _publication = new();
    private LoaderAuthorities _authorities = new(new PluginPublicationGate());
    private PluginRuntimeRegistry _registry = new();
    private TokenRegistry _tokens = new();

    [SetUp]
    public void SetUp()
    {
        _home = Directory.CreateTempSubdirectory("arronix-admitted").FullName;
        _root = Path.Combine(_home, "plugins");
        Directory.CreateDirectory(_root);

        _options = new PluginRuntimeOptions { RootFolder = _root, StateFolder = Path.Combine(_home, "state") };
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

    [Test]
    public async Task TwoAdmittedKindsEachOwnTheirOwnTokensAndNotEachOthers()
    {
        Install("emitted");

        var admission = StubAdmission.Admitting(
            new AdmittedMediaKind(Films, [Token("{Film Title}"), Token("{Film Year}")]),
            new AdmittedMediaKind(Serials, [Token("{Serial Title}")]));

        var result = (await CreateLoader().LoadAllAsync(admission)).Should().ContainSingle().Which;

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
    public async Task ADeclarationThatRestatesNoDerivableFactStillOwnsWhatWasAdmitted()
    {
        Install("emitted");

        var admission = StubAdmission.Admitting(new AdmittedMediaKind(Films, [Token("{Film Title}")]));

        var result = (await CreateLoader().LoadAllAsync(admission)).Should().ContainSingle().Which;

        result.State.Should().Be(
            PluginState.Active,
            "media kinds and tokens are derived from the extension's own types, so a manifest that omits "
            + "them has stated everything it owns");
        result.Admitted.Kinds.Should().Equal(Films);
        _tokens.Claims.Should().ContainSingle().Which.Token.Name.Should().Be("{Film Title}");
    }

    [Test]
    public async Task AnEmptyHostInventoryRemainsAuthoritative()
    {
        Install("emitted");

        var admission = StubAdmission.Admitting();
        var result = (await CreateLoader().LoadAllAsync(admission)).Should().ContainSingle().Which;

        result.State.Should().Be(PluginState.Active);
        result.Admitted.IsAuthoritative.Should().BeTrue();
        result.Admitted.MediaKinds.Should().BeEmpty();
        admission.Events.Should().Equal("prepare:emitted", "commit:emitted");
    }

    [Test]
    public async Task AMalformedSuccessfulAdmissionStillRollsBackItsAttempt()
    {
        Install("emitted");

        var admission = new MalformedAdmission();
        var result = (await CreateLoader().LoadAllAsync(admission)).Should().ContainSingle().Which;

        using var assertions = new AssertionScope();
        result.State.Should().Be(PluginState.Quarantined);
        result.ErrorCode.Should().Be(CoreErrorCode.PluginLoadFailure);
        result.Defects.Should().Contain(defect => defect.Contains("authoritative", StringComparison.Ordinal));
        admission.Events.Should().Equal("prepare:emitted", "rollback:emitted");
        _tokens.Claims.Should().BeEmpty();
    }

    [Test]
    public void LoaderEntryPointsRequireAnExplicitHostAdmissionAuthority()
    {
        var entryPoints = typeof(PluginLoader)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(method => method.Name is "LoadAsync" or "LoadAllAsync")
            .ToArray();

        entryPoints.Should().HaveCount(2);
        entryPoints.Should().OnlyContain(method => method.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(IPluginAdmissionCheck)
            && !parameter.IsOptional
            && !parameter.HasDefaultValue));
    }

    [Test]
    public void TheAdmittedInventorySnapshotsItsKindsAndTokens()
    {
        var tokens = new List<NamingToken> { Token("{Film Title}") };
        var kind = new AdmittedMediaKind(Films, tokens);
        var kinds = new List<AdmittedMediaKind> { kind };
        var inventory = new AdmittedInventory(kinds);

        tokens.Clear();
        kinds.Clear();

        using var assertions = new AssertionScope();
        inventory.MediaKinds.Should().ContainSingle();
        inventory.Kinds.Should().Equal(Films);
        inventory.TryGet(Films, out var admitted).Should().BeTrue();
        admitted!.Tokens.Should().ContainSingle().Which.Name.Should().Be("{Film Title}");
    }

    [Test]
    public async Task ADeclaredTokenTheAdmittedKindDoesNotDefineIsRefused()
    {
        Install("emitted", tokens: """{ "name": "{Film Runtime}", "description": "invented" }""");

        var admission = StubAdmission.Admitting(new AdmittedMediaKind(Films, [Token("{Film Title}")]));

        var result = (await CreateLoader().LoadAllAsync(admission)).Should().ContainSingle().Which;

        result.State.Should().Be(PluginState.Quarantined);
        result.ErrorCode.Should().Be(CoreErrorCode.PluginPolicyDeclarationInvalid);
        result.Defects.Should().Contain(defect => defect.Contains("{Film Runtime}", StringComparison.Ordinal));
        result.Defects.Should().Contain(defect => defect.Contains("{Film Title}", StringComparison.Ordinal));
        _tokens.Claims.Should().BeEmpty();
        admission.Events.Should().Equal("prepare:emitted", "rollback:emitted");
    }

    [Test]
    public async Task ManifestAgreementUsesTheNamingGrammarsTokenEquality()
    {
        Install("emitted", tokens: """{ "name": "{film.title}", "description": "same token" }""");

        var admission = StubAdmission.Admitting(new AdmittedMediaKind(Films, [Token("{Film Title}")]));

        var result = (await CreateLoader().LoadAllAsync(admission)).Should().ContainSingle().Which;

        result.State.Should().Be(PluginState.Active);
        _tokens.Claims.Should().ContainSingle().Which.Token.Name.Should().Be("{Film Title}");
    }

    /// <remarks>
    /// Two extensions are prepared for one kind. The second can prepare a non-conflicting token plan, but
    /// the identity check refuses it before publication because the first extension is already active. The
    /// original token claim remains the only committed claim.
    /// </remarks>
    [Test]
    public async Task ASecondCandidateForAnActiveKindIsRefusedWithoutPublishingItsTokenPlan()
    {
        Install("first");
        Install("second", assemblyName: "Emitted.Second");

        var admission = StubAdmission
            .For("first", new AdmittedMediaKind(Films, [Token("{Film Title}")]))
            .And("second", new AdmittedMediaKind(Films, [Token("{Second Title}")]));

        var results = await CreateLoader().LoadAllAsync(admission);

        results.Should().HaveCount(2);
        results.Single(result => result.Id == PluginId.FromString("first")).State.Should().Be(PluginState.Active);

        var refused = results.Single(result => result.Id == PluginId.FromString("second"));
        refused.State.Should().Be(PluginState.Quarantined);
        refused.ErrorCode.Should().Be(CoreErrorCode.MediaKindConflict);
        refused.Defects.Should().Contain(defect => defect.Contains("films", StringComparison.Ordinal));

        _tokens.Claims.Should().ContainSingle(
            "the refused extension never commits its prepared token plan")
            .Which.Token.Name.Should().Be("{Film Title}");
        admission.Events.Should().Equal(
            "prepare:first",
            "commit:first",
            "prepare:second",
            "rollback:second");
    }

    /// <summary>
    /// A candidate which fails the late declaration agreement check rolls back its prepared Host receipt
    /// before the loader advances to the next candidate.
    /// </summary>
    [Test]
    public async Task ALateAgreementFailureRollsBackBeforeTheNextCandidateIsPrepared()
    {
        Install(
            "first",
            tokens: """{ "name": "{Invented}", "description": "not in the admitted projection" }""");
        Install("second", assemblyName: "Emitted.Second");

        var admission = new TrackingAdmission(
            new AdmittedMediaKind(Films, [Token("{Film Title}")]));

        var results = await CreateLoader().LoadAllAsync(admission);

        using var assertions = new AssertionScope();
        results.Single(result => result.Id == PluginId.FromString("first")).State
            .Should().Be(PluginState.Quarantined);
        results.Single(result => result.Id == PluginId.FromString("second")).State
            .Should().Be(PluginState.Active);
        admission.Events.Should().Equal(
            "prepare:first",
            "rollback:first",
            "prepare:second",
            "commit:second");
        admission.Owner.Should().Be(PluginId.FromString("second"));
        _tokens.Claims.Should().ContainSingle().Which.Plugin.Should().Be(PluginId.FromString("second"));
    }

    [Test]
    public async Task AFailedReloadCannotReplaceTheOriginalActiveRuntimeAuthority()
    {
        Install("emitted");

        var plugin = PluginId.FromString("emitted");
        var admission = new TrackingAdmission(
            new AdmittedMediaKind(Films, [Token("{Film Title}")]));
        var loader = CreateLoader();

        var original = (await loader.LoadAllAsync(admission)).Should().ContainSingle().Which;
        var originalContext = original.LoadContext.Should().NotBeNull().And.Subject;
        var reload = (await loader.LoadAllAsync(admission)).Should().ContainSingle().Which;

        using var assertions = new AssertionScope();
        original.State.Should().Be(PluginState.Active);
        reload.State.Should().Be(PluginState.Quarantined,
            "the already-owned token makes this reload a genuine failed candidate, not a no-op");
        _registry.TryGet(plugin, out var recorded).Should().BeTrue();
        recorded.Should().BeSameAs(original,
            "the original result is the runtime-lifetime authority a failed candidate must not replace");
        _registry.Active.Should().ContainSingle().Which.Should().BeSameAs(original);
        recorded!.LoadContext.Should().BeSameAs(originalContext,
            "retaining the original active result retains its inaccessible attempt-scoped lifetime receipt");
        admission.Owner.Should().Be(plugin,
            "rolling back the failed attempt must not withdraw the original committed attempt");
        admission.Events.Should().Equal(
            "prepare:emitted",
            "commit:emitted",
            "prepare:emitted",
            "rollback:emitted");
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
        NullLogger<PluginLoader>.Instance,
        _publication,
        _authorities.Graph,
        _authorities.Contracts,
        _authorities.Dependencies);

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
                "schemaVersion": 1,
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

        public List<string> Events { get; } = [];

        public PluginAdmissionResult Prepare(ValidatedManifest manifest, PluginRegistrationLedger ledger)
        {
            Events.Add($"prepare:{manifest.Id}");
            var inventory = _byPlugin.TryGetValue(manifest.Id.Value, out var selected) ? selected : _fallback;
            return PluginAdmissionResult.Prepared(new StubAttempt(manifest.Id, inventory, Events));
        }

        private sealed class StubAttempt(
            PluginId plugin,
            AdmittedInventory inventory,
            List<string> events) : IPluginAdmissionAttempt
        {
            public PluginId Plugin { get; } = plugin;

            public AdmittedInventory Inventory { get; } = inventory;

            public bool TryCommit(out CoreErrorCode errorCode, out IReadOnlyList<string> defects)
            {
                events.Add($"commit:{Plugin}");
                errorCode = CoreErrorCode.Unknown;
                defects = [];
                return true;
            }

            public void Rollback() => events.Add($"rollback:{Plugin}");
        }
    }

    /// <summary>A broken Host double proving that even malformed success retains rollback ownership.</summary>
    private sealed class MalformedAdmission : IPluginAdmissionCheck
    {
        public List<string> Events { get; } = [];

        public PluginAdmissionResult Prepare(ValidatedManifest manifest, PluginRegistrationLedger ledger)
        {
            Events.Add($"prepare:{manifest.Id}");
            return PluginAdmissionResult.Prepared(new MalformedAttempt(manifest.Id, Events));
        }

        private sealed class MalformedAttempt(
            PluginId plugin,
            List<string> events) : IPluginAdmissionAttempt
        {
            public PluginId Plugin { get; } = plugin;

            public AdmittedInventory Inventory { get; } = AdmittedInventory.NotAdmitted;

            public bool TryCommit(out CoreErrorCode errorCode, out IReadOnlyList<string> defects)
            {
                errorCode = CoreErrorCode.Unknown;
                defects = [];
                return true;
            }

            public void Rollback() => events.Add($"rollback:{Plugin}");
        }
    }

    /// <summary>A transactional Host double which tracks attempt-specific commit and rollback ownership.</summary>
    private sealed class TrackingAdmission : IPluginAdmissionCheck
    {
        private readonly AdmittedInventory _inventory;

        public TrackingAdmission(params AdmittedMediaKind[] kinds)
            => _inventory = new AdmittedInventory(kinds);

        public List<string> Events { get; } = [];

        public PluginId? Owner { get; private set; }

        public PluginAdmissionResult Prepare(ValidatedManifest manifest, PluginRegistrationLedger ledger)
        {
            Events.Add($"prepare:{manifest.Id}");
            return PluginAdmissionResult.Prepared(new TrackingAttempt(this, manifest.Id, _inventory));
        }

        private sealed class TrackingAttempt(
            TrackingAdmission admission,
            PluginId plugin,
            AdmittedInventory inventory) : IPluginAdmissionAttempt
        {
            private bool _committed;

            public PluginId Plugin { get; } = plugin;

            public AdmittedInventory Inventory { get; } = inventory;

            public bool TryCommit(out CoreErrorCode errorCode, out IReadOnlyList<string> defects)
            {
                admission.Events.Add($"commit:{Plugin}");

                if (admission.Owner is { } owner)
                {
                    errorCode = CoreErrorCode.MediaKindConflict;
                    defects = [$"The admitted kind is still owned by '{owner}'."];
                    return false;
                }

                admission.Owner = Plugin;
                _committed = true;
                errorCode = CoreErrorCode.Unknown;
                defects = [];
                return true;
            }

            public void Rollback()
            {
                admission.Events.Add($"rollback:{Plugin}");

                if (_committed && admission.Owner == Plugin)
                {
                    admission.Owner = null;
                }
            }
        }
    }
}
