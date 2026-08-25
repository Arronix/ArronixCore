using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Arronix.Abstractions.Caching;
using Arronix.Common.Caching;
using Arronix.Host.Tests.Support;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Telemetry;
using Arronix.Host.Composition;
using Arronix.Host.Health;
using Arronix.Host.Languages;
using Arronix.Host.Media;
using Arronix.Host.Providers;
using Arronix.Host.Runtime;
using Arronix.Host.Scheduling;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace Arronix.Host.Tests.Runtime;

/// <summary>Proves startup failure unwinds extensions which were already committed by the loader.</summary>
[TestFixture]
internal sealed class PluginBootstrapperStartupRecoveryTests
{
    private static readonly PluginId FixturePlugin = PluginId.FromString("g02.admission.fixture");
    private string _home = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _home = Directory.CreateTempSubdirectory("arronix-startup-recovery").FullName;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task APostLoadFailureUnwindsCommittedExtensionsWithoutTheStartupToken(bool cancelStartup)
    {
        using var provider = BuildProvider();
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();
        var events = provider.GetRequiredService<StartupPlatformServices>();
        var definitions = provider.GetRequiredService<ProviderDefinitionStore>();
        var bootstrapper = provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();

        await definitions.AddAsync(new ProviderDefinition
        {
            Id = 0,
            Provider = ProviderId.Create(PluginId.FromString("missing.provider"), "proof"),
            Family = ProviderFamily.Indexer,
            Name = "Missing provider",
            Settings = new Dictionary<string, string>(StringComparer.Ordinal),
        });

        using var canceled = new CancellationTokenSource();
        events.BeforeFailure = () =>
        {
            var active = runtime.Active.Should().ContainSingle().Which;
            events.ObservedLoadContext = active.LoadContext;

            if (cancelStartup)
            {
                canceled.Cancel();
            }
        };
        events.ThrowOnPublish = !cancelStartup;

        Exception? failure = null;
        try
        {
            await bootstrapper.StartAsync(canceled.Token);
        }
        catch (Exception caught)
        {
            failure = caught;
        }

        if (cancelStartup)
        {
            failure.Should().BeAssignableTo<OperationCanceledException>();
        }
        else
        {
            failure.Should().BeOfType<InvalidOperationException>()
                .Which.Message.Should().Contain(StartupPlatformServices.FailureMessage);
        }

        var context = events.ObservedLoadContext.Should().NotBeNull().And.Subject;
        runtime.TryGet(FixturePlugin, out var stopped).Should().BeTrue();
        var health = await provider.GetRequiredService<IHealthAggregator>().CollectAsync();

        using var assertions = new AssertionScope();
        stopped!.State.Should().Be(PluginState.Stopped);
        stopped.Ledger.Should().BeNull();
        stopped.LoadContext.Should().BeNull();
        runtime.Active.Should().BeEmpty();
        bootstrapper.States.Should().ContainSingle().Which.State.Should().Be(PluginState.Stopped);

        provider.GetRequiredService<MediaKindRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<TokenRegistry>().Claims.Should().BeEmpty();
        provider.GetRequiredService<ProviderRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<LanguageDefinitionRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<BackgroundTaskRegistry>().Registrations().Should().BeEmpty();
        AssemblyLoadContext.All.Should().NotContain(candidate => ReferenceEquals(candidate, context));
        health.Checks.Should().ContainSingle(check =>
            check.CheckId == "extensions"
            && check.Message!.StartsWith("0 of 1", StringComparison.Ordinal));
        events.TelemetryEvents.Select(static telemetry => telemetry.Message)
            .Should().Contain(message => message.Contains("disposed asynchronously", StringComparison.Ordinal));
    }

    [Test]
    public async Task APreCanceledStartDoesNotBeginPluginAdmission()
    {
        using var provider = BuildProvider();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var bootstrapper = provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();
        var start = () => bootstrapper.StartAsync(canceled.Token);

        await start.Should().ThrowAsync<OperationCanceledException>();

        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();
        runtime.All.Should().BeEmpty();
        runtime.Active.Should().BeEmpty();
        provider.GetRequiredService<MediaKindRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<TokenRegistry>().Claims.Should().BeEmpty();
    }

    private ServiceProvider BuildProvider()
    {
        var pluginRoot = Path.Combine(AppContext.BaseDirectory, "G02PackagedPlugins");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arronix:Host:ExtensionFolder"] = pluginRoot,
                ["Arronix:Plugins:RootFolder"] = pluginRoot,
                ["Arronix:Plugins:StateFolder"] = Path.Combine(_home, "state"),
                ["Arronix:Library:RootFolders:0"] = Path.Combine(_home, "library"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddArronixHost(configuration);
        services.AddSingleton<StartupPlatformServices>();
        services.AddSingleton<ICacheProvider>(provider => provider.GetRequiredService<StartupPlatformServices>());
        services.AddSingleton<ITelemetryEmitter>(provider => provider.GetRequiredService<StartupPlatformServices>());
        services.AddSingleton<IEventPublisher>(provider => provider.GetRequiredService<StartupPlatformServices>());
        services.AddSingleton<IHostRuntimeInfo>(provider => provider.GetRequiredService<StartupPlatformServices>());
        services.AddSingleton<IOperatingSystemInfo>(provider => provider.GetRequiredService<StartupPlatformServices>());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private sealed class StartupPlatformServices :
        ICacheNamespaceProvider,
        ITelemetryEmitter,
        IEventPublisher,
        IHostRuntimeInfo,
        IOperatingSystemInfo
    {
        internal const string FailureMessage = "Post-load reconciliation failure.";
        private readonly object _gate = new();
        private readonly List<TelemetryEvent> _telemetry = [];

        internal Action? BeforeFailure { get; set; }

        internal bool ThrowOnPublish { get; set; }

        internal PluginLoadContext? ObservedLoadContext { get; set; }

        internal IReadOnlyList<TelemetryEvent> TelemetryEvents
        {
            get
            {
                lock (_gate)
                {
                    return [.. _telemetry];
                }
            }
        }

        public DateTimeOffset StartTime { get; } = DateTimeOffset.UnixEpoch;

        public bool IsUserInteractive => false;

        public bool IsAdministrator => false;

        public bool IsWindowsService => false;

        public bool IsContainerized => false;

        public string? ExecutingApplication => null;

        public string Name => "Test";

        public string Version => "1";

        public string FullName => "Test operating system";

        public bool IsDocker => false;

        public bool IsPodman => false;

        public ICacheNamespace CreateNamespace(string name) => new PlatformServiceStub.StubNamespace(name);

        public ICache<TValue> GetCache<TOwner, TValue>(string partition) => throw Unused();

        public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
            => throw Unused();

        public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
            string partition,
            Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
            TimeSpan? lifetime = null) => throw Unused();

        public void Emit(TelemetryEvent telemetryEvent)
        {
            lock (_gate)
            {
                _telemetry.Add(telemetryEvent);
            }
        }

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent
        {
            BeforeFailure?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            return ThrowOnPublish
                ? Task.FromException(new InvalidOperationException(FailureMessage))
                : Task.CompletedTask;
        }

        private static InvalidOperationException Unused()
            => new("The startup-recovery fixture must not exercise undeclared platform privileges.");
    }
}
