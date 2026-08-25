using System.IO;
using System.Linq;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Telemetry;
using Arronix.Common.Hosting;
using Arronix.Host.Composition;
using Arronix.Host.Health;
using Arronix.Host.Hosting;
using Arronix.Host.Intent;
using Arronix.Host.Languages;
using Arronix.Host.Media;
using Arronix.Host.Providers;
using Arronix.Host.Runtime;
using Arronix.Host.Scheduling;
using Arronix.Host.Storage;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// The composition root, resolved end to end.
/// </summary>
/// <remarks>
/// The registration order in the composition root is load-bearing in two places, and a container that
/// resolves proves both: the platform's own file system exists before anything that consumes it, and the
/// extension bootstrapper can reach every registry it commits into. A composition that only compiles proves
/// neither.
/// </remarks>
[TestFixture]
internal sealed class HostCompositionTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "arronix-host-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A detector a host brought with it, which composition must not overrule.</summary>
    private sealed class AlwaysAService : IWindowsServiceDetector
    {
        public bool IsWindowsService => true;
    }

    private ServiceProvider Build()
    {
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

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Test]
    public void EverySubsystemTheCompositionRootNamesResolves()
    {
        using var provider = Build();

        provider.GetRequiredService<IFileSystem>().Should().BeOfType<Arronix.Host.FileSystem.HostFileSystem>();
        provider.GetRequiredService<IMediaKindRegistry>().Should().NotBeNull();
        provider.GetRequiredService<IMediaStore>().Should().NotBeNull();
        provider.GetRequiredService<IBackgroundTaskRegistry>().Should().NotBeNull();
        provider.GetRequiredService<JobScheduler>().Should().NotBeNull();
        provider.GetRequiredService<ConcurrencyGovernor>().Should().NotBeNull();
        provider.GetRequiredService<FailureClassifier>().Should().NotBeNull();
        provider.GetRequiredService<ProviderRegistry>().Should().NotBeNull();
        provider.GetRequiredService<ProviderDefinitionStore>().Should().NotBeNull();
        provider.GetRequiredService<ProviderStatusStore>().Should().NotBeNull();
        provider.GetRequiredService<IndexerDispatcher>().Should().NotBeNull();
        provider.GetRequiredService<NotificationDispatcher>().Should().NotBeNull();
        provider.GetRequiredService<IHealthAggregator>().Should().NotBeNull();
        provider.GetRequiredService<IIntentRegistry>().Should().NotBeNull();
        provider.GetRequiredService<WorkbenchBroker>().Should().NotBeNull();
        provider.GetRequiredService<CompletenessCalculator>().Should().NotBeNull();
    }

    [Test]
    public void TheSharedPlatformAssemblyResolvesBecauseTheHostSuppliesTheFileSystemItNeeds()
    {
        using var provider = Build();

        // The shared assembly consumes the file-system contract without shipping an implementation, so this
        // resolution fails outright in a host that registers the shared assembly and nothing else.
        provider.GetRequiredService<Arronix.Common.Hashing.IFileHasher>().Should().NotBeNull();
    }

    [Test]
    public void TheSchedulerIsOneObjectWhetherResolvedDirectlyOrAsAHostedService()
    {
        using var provider = Build();

        var direct = provider.GetRequiredService<JobScheduler>();
        var hosted = provider.GetServices<IHostedService>().OfType<JobScheduler>().Single();

        // Two instances would mean the bootstrapper released startup work on a scheduler that was not the
        // one running.
        hosted.Should().BeSameAs(direct);
    }

    [Test]
    public void TheExtensionBootstrapperIsRegisteredAsAHostedService()
    {
        using var provider = Build();

        provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Should().ContainSingle();
    }

    [Test]
    public void EveryExtensionTransactionParticipantSharesOnePublicationBoundary()
    {
        using var provider = Build();

        var publication = provider.GetRequiredService<PluginPublicationGate>();
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();
        var kinds = provider.GetRequiredService<MediaKindRegistry>();
        var languages = provider.GetRequiredService<LanguageDefinitionRegistry>();
        var providers = provider.GetRequiredService<ProviderRegistry>();
        var jobs = provider.GetRequiredService<BackgroundTaskRegistry>();
        var pluginHealth = provider.GetRequiredService<PluginHealthContributor>();

        provider.GetRequiredService<PluginLoader>().PublicationGate.Should().BeSameAs(publication);
        runtime.PublicationGate.Should().BeSameAs(publication);
        kinds.PublicationGate.Should().BeSameAs(publication);
        languages.PublicationGate.Should().BeSameAs(publication);
        providers.PublicationGate.Should().BeSameAs(publication);
        jobs.PublicationGate.Should().BeSameAs(publication);
        pluginHealth.PublicationGate.Should().BeSameAs(publication);
        provider.GetRequiredService<IMediaKindRegistry>().Should().BeSameAs(kinds);
        provider.GetRequiredService<IBackgroundTaskRegistry>().Should().BeSameAs(jobs);

        provider.GetRequiredService<MediaTypeBinder>().Uses(kinds, providers).Should().BeTrue();
        provider.GetRequiredService<ProviderDefinitionStore>().Uses(providers).Should().BeTrue();
        pluginHealth.UsesRuntime(runtime).Should().BeTrue();

        // The client contract catalog reads the Active set under a gate. Taking that gate from the registry
        // rather than from the container is what makes a catalog guarding a different registry
        // unconstructable, so the composition asserts both halves.
        var contracts = provider.GetRequiredService<ClientContractCatalog>();
        contracts.PublicationGate.Should().BeSameAs(publication);
        contracts.UsesRuntime(runtime).Should().BeTrue();
        provider.GetRequiredService<IClientContractCatalog>().Should().BeSameAs(contracts);
        provider.GetRequiredService<JobScheduler>().UsesRegistry(jobs).Should().BeTrue();
    }

    [Test]
    public void HostCompositionReplacesForeignRegistryAliasesWithItsTransactionParticipants()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IMediaKindRegistry>(_ =>
            throw new InvalidOperationException("A foreign media registry alias must not be resolved."));
        services.AddSingleton<IBackgroundTaskRegistry>(_ =>
            throw new InvalidOperationException("A foreign scheduling registry alias must not be resolved."));
        services.AddArronixHost(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMediaKindRegistry>()
            .Should().BeSameAs(provider.GetRequiredService<MediaKindRegistry>());
        provider.GetRequiredService<IBackgroundTaskRegistry>()
            .Should().BeSameAs(provider.GetRequiredService<BackgroundTaskRegistry>());
    }

    [Test]
    public void TheHostsOwnHealthContributorsAreAllRegistered()
    {
        using var provider = Build();

        provider.GetServices<IHealthContributor>().Select(contributor => contributor.ContributorId)
            .Should().BeEquivalentTo("extensions", "scheduler", "providers", "client-contracts");
    }

    [Test]
    public void TheExtensionHealthContributorIsOneObjectSoTheBootstrapperCanAddToIt()
    {
        using var provider = Build();

        var direct = provider.GetRequiredService<PluginHealthContributor>();
        var enumerated = provider.GetServices<IHealthContributor>().OfType<PluginHealthContributor>().Single();

        enumerated.Should().BeSameAs(direct);
    }

    [Test]
    public async Task AHostWithNoExtensionsInstalledStartsAndServesAnEmptyCatalog()
    {
        using var provider = Build();

        var bootstrapper = provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();

        await bootstrapper.StartAsync(CancellationToken.None);

        provider.GetRequiredService<IMediaKindRegistry>().All.Should().BeEmpty();
        bootstrapper.States.Should().BeEmpty();

        var snapshot = await provider.GetRequiredService<IHealthAggregator>().CollectAsync();

        snapshot.Status.Should().Be(HealthStatus.Healthy);
        snapshot.Checks.Should().Contain(check => check.CheckId == "extensions");
    }

    [Test]
    public async Task StartupWorkIsHeldUntilTheBootstrapperHasFinished()
    {
        using var provider = Build();

        var scheduler = provider.GetRequiredService<JobScheduler>();
        var registry = provider.GetRequiredService<BackgroundTaskRegistry>();
        var job = Support.FakeJob.Succeeding("startup-work");

        registry.RegisterJob(job, "startup");

        (await scheduler.TickAsync()).Should().Be(0);

        await provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single()
            .StartAsync(CancellationToken.None);

        (await scheduler.TickAsync()).Should().Be(1);
    }

    [Test]
    public async Task StoppingWithdrawsEverythingAndLeavesTheHostServing()
    {
        using var provider = Build();

        var bootstrapper = provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();

        await bootstrapper.StartAsync(CancellationToken.None);
        await bootstrapper.StopAsync(CancellationToken.None);

        provider.GetRequiredService<IMediaKindRegistry>().All.Should().BeEmpty();
        (await provider.GetRequiredService<IHealthAggregator>().CollectAsync()).Should().NotBeNull();
    }

    [Test]
    public void TheClockIsSubstitutableWithOneRegistration()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<TimeProvider>(
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UnixEpoch));
        services.AddArronixHost(configuration);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().GetUtcNow().Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Test]
    public void EveryOptionsSectionBindsUnderTheOnePrefix()
    {
        using var provider = Build();

        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Configuration.LibraryOptions>>()
            .Value.RootFolders.Should().Contain(_root);
    }

    [Test]
    public void TheHostAnswersTheWindowsServiceQuestionWithTheFrameworksOwnTest()
    {
        using var provider = Build();

        provider.GetRequiredService<IWindowsServiceDetector>().Should().BeOfType<WindowsServiceDetector>(
            "the shared assembly's fallback only says the question was never answered");
    }

    [Test]
    public void AHostThatBroughtItsOwnServiceDetectorKeepsIt()
    {
        var services = new ServiceCollection();
        var mine = new AlwaysAService();
        services.AddLogging();
        services.AddSingleton<IWindowsServiceDetector>(mine);
        services.AddArronixHost(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IWindowsServiceDetector>().Should().BeSameAs(mine);
    }

    [Test]
    public void EveryServiceAPackageNeedsToBeActivatedIsRegistered()
    {
        using var provider = Build();

        // The five an extension with an entry assembly cannot be activated without. Nothing is missing now,
        // which is what lets an ordinary host run one at all.
        using (new AssertionScope())
        {
            provider.GetService<ICacheProvider>().Should().NotBeNull();
            provider.GetService<IHostRuntimeInfo>().Should().NotBeNull();
            provider.GetService<IOperatingSystemInfo>().Should().NotBeNull();
            provider.GetService<IEventPublisher>().Should().NotBeNull();
            provider.GetService<ITelemetryEmitter>().Should().NotBeNull();

            provider.GetRequiredService<PluginPlatformServices>()
                .MissingRequiredServices()
                .Should()
                .BeEmpty();
        }
    }
}
