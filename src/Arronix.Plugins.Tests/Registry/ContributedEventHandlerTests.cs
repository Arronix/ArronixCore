using System.IO;
using System.Linq;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Providers;
using Arronix.Plugins.Configuration;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Tests.Support;
using FluentAssertions.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PluginRegistryNamespace = Arronix.Plugins.Registry;
namespace Arronix.Plugins.Tests.Registry;

/// <summary>
/// What the platform's dispatch paths are handed when real, loaded extensions subscribe to an event.
/// </summary>
/// <remarks>
/// The extensions here are compiled assemblies loaded through the real pipeline, so the contributions are
/// leased from genuine runtimes rather than from a fixture standing in for one.
/// </remarks>
[TestFixture]
public sealed class ContributedEventHandlerTests
{
    private string _home = string.Empty;
    private string _root = string.Empty;
    private PluginRuntimeOptions _options = new();
    private PluginRegistryNamespace.PluginPublicationGate _publication = new();
    private LoaderAuthorities _authorities = new(new PluginRegistryNamespace.PluginPublicationGate());
    private PluginRegistryNamespace.PluginRuntimeRegistry _registry = new();
    private PluginRegistryNamespace.TokenRegistry _tokens = new();

    [SetUp]
    public void SetUp()
    {
        _home = Directory.CreateTempSubdirectory("arronix-contributed").FullName;
        _root = Path.Combine(_home, "plugins");
        Directory.CreateDirectory(_root);

        _options = new PluginRuntimeOptions
        {
            RootFolder = _root,
            StateFolder = Path.Combine(_home, "state")
        };
        _publication = new PluginRegistryNamespace.PluginPublicationGate();
        _authorities = new LoaderAuthorities(_publication);
        _registry = new PluginRegistryNamespace.PluginRuntimeRegistry(_publication);
        _tokens = new PluginRegistryNamespace.TokenRegistry(_publication);
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
    public async Task HandlersArriveByExtensionIdentifierAndThenByRegistrationOrderAsync()
    {
        // Installed in the opposite order to the one they must be handed over in.
        Install("b.television");
        Install("a.movies");

        (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().OnlyContain(
            result => result.State == PluginState.Active);

        using var contributed = new PluginRegistryNamespace.PluginContributionSource(_registry)
            .AcquireEventHandlers(typeof(ProviderDefinitionChanged));

        contributed.Contributions.Select(contribution => (contribution.Owner.ToString(), contribution.Ordinal))
            .Should().Equal(
                ("a.movies", 0),
                ("a.movies", 1),
                ("b.television", 0),
                ("b.television", 1));
    }

    [Test]
    public async Task EachContributedHandlerIsTheExtensionsOwnObjectAndCanBeCalledAsync()
    {
        Install("a.movies");

        await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        using var contributed = new PluginRegistryNamespace.PluginContributionSource(_registry)
            .AcquireEventHandlers(typeof(ProviderDefinitionChanged));

        var contribution = contributed.Contributions.Should().HaveCount(2).And.Subject.First();

        contribution.Value.EventType.Should().Be<ProviderDefinitionChanged>();
        contribution.Value.Handler.GetType().Assembly.IsCollectible.Should().BeTrue(
            "it is the extension's own object, loaded in the extension's own context");
        await contribution.Value.Invoke(Changed(), CancellationToken.None);
    }

    [Test]
    public async Task AnEventNobodySubscribedToLeasesNothingAsync()
    {
        Install("a.movies");

        await CreateLoader().LoadAllAsync(NoOpAdmission.Instance);

        using var contributed = new PluginRegistryNamespace.PluginContributionSource(_registry).AcquireEventHandlers(typeof(Unrelated));

        contributed.Contributions.Should().BeEmpty("dispatch is by exact type, and nothing subscribed to this one");
    }

    [Test]
    public async Task HoldingContributionsHoldsTheExtensionOpenAsync()
    {
        Install("a.movies");

        var loaded = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;
        var invocation = (PluginRegistryNamespace.PluginInvocationLifetime)loaded.Ledger!.Invocation!;

        using (new PluginRegistryNamespace.PluginContributionSource(_registry).AcquireEventHandlers(typeof(ProviderDefinitionChanged)))
        {
            invocation.Outstanding.Should().Be(1, "one extension contributed, so one lease is held");
        }

        invocation.Outstanding.Should().Be(0, "and releasing the contributions releases it");
    }

    [Test]
    public async Task APackageOwnsItsEntryAssemblyAndTheContractsItPublishesAndNothingElseAsync()
    {
        Install("a.movies", publishedContract: "Owned.Delivered.Contract");

        var loaded = (await CreateLoader().LoadAllAsync(NoOpAdmission.Instance)).Should().ContainSingle().Which;
        loaded.State.Should().Be(PluginState.Active, loaded.Message ?? "no message");
        var ownership = loaded.PackageLease!.Ownership!;

        // Loaded into the extension's own context after the fact, the way a private assembly it shipped
        // would be. Sharing the context is not owning.
        var shipped = loaded.LoadContext!.LoadFromAssemblyPath(
            EmittedEvent.Write(Path.Combine(_home, "shipped"), "Private.Delivered.Assembly"));

        using (new AssertionScope())
        {
            ownership.Assemblies.Select(assembly => assembly.GetName().Name)
                .Should().BeEquivalentTo(["Emitted.Plugin", "Owned.Delivered.Contract"]);
            ownership.Owns(shipped).Should().BeFalse("it is in the package's context but is not what the package owns");
            ownership.Owns(typeof(ContributedEventHandlerTests).Assembly).Should().BeFalse();
        }
    }

    private static ProviderDefinitionChanged Changed()
        => new(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, null, ProviderFamily.Indexer, 1, ProviderChangeKind.Added);

    private PluginLoader CreateLoader() => new(
        Options.Create(_options),
        new PluginPlatformServices(
            new StubJsonSerializer(),
            TimeProvider.System,
            cache: new RecordingCacheProvider(),
            telemetry: new RecordingTelemetryEmitter(),
            events: new RecordingEventPublisher(),
            runtime: new StubHostRuntimeInfo(),
            operatingSystem: new StubOperatingSystemInfo()),
        _registry,
        _tokens,
        TimeProvider.System,
        NullLogger<PluginLoader>.Instance,
        _publication,
        _authorities.Graph,
        _authorities.Contracts,
        _authorities.Dependencies);

    private void Install(string pluginId, string? publishedContract = null)
    {
        var folder = Path.Combine(_root, pluginId);
        var assembly = EmittedPlugin.Write(folder, pluginId, EmittedBehavior.SubscribeTwiceToAPlatformEvent);
        var contract = publishedContract is null
            ? null
            : Path.GetFileName(EmittedContract.Write(folder, publishedContract, new Version(1, 0, 0, 0)));

        File.WriteAllText(
            Path.Combine(folder, PluginManifestReader.FileName),
            $$"""
              {
                "schemaVersion": 1,
                "id": "{{pluginId}}",
                "name": "Emitted",
                "version": "0.1.0",
                "contracts": { "arronix": ">={{PluginLoader.HostContractVersion.Major}}.{{PluginLoader.HostContractVersion.Minor}} <{{PluginLoader.HostContractVersion.Major}}.{{PluginLoader.HostContractVersion.Minor + 1}}" },
                "entryAssembly": "{{Path.GetFileName(assembly)}}",
                "contractAssemblies": [{{(contract is null ? string.Empty : $"\"{contract}\"")}}],
                "capabilities": ["storage"]
              }
              """);
    }

    /// <summary>An event the fixture publishes and nothing subscribes to.</summary>
    private sealed record Unrelated : IDomainEvent
    {
        public Guid EventId { get; } = Guid.CreateVersion7();

        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UnixEpoch;

        public string? CorrelationId => null;
    }
}
