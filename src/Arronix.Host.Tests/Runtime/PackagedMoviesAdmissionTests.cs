using System.IO;
using System.Linq;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Host.Composition;
using Arronix.Host.Media;
using Arronix.Host.Runtime;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arronix.Host.Tests.Runtime;

/// <summary>Runs the repository-built Movies package through discovery, admission, and publication.</summary>
[TestFixture]
internal sealed class PackagedMoviesAdmissionTests
{
    private string _stateRoot = string.Empty;
    private ServiceProvider? _provider;

    [SetUp]
    public void SetUp()
    {
        _stateRoot = Directory.CreateTempSubdirectory("arronix-packaged-movies").FullName;
        var pluginRoot = Path.Combine(AppContext.BaseDirectory, "PackagedPlugins");

        File.Exists(Path.Combine(pluginRoot, "movies", "plugin.json")).Should().BeTrue(
            "the build must stage the real package before the test can prove its loader behavior");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arronix:Host:ExtensionFolder"] = pluginRoot,
                ["Arronix:Plugins:RootFolder"] = pluginRoot,
                ["Arronix:Plugins:StateFolder"] = Path.Combine(_stateRoot, "state"),
                ["Arronix:Library:RootFolders:0"] = Path.Combine(_stateRoot, "library"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddArronixHost(configuration);
        services.AddSingleton<ICacheProvider, RequiredServiceStub>();
        services.AddSingleton<ITelemetryEmitter, RequiredServiceStub>();
        services.AddSingleton<IEventPublisher, RequiredServiceStub>();
        services.AddSingleton<IHostRuntimeInfo, RequiredServiceStub>();
        services.AddSingleton<IOperatingSystemInfo, RequiredServiceStub>();
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

        if (Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    [Test]
    public async Task RealPackagedMoviesReachesTheKnownTokenGateAndIsWithdrawnAtomically()
    {
        var provider = _provider!;
        var bootstrapper = provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();

        await bootstrapper.StartAsync(CancellationToken.None);

        var state = bootstrapper.States.Should().ContainSingle().Which;
        var kinds = provider.GetRequiredService<MediaKindRegistry>();
        var tokens = provider.GetRequiredService<TokenRegistry>();

        using var assertions = new AssertionScope();
        state.Id.Should().Be(PluginId.FromString("movies"));
        state.State.Should().Be(PluginState.Quarantined);
        state.ErrorCode.Should().Be(CoreErrorCode.PluginPolicyDeclarationInvalid);
        state.Message.Should().Contain("tokens");
        state.Defects.Should().NotBeEmpty();
        kinds.TryGet(MediaKindId.FromString("movies"), out _).Should().BeFalse(
            "a late loader failure must withdraw the typed kind admitted earlier in the pipeline");
        tokens.Claims.Should().BeEmpty(
            "a quarantined package must not retain naming-token ownership");
    }

    /// <summary>
    /// Satisfies loader preconditions which this package never exercises. The package still receives the
    /// loader's capability-filtered wrappers, not these instances directly.
    /// </summary>
    private sealed class RequiredServiceStub :
        ICacheProvider,
        ITelemetryEmitter,
        IEventPublisher,
        IHostRuntimeInfo,
        IOperatingSystemInfo
    {
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

        public ICache<TValue> GetCache<TOwner, TValue>(string partition) => throw Unused();

        public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
            => throw Unused();

        public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
            string partition,
            Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
            TimeSpan? lifetime = null) => throw Unused();

        public void Emit(TelemetryEvent telemetryEvent)
        {
        }

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;

        private static InvalidOperationException Unused() =>
            new("The packaged Movies extension must not exercise undeclared platform privileges.");
    }
}
