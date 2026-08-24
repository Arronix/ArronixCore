using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Identity;
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
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arronix.Host.Tests.Runtime;

/// <summary>Proves G02 against a packaged extension with contributions in every withdrawn registry.</summary>
/// <remarks>
/// The fixture project is a build dependency but not an assembly reference. Every type asserted here was
/// discovered from disk and loaded into the extension's own context; the test cannot construct one of its
/// contributions in process.
/// </remarks>
[TestFixture]
internal sealed class PackagedAdmissionFixtureTests
{
    private static readonly PluginId FixturePlugin = PluginId.FromString("g02.admission.fixture");
    private static readonly PluginId MoviesPlugin = PluginId.FromString("movies");
    private static readonly MediaKindId FixtureKind = MediaKindId.FromString("g02-fixture");
    private const string FixtureJobId = "g02.admission.fixture.proof";
    private const string ModuleDisposedMessage = "G02 admission fixture module disposed asynchronously.";
    private const string NotifierDisposedMessage = "G02 admission fixture notifier disposed asynchronously.";
    private const string LanguageDisposedMessage = "G02 admission fixture language disposed asynchronously.";
    private const string JobDisposedMessage = "G02 admission fixture scheduled job disposed asynchronously.";
    private const string HealthDisposedMessage = "G02 admission fixture health contributor disposed asynchronously.";
    private const string JobCancellationObservedMessage =
        "G02 admission fixture scheduled job observed cancellation.";
    private const string ThrowingUnloadFailure = "G02 fixture unloading failure.";
    private const string ThrowingJobIdFailure = "G02 fixture JobId getter failure.";
    private const string ForbiddenRootResolutionMessage =
        "G02 forbidden provider resolved a root service.";
    private const string ForbiddenConstructorInvocation =
        "G02 forbidden IServiceProvider constructor was invoked.";
    private static readonly string[] CleanupOrder =
    [
        NotifierDisposedMessage,
        LanguageDisposedMessage,
        HealthDisposedMessage,
        JobDisposedMessage,
        ModuleDisposedMessage,
    ];
    private static readonly string[] PreAdmissionFailureCleanupOrder =
    [
        HealthDisposedMessage,
        JobDisposedMessage,
        ModuleDisposedMessage,
    ];
    private static readonly string[] PostLanguagePreparationFailureCleanupOrder =
    [
        LanguageDisposedMessage,
        HealthDisposedMessage,
        JobDisposedMessage,
        ModuleDisposedMessage,
    ];

    private string _stateRoot = string.Empty;
    private string _packagedRoot = string.Empty;
    private ServiceProvider? _provider;

    [SetUp]
    public void SetUp()
    {
        _stateRoot = Directory.CreateTempSubdirectory("arronix-g02-admission").FullName;
        _packagedRoot = Path.Combine(AppContext.BaseDirectory, "G02PackagedPlugins");

        File.Exists(Path.Combine(_packagedRoot, FixturePlugin.Value, "plugin.json")).Should().BeTrue(
            "the build must stage the purpose-built package before its lifecycle can be proved");

        _provider = BuildProvider(_packagedRoot);
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
    public void HostedServiceOrderDrainsTheSchedulerBeforeExtensionTeardown()
    {
        var hosted = _provider!.GetServices<IHostedService>().ToList();
        var bootstrapper = hosted.FindIndex(static service => service is PluginBootstrapper);
        var scheduler = hosted.FindIndex(static service => service is JobScheduler);

        using var assertions = new AssertionScope();
        bootstrapper.Should().BeGreaterThanOrEqualTo(0);
        scheduler.Should().BeGreaterThan(bootstrapper,
            "the generic host stops hosted services in reverse registration order");
    }

    [Test]
    public async Task AdmissionPublishesEveryRealContributionFromTheAuthoritativeInventory()
    {
        var manifestPath = Path.Combine(_packagedRoot, FixturePlugin.Value, "plugin.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();

        manifest.ContainsKey("mediaKinds").Should().BeFalse(
            "the scheduled job must get its kind from Host admission, not a duplicate manifest field");
        manifest.ContainsKey("tokens").Should().BeFalse(
            "token ownership must likewise come from the admitted kind");

        var provider = _provider!;
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var loaded = provider.GetRequiredService<PluginRuntimeRegistry>().Active.Should().ContainSingle().Which;
        var kind = provider.GetRequiredService<MediaKindRegistry>().Require(FixtureKind);
        var runtime = kind.MediaType.Should().NotBeNull().And.Subject.As<IMediaTypeRuntime>();
        var contributedProvider = provider.GetRequiredService<ProviderRegistry>().All.Should().ContainSingle().Which;
        var language = provider.GetRequiredService<LanguageDefinitionRegistry>().All.Should().ContainSingle().Which;
        var jobs = provider.GetRequiredService<BackgroundTaskRegistry>();
        var job = jobs.Registrations().Should().ContainSingle().Which;
        var claims = provider.GetRequiredService<TokenRegistry>().Claims;
        (await jobs.TriggerJobAsync(FixtureJobId)).Should().BeTrue();
        var queued = provider.GetRequiredService<JobQueue>().Snapshot().Should().ContainSingle().Which;
        var health = await provider.GetRequiredService<IHealthAggregator>().CollectAsync();
        var activeState = bootstrapper.States.Should().ContainSingle().Which;

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        activeState.State.Should().Be(PluginState.Active);
        loaded.Id.Should().Be(FixturePlugin);
        loaded.LoadContext.Should().NotBeNull();
        loaded.Admitted.Kinds.Should().Equal(FixtureKind);

        kind.Plugin.Should().Be(FixturePlugin);
        AssemblyLoadContext.GetLoadContext(runtime.ItemType.Assembly).Should().BeSameAs(loaded.LoadContext);

        contributedProvider.Plugin.Should().Be(FixturePlugin);
        contributedProvider.Family.Should().Be(ProviderFamily.Notifier);
        contributedProvider.Id.Local.Should().Be("proof-notifier");
        AssemblyLoadContext.GetLoadContext(contributedProvider.Provider.GetType().Assembly)
            .Should().BeSameAs(loaded.LoadContext);

        language.Language.Code.Should().Be("x-g02");
        language.PrepareQuery("proof").Should().Be("proof");
        AssemblyLoadContext.GetLoadContext(language.GetType().Assembly).Should().BeSameAs(loaded.LoadContext);

        job.Owner.Should().Be(FixturePlugin);
        job.RegistrationId.Should().Be(FixtureJobId);
        job.Name.Should().Be("G02 admission proof");
        job.MaxConcurrency.Should().Be(1);
        job.ShutdownDeadline.Should().Be(TimeSpan.FromMilliseconds(100));
        job.MediaKind.Should().Be(FixtureKind,
            "the omitted manifest mediaKinds field cannot be the source of this association");
        job.ThrottleKeys.Should().Contain("kind:g02-fixture");
        AssemblyLoadContext.GetLoadContext(job.Job.GetType().Assembly).Should().BeSameAs(loaded.LoadContext);

        queued.JobId.Should().Be(FixtureJobId);
        queued.MediaKind.Should().Be(FixtureKind,
            "the admitted kind must survive beyond registration metadata into the runtime work envelope");
        queued.ThrottleKeys.Should().Contain("kind:g02-fixture");

        claims.Should().NotBeEmpty();
        claims.Should().OnlyContain(claim => claim.Plugin == FixturePlugin && claim.MediaKind == FixtureKind);
        claims.Select(claim => claim.Token.Name).Should().OnlyHaveUniqueItems();

        health.Checks.Should().ContainSingle(check => check.CheckId == "g02.admission.fixture/alive");
        health.Checks.Should().ContainSingle(check => check.CheckId == "g02.admission.fixture/module-alive");
    }

    [Test]
    public async Task ALateManifestMismatchRejectsPreparedCandidatesWithoutPublishingThem()
    {
        var restaged = RestageWithManifest(manifest =>
        {
            manifest["mediaKinds"] = JsonSerializer.SerializeToNode(new[] { "invented-kind" });
        });

        using var provider = BuildProvider(restaged);
        var bootstrapper = Bootstrapper(provider);
        var telemetry = provider.GetRequiredService<ITelemetryEmitter>()
            .Should().BeOfType<RequiredServiceStub>().Subject;

        await bootstrapper.StartAsync(CancellationToken.None);

        var state = bootstrapper.States.Should().ContainSingle().Which;
        var health = await provider.GetRequiredService<IHealthAggregator>().CollectAsync();

        using var assertions = new AssertionScope();
        state.Id.Should().Be(FixturePlugin);
        state.State.Should().Be(PluginState.Quarantined);
        state.ErrorCode.Should().Be(CoreErrorCode.PluginPolicyDeclarationInvalid);
        state.Defects.Should().Contain(defect => defect.Contains("invented-kind", StringComparison.Ordinal));

        provider.GetRequiredService<MediaKindRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<TokenRegistry>().Claims.Should().BeEmpty();
        provider.GetRequiredService<ProviderRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<LanguageDefinitionRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<BackgroundTaskRegistry>().Registrations().Should().BeEmpty();
        provider.GetRequiredService<PluginRuntimeRegistry>().Active.Should().BeEmpty();
        health.Checks.Should().NotContain(check => check.CheckId == "g02.admission.fixture/alive",
            "a prepared health candidate is invisible until the complete publication transaction commits");
        health.Checks.Should().NotContain(check => check.CheckId == "g02.admission.fixture/module-alive",
            "the module's prepared health candidate was never published");
        health.Checks.Should().ContainSingle(check =>
            check.CheckId == "extensions/g02.admission.fixture" && check.Status == HealthStatus.Unhealthy,
            "Host's quarantine report is distinct from health code contributed by an admitted extension");
        AssertCompleteCleanupTelemetry(
            telemetry,
            "late rejection must dispose every activated and directly registered object");
    }

    [Test]
    public async Task AFinalHealthCollisionRollsBackEveryEarlierHostPublication()
    {
        var provider = _provider!;
        var pluginHealth = provider.GetRequiredService<PluginHealthContributor>();
        var squatter = new SquattingHealthContributor();
        var admission = new HealthSquattingAdmission(
            Bootstrapper(provider).Admission,
            provider.GetRequiredService<MediaKindRegistry>(),
            provider.GetRequiredService<LanguageDefinitionRegistry>(),
            provider.GetRequiredService<ProviderRegistry>(),
            provider.GetRequiredService<BackgroundTaskRegistry>(),
            provider.GetRequiredService<TokenRegistry>(),
            provider.GetRequiredService<PluginRuntimeRegistry>(),
            pluginHealth,
            squatter);
        var telemetry = provider.GetRequiredService<ITelemetryEmitter>()
            .Should().BeOfType<RequiredServiceStub>().Subject;

        var result = (await provider.GetRequiredService<PluginLoader>().LoadAllAsync(admission))
            .Should().ContainSingle().Which;
        var health = await pluginHealth.CheckAsync();

        using var assertions = new AssertionScope();
        admission.Visibility.Should().Be(new PreparationVisibility(0, 0, 0, 0, 0, 0),
            "Host preparation builds attempt-local candidates without exposing any registry mutation");
        result.State.Should().Be(PluginState.Quarantined);
        result.ErrorCode.Should().Be(CoreErrorCode.PluginIdConflict);
        result.Defects.Should().ContainSingle(defect =>
            defect.Contains("health", StringComparison.OrdinalIgnoreCase)
            && defect.Contains(FixturePlugin.Value, StringComparison.Ordinal));

        provider.GetRequiredService<MediaKindRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<LanguageDefinitionRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<BackgroundTaskRegistry>().Registrations().Should().BeEmpty();
        provider.GetRequiredService<ProviderRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<TokenRegistry>().Claims.Should().BeEmpty();
        provider.GetRequiredService<JobQueue>().Snapshot().Should().BeEmpty();
        provider.GetRequiredService<PluginRuntimeRegistry>().Active.Should().BeEmpty();

        health.Should().ContainSingle(check =>
            check.CheckId == $"{FixturePlugin}/squatter-alive"
            && check.Status == HealthStatus.Healthy,
            "rollback removes only the failed attempt's exact health candidate, not the prior squatter");
        squatter.CheckCount.Should().Be(1);
        health.Should().NotContain(check => check.CheckId == $"{FixturePlugin}/alive");
        health.Should().NotContain(check => check.CheckId == $"{FixturePlugin}/module-alive");
        AssertCompleteCleanupTelemetry(
            telemetry,
            "a failure at the final Host publication must unwind the already-published kind, language, job and provider");
    }

    [Test]
    public async Task AFailedPackagedReloadCannotReplaceTheOriginalHostLifetimeAuthority()
    {
        var provider = _provider!;
        var bootstrapper = Bootstrapper(provider);
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();
        var providers = provider.GetRequiredService<ProviderRegistry>();
        var languages = provider.GetRequiredService<LanguageDefinitionRegistry>();
        var tokens = provider.GetRequiredService<TokenRegistry>();
        var telemetry = provider.GetRequiredService<ITelemetryEmitter>()
            .Should().BeOfType<RequiredServiceStub>().Subject;

        await bootstrapper.StartAsync(CancellationToken.None);

        var original = runtime.Active.Should().ContainSingle().Which;
        original.RuntimeLease.Should().NotBeNull();
        var originalLease = original.RuntimeLease!;
        originalLease.AdmissionAttempt.Should().NotBeNull();
        var originalAdmission = originalLease.AdmissionAttempt!;
        var originalContext = original.LoadContext.Should().NotBeNull().And.Subject;
        var originalProvider = providers.All.Should().ContainSingle().Which;
        var originalLanguage = languages.All.Should().ContainSingle().Which;
        var originalClaims = tokens.Claims;

        var reload = (await provider.GetRequiredService<PluginLoader>().LoadAllAsync(bootstrapper.Admission))
            .Should().ContainSingle().Which;
        var contributedHealth = await provider.GetRequiredService<IHealthAggregator>().CollectAsync();

        using (new AssertionScope())
        {
            reload.State.Should().Be(PluginState.Quarantined);
            reload.ErrorCode.Should().Be(CoreErrorCode.MediaKindConflict);
            runtime.TryGet(FixturePlugin, out var recorded).Should().BeTrue();
            recorded.Should().BeSameAs(original,
                "a failed reload cannot replace the result which owns the live Host admission receipt");
            runtime.Active.Should().ContainSingle().Which.Should().BeSameAs(original);
            recorded!.LoadContext.Should().BeSameAs(originalContext);
            recorded.RuntimeLease.Should().BeSameAs(originalLease);
            recorded.RuntimeLease!.AdmissionAttempt.Should().BeSameAs(originalAdmission,
                "the failed reload cannot replace the exact receipt which owns Host publication");
            providers.All.Should().ContainSingle().Which.Should().BeSameAs(originalProvider);
            languages.All.Should().ContainSingle().Which.Should().BeSameAs(originalLanguage);
            tokens.Claims.Should().BeEquivalentTo(originalClaims);
            contributedHealth.Checks.Should().ContainSingle(check =>
                check.CheckId == $"{FixturePlugin}/alive");
            contributedHealth.Checks.Should().ContainSingle(check =>
                check.CheckId == $"{FixturePlugin}/module-alive");
            AssertCleanupTelemetry(
                telemetry,
                PreAdmissionFailureCleanupOrder,
                "the rejected reload releases its directly registered instances without touching the original");
        }

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        runtime.Active.Should().BeEmpty();
        runtime.TryGet(FixturePlugin, out var stopped).Should().BeTrue();
        stopped!.State.Should().Be(PluginState.Stopped,
            "successful teardown proves the original Host receipt remained authoritative after the failed reload");
        provider.GetRequiredService<MediaKindRegistry>().All.Should().BeEmpty();
        languages.All.Should().BeEmpty();
        provider.GetRequiredService<BackgroundTaskRegistry>().Registrations().Should().BeEmpty();
        providers.All.Should().BeEmpty();
        tokens.Claims.Should().BeEmpty();
        AssertCleanupTelemetry(
            telemetry,
            [.. PreAdmissionFailureCleanupOrder, .. CleanupOrder],
            "the rejected reload and the original active runtime must each be torn down exactly once");
    }

    [Test]
    public async Task StoppingWithdrawsEveryContributionAndEndsActivatedProviderLifetime()
    {
        var provider = _provider!;
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var implementation = provider.GetRequiredService<ProviderRegistry>().All.Should().ContainSingle().Which.Provider;
        var synchronouslyDisposed = implementation.GetType().GetProperty("IsSynchronouslyDisposed");
        var asynchronouslyDisposed = implementation.GetType().GetProperty("IsAsynchronouslyDisposed");
        var language = provider.GetRequiredService<LanguageDefinitionRegistry>().All.Should().ContainSingle().Which;
        var languageSynchronouslyDisposed = language.GetType().GetProperty("IsSynchronouslyDisposed");
        var languageAsynchronouslyDisposed = language.GetType().GetProperty("IsAsynchronouslyDisposed");
        var telemetry = provider.GetRequiredService<ITelemetryEmitter>()
            .Should().BeOfType<RequiredServiceStub>().Subject;
        var before = await provider.GetRequiredService<IHealthAggregator>().CollectAsync();

        using (new AssertionScope())
        {
            provider.GetRequiredService<MediaKindRegistry>().All.Should().ContainSingle();
            provider.GetRequiredService<TokenRegistry>().Claims.Should().NotBeEmpty();
            provider.GetRequiredService<LanguageDefinitionRegistry>().All.Should().ContainSingle();
            provider.GetRequiredService<BackgroundTaskRegistry>().Registrations().Should().ContainSingle();
            before.Checks.Should().ContainSingle(check => check.CheckId == "g02.admission.fixture/alive");
            before.Checks.Should().ContainSingle(check => check.CheckId == "g02.admission.fixture/module-alive");
            implementation.Should().BeAssignableTo<IDisposable>();
            implementation.Should().BeAssignableTo<IAsyncDisposable>();
            synchronouslyDisposed.Should().NotBeNull("the isolated fixture exposes both teardown paths");
            asynchronouslyDisposed.Should().NotBeNull("the isolated fixture exposes both teardown paths");
            languageSynchronouslyDisposed.Should().NotBeNull("the isolated language exposes both teardown paths");
            languageAsynchronouslyDisposed.Should().NotBeNull("the isolated language exposes both teardown paths");
            synchronouslyDisposed!.GetValue(implementation).Should().Be(false);
            asynchronouslyDisposed!.GetValue(implementation).Should().Be(false);
            languageSynchronouslyDisposed!.GetValue(language).Should().Be(false);
            languageAsynchronouslyDisposed!.GetValue(language).Should().Be(false);
        }

        await bootstrapper.StopAsync(CancellationToken.None);

        var after = await provider.GetRequiredService<IHealthAggregator>().CollectAsync();

        using var assertions = new AssertionScope();
        provider.GetRequiredService<MediaKindRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<TokenRegistry>().Claims.Should().BeEmpty();
        provider.GetRequiredService<ProviderRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<LanguageDefinitionRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<BackgroundTaskRegistry>().Registrations().Should().BeEmpty();
        provider.GetRequiredService<PluginRuntimeRegistry>().Active.Should().BeEmpty();
        bootstrapper.States.Should().ContainSingle().Which.State.Should().Be(PluginState.Stopped);
        after.Checks.Should().NotContain(check => check.CheckId == "g02.admission.fixture/alive");
        after.Checks.Should().NotContain(check => check.CheckId == "g02.admission.fixture/module-alive");
        synchronouslyDisposed!.GetValue(implementation).Should().Be(false,
            "asynchronous teardown is preferred when an activated provider implements both contracts");
        asynchronouslyDisposed!.GetValue(implementation).Should().Be(true,
            "StopAsync must await activated providers rather than merely hiding them");
        languageSynchronouslyDisposed!.GetValue(language).Should().Be(false,
            "asynchronous teardown is preferred when an activated language implements both contracts");
        languageAsynchronouslyDisposed!.GetValue(language).Should().Be(true,
            "StopAsync must await activated languages rather than merely removing them from the registry");
        AssertCompleteCleanupTelemetry(
            telemetry,
            "StopAsync must not return before every activated and directly registered object is torn down");
    }

    [Test]
    public async Task AnOverrunningJobDefersDisposalAndUnloadWhilePluginCodeIsExecuting()
    {
        var provider = _provider!;
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>().Active.Should().ContainSingle().Which;
        var jobs = provider.GetRequiredService<BackgroundTaskRegistry>();
        var registration = jobs.Registrations().Should().ContainSingle().Which;
        var release = registration.Job.GetType().GetMethod("ReleaseExecution");
        var implementation = provider.GetRequiredService<ProviderRegistry>().All.Should().ContainSingle().Which.Provider;
        var asynchronouslyDisposed = implementation.GetType().GetProperty("IsAsynchronouslyDisposed");
        var telemetry = provider.GetRequiredService<ITelemetryEmitter>()
            .Should().BeOfType<RequiredServiceStub>().Subject;
        var scheduler = provider.GetRequiredService<JobScheduler>();

        release.Should().NotBeNull("the isolated overrun fixture must provide a bounded way to finish");
        asynchronouslyDisposed.Should().NotBeNull();
        (await jobs.TriggerJobAsync(FixtureJobId)).Should().BeTrue();
        (await scheduler.TickAsync()).Should().Be(1);
        scheduler.HasInFlight(FixturePlugin).Should().BeTrue();

        (await scheduler.DrainAsync()).Should().ContainSingle().Which.Should().Be(FixtureJobId);
        await bootstrapper.StopAsync(CancellationToken.None);

        using (new AssertionScope())
        {
            bootstrapper.States.Should().ContainSingle().Which.State.Should().Be(PluginState.Active);
            provider.GetRequiredService<PluginRuntimeRegistry>().Active.Should().ContainSingle().Which
                .Should().BeSameAs(runtime);
            provider.GetRequiredService<MediaKindRegistry>().All.Should().ContainSingle();
            provider.GetRequiredService<ProviderRegistry>().All.Should().ContainSingle();
            runtime.LoadContext.Should().NotBeNull(
                "unloading while the scheduler still executes plugin code would be unsafe");
            asynchronouslyDisposed!.GetValue(implementation).Should().Be(false);
            telemetry.TelemetryEvents.Should().NotContain(
                telemetryEvent => CleanupOrder.Contains(telemetryEvent.Message, StringComparer.Ordinal));
        }

        release!.Invoke(registration.Job, parameters: null);
        for (var attempt = 0; attempt < 100 && scheduler.InFlight > 0; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        scheduler.InFlight.Should().Be(0, "the fixture execution was explicitly released");
        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        bootstrapper.States.Should().ContainSingle().Which.State.Should().Be(PluginState.Stopped);
        provider.GetRequiredService<PluginRuntimeRegistry>().Active.Should().BeEmpty();
        asynchronouslyDisposed!.GetValue(implementation).Should().Be(true);
        AssertCompleteCleanupTelemetry(telemetry, "the deferred lifetime must remain cleanable once execution ends");
    }

    [Test]
    public async Task ACancelingJobDrainsBeforeItsExtensionObjectsAreDisposed()
    {
        var provider = _provider!;
        var bootstrapper = Bootstrapper(provider);

        await bootstrapper.StartAsync(CancellationToken.None);

        var jobs = provider.GetRequiredService<BackgroundTaskRegistry>();
        var scheduler = provider.GetRequiredService<JobScheduler>();
        var telemetry = provider.GetRequiredService<ITelemetryEmitter>()
            .Should().BeOfType<RequiredServiceStub>().Subject;
        using var execution = new CancellationTokenSource();

        (await jobs.TriggerJobAsync(
            FixtureJobId,
            new Dictionary<string, object> { ["waitForCancellation"] = true })).Should().BeTrue();
        (await scheduler.TickAsync(execution.Token)).Should().Be(1);
        execution.Cancel();

        (await scheduler.DrainAsync()).Should().BeEmpty();
        scheduler.InFlight.Should().Be(0);
        telemetry.TelemetryEvents.Should().NotContain(
            telemetryEvent => CleanupOrder.Contains(telemetryEvent.Message, StringComparer.Ordinal),
            "draining execution does not itself dispose extension-owned objects");
        telemetry.TelemetryEvents.Should().ContainSingle(telemetryEvent =>
            telemetryEvent.Message == JobCancellationObservedMessage);

        await bootstrapper.StopAsync(CancellationToken.None);

        var lifecycle = telemetry.TelemetryEvents
            .Where(telemetryEvent => telemetryEvent.Message == JobCancellationObservedMessage
                                     || CleanupOrder.Contains(
                                         telemetryEvent.Message,
                                         StringComparer.Ordinal))
            .ToArray();

        using var assertions = new AssertionScope();
        bootstrapper.States.Should().ContainSingle().Which.State.Should().Be(PluginState.Stopped);
        lifecycle.Should().OnlyContain(telemetryEvent =>
            IsAttributedCleanup(telemetryEvent, telemetryEvent.Message));
        lifecycle.Select(telemetryEvent => telemetryEvent.Message).Should().Equal(
            [JobCancellationObservedMessage, .. CleanupOrder],
            "job cancellation completes before any extension-owned instance is disposed");
        AssertCompleteCleanupTelemetry(telemetry,
            "ordinary cancellation must finish before teardown disposes the job, provider, language and module");
    }

    [Test]
    public async Task StoppingDropsRuntimeRootsAndUnregistersTheExtensionLoadContext()
    {
        var provider = _provider!;
        var bootstrapper = Bootstrapper(provider);
        var registry = provider.GetRequiredService<PluginRuntimeRegistry>();

        await bootstrapper.StartAsync(CancellationToken.None);

        var active = registry.Active.Should().ContainSingle().Which;
        var context = active.LoadContext.Should().NotBeNull().And.Subject;
        AssemblyLoadContext.All.Should().Contain(loadContext => ReferenceEquals(loadContext, context));

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        registry.TryGet(FixturePlugin, out var stopped).Should().BeTrue();
        stopped!.State.Should().Be(PluginState.Stopped);
        stopped.LoadContext.Should().BeNull();
        stopped.Ledger.Should().BeNull();
        AssemblyLoadContext.All.Should().NotContain(loadContext => ReferenceEquals(loadContext, context),
            "Unload removes the context from the runtime's active context inventory deterministically");
    }

    [Test]
    public async Task AThrowingUnloadHandlerIsRecordedWithoutAbortingTheRemainingLoadBatch()
    {
        var root = RestageThrowingFixtureBesideMovies();
        using var provider = BuildProvider(root, _stateRoot, captureLogs: true);
        var bootstrapper = Bootstrapper(provider);
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();
        var logs = provider.GetRequiredService<RecordingLoggerProvider>();
        var telemetry = provider.GetRequiredService<ITelemetryEmitter>()
            .Should().BeOfType<RequiredServiceStub>().Subject;

        await bootstrapper.StartAsync(CancellationToken.None);

        var started = bootstrapper.States.ToDictionary(state => state.Id);
        var active = runtime.Active.Should().ContainSingle().Which;
        var admittedKinds = provider.GetRequiredService<MediaKindRegistry>().All
            .Select(kind => kind.Kind)
            .ToArray();
        var cleanupFailures = logs.Entries
            .Where(entry => entry.EventId.Id == 1004)
            .ToArray();

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        started[FixturePlugin].State.Should().Be(PluginState.Quarantined);
        started[FixturePlugin].ErrorCode.Should().Be(CoreErrorCode.PluginPolicyDeclarationInvalid);
        started[PluginId.FromString("movies")].State.Should().Be(PluginState.Active,
            "a faulty unloading callback in one extension cannot abort the remaining installation batch");
        active.Id.Should().Be(PluginId.FromString("movies"));
        admittedKinds.Should().Equal(MediaKindId.FromString("movies"));
        cleanupFailures.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains(FixturePlugin.Value, StringComparison.Ordinal)
            && entry.Message.Contains(ThrowingUnloadFailure, StringComparison.Ordinal));
        AssertCompleteCleanupTelemetry(telemetry,
            "the throwing callback runs only after every fixture-owned value has still been released");
        runtime.Active.Should().BeEmpty();
        bootstrapper.States.Should().ContainSingle(state =>
            state.Id == PluginId.FromString("movies") && state.State == PluginState.Stopped,
            "the independently admitted extension remains under normal lifecycle authority");
    }

    [Test]
    public async Task AThrowingJobEnvelopeGetterIsContainedAfterLanguageActivationAndLoadingContinues()
    {
        var root = RestageThrowingJobEnvelopeBesideMovies();
        using var provider = BuildProvider(root, _stateRoot);
        var bootstrapper = Bootstrapper(provider);
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();
        var telemetry = provider.GetRequiredService<ITelemetryEmitter>()
            .Should().BeOfType<RequiredServiceStub>().Subject;

        await bootstrapper.StartAsync(CancellationToken.None);

        var started = bootstrapper.States.ToDictionary(state => state.Id);
        var active = runtime.Active.Should().ContainSingle().Which;
        var admittedKinds = provider.GetRequiredService<MediaKindRegistry>().All
            .Select(kind => kind.Kind)
            .ToArray();
        var fixtureProviders = provider.GetRequiredService<ProviderRegistry>().All
            .Where(registered => registered.Plugin == FixturePlugin)
            .ToArray();
        var fixtureLanguages = provider.GetRequiredService<LanguageDefinitionRegistry>().All
            .Where(language => language.Language.Code == "x-g02")
            .ToArray();
        var fixtureJobs = provider.GetRequiredService<BackgroundTaskRegistry>().Registrations()
            .Where(job => job.Owner == FixturePlugin)
            .ToArray();
        var fixtureClaims = provider.GetRequiredService<TokenRegistry>().Claims
            .Where(claim => claim.Plugin == FixturePlugin)
            .ToArray();
        var fixtureQueue = provider.GetRequiredService<JobQueue>().Snapshot()
            .Where(work => work.JobId == FixtureJobId)
            .ToArray();
        var health = await provider.GetRequiredService<IHealthAggregator>().CollectAsync();
        var cleanup = CleanupEvents(telemetry);

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        started[FixturePlugin].State.Should().Be(PluginState.Quarantined);
        started[FixturePlugin].ErrorCode.Should().Be(CoreErrorCode.JobSchedulingFailed);
        started[FixturePlugin].Defects.Should().ContainSingle(defect =>
            defect.Contains(ThrowingJobIdFailure, StringComparison.Ordinal));
        started[MoviesPlugin].State.Should().Be(PluginState.Active,
            "one extension-controlled envelope getter cannot abort the remaining installation batch");
        active.Id.Should().Be(MoviesPlugin);
        admittedKinds.Should().Equal(MediaKindId.FromString("movies"));
        fixtureProviders.Should().BeEmpty();
        fixtureLanguages.Should().BeEmpty();
        fixtureJobs.Should().BeEmpty();
        fixtureClaims.Should().BeEmpty();
        fixtureQueue.Should().BeEmpty();
        health.Checks.Should().NotContain(check =>
            check.CheckId == "g02.admission.fixture/alive"
            || check.CheckId == "g02.admission.fixture/module-alive");
        cleanup.Select(telemetryEvent => telemetryEvent.Message).Should().Equal(
            PostLanguagePreparationFailureCleanupOrder,
            "the activated language and every directly registered fixture object are released exactly once");
        cleanup.Should().OnlyContain(
            telemetryEvent => IsAttributedCleanup(telemetryEvent, telemetryEvent.Message));
        runtime.Active.Should().BeEmpty();
        bootstrapper.States.Should().ContainSingle(state =>
            state.Id == MoviesPlugin && state.State == PluginState.Stopped);
    }

    [Test]
    public async Task AProviderRequestingTheRootServiceLocatorIsRejectedWithoutResolvingItAndLoadingContinues()
    {
        var root = RestageForbiddenProviderBesideMovies();
        using var provider = BuildProvider(root, _stateRoot);
        var bootstrapper = Bootstrapper(provider);
        var runtime = provider.GetRequiredService<PluginRuntimeRegistry>();
        var telemetry = provider.GetRequiredService<ITelemetryEmitter>()
            .Should().BeOfType<RequiredServiceStub>().Subject;

        await bootstrapper.StartAsync(CancellationToken.None);

        var started = bootstrapper.States.ToDictionary(state => state.Id);
        var active = runtime.Active.Should().ContainSingle().Which;
        var admittedKinds = provider.GetRequiredService<MediaKindRegistry>().All
            .Select(kind => kind.Kind)
            .ToArray();
        var fixtureProviders = provider.GetRequiredService<ProviderRegistry>().All
            .Where(registered => registered.Plugin == FixturePlugin)
            .ToArray();
        var fixtureLanguages = provider.GetRequiredService<LanguageDefinitionRegistry>().All
            .Where(language => language.Language.Code == "x-g02")
            .ToArray();
        var fixtureJobs = provider.GetRequiredService<BackgroundTaskRegistry>().Registrations()
            .Where(job => job.Owner == FixturePlugin)
            .ToArray();
        var fixtureClaims = provider.GetRequiredService<TokenRegistry>().Claims
            .Where(claim => claim.Plugin == FixturePlugin)
            .ToArray();
        var fixtureQueue = provider.GetRequiredService<JobQueue>().Snapshot()
            .Where(work => work.JobId == FixtureJobId)
            .ToArray();
        var health = await provider.GetRequiredService<IHealthAggregator>().CollectAsync();
        var cleanup = CleanupEvents(telemetry);

        await bootstrapper.StopAsync(CancellationToken.None);

        using var assertions = new AssertionScope();
        started[FixturePlugin].State.Should().Be(PluginState.Quarantined);
        started[FixturePlugin].ErrorCode.Should().Be(CoreErrorCode.PluginLoadFailure);
        started[FixturePlugin].Defects.Should().ContainSingle(defect =>
            defect.Contains("G02ForbiddenServiceProviderNotifier", StringComparison.Ordinal)
            && defect.Contains("no supported activation constructor", StringComparison.Ordinal)
            && defect.Contains("Host service provider", StringComparison.Ordinal)
            && defect.Contains("never exposed", StringComparison.Ordinal));
        started[FixturePlugin].Defects.Should().NotContain(defect =>
            defect.Contains(ForbiddenConstructorInvocation, StringComparison.Ordinal),
            "the forbidden constructor must be rejected before extension code can execute");
        telemetry.TelemetryEvents.Should().NotContain(telemetryEvent =>
            telemetryEvent.Message == ForbiddenRootResolutionMessage,
            "the Host root provider must never reach extension code");
        started[MoviesPlugin].State.Should().Be(PluginState.Active,
            "one extension's forbidden constructor cannot abort the remaining installation batch");
        active.Id.Should().Be(MoviesPlugin);
        admittedKinds.Should().Equal(MediaKindId.FromString("movies"));
        fixtureProviders.Should().BeEmpty();
        fixtureLanguages.Should().BeEmpty();
        fixtureJobs.Should().BeEmpty();
        fixtureClaims.Should().BeEmpty();
        fixtureQueue.Should().BeEmpty();
        health.Checks.Should().NotContain(check =>
            check.CheckId == "g02.admission.fixture/alive"
            || check.CheckId == "g02.admission.fixture/module-alive");
        cleanup.Select(telemetryEvent => telemetryEvent.Message).Should().Equal(
            PostLanguagePreparationFailureCleanupOrder,
            "the activated language and every directly registered fixture object are released exactly once");
        cleanup.Should().OnlyContain(
            telemetryEvent => IsAttributedCleanup(telemetryEvent, telemetryEvent.Message));
        runtime.Active.Should().BeEmpty();
        bootstrapper.States.Should().ContainSingle(state =>
            state.Id == MoviesPlugin && state.State == PluginState.Stopped);
    }

    private string RestageWithManifest(Action<JsonObject> rewrite)
    {
        var source = Path.Combine(_packagedRoot, FixturePlugin.Value);
        var root = Path.Combine(_stateRoot, "restaged");
        var destination = Path.Combine(root, FixturePlugin.Value);

        CopyPackage(source, destination);

        var manifestPath = Path.Combine(destination, "plugin.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        rewrite(manifest);
        File.WriteAllText(
            manifestPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        return root;
    }

    private string RestageThrowingFixtureBesideMovies()
        => RestageFixtureBesideMovies(
            "throwing-unload",
            "0.1.1",
            manifest => manifest["mediaKinds"] = JsonSerializer.SerializeToNode(new[] { "invented-kind" }));

    private string RestageThrowingJobEnvelopeBesideMovies()
        => RestageFixtureBesideMovies("throwing-job-envelope", "0.1.2");

    private string RestageForbiddenProviderBesideMovies()
        => RestageFixtureBesideMovies("forbidden-provider", "0.1.3");

    private string RestageFixtureBesideMovies(
        string scenario,
        string version,
        Action<JsonObject>? rewrite = null)
    {
        var root = Path.Combine(_stateRoot, scenario);
        var fixture = Path.Combine(root, "a-admission-fixture");
        var movies = Path.Combine(root, "z-movies");

        CopyPackage(Path.Combine(_packagedRoot, FixturePlugin.Value), fixture);
        CopyPackage(Path.Combine(AppContext.BaseDirectory, "PackagedPlugins", "movies"), movies);

        var manifestPath = Path.Combine(fixture, "plugin.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["version"] = version;
        rewrite?.Invoke(manifest);
        File.WriteAllText(
            manifestPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        return root;
    }

    private static void CopyPackage(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private ServiceProvider BuildProvider(string pluginRoot) => BuildProvider(pluginRoot, _stateRoot);

    private static ServiceProvider BuildProvider(
        string pluginRoot,
        string stateRoot,
        bool captureLogs = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arronix:Host:ExtensionFolder"] = pluginRoot,
                ["Arronix:Plugins:RootFolder"] = pluginRoot,
                ["Arronix:Plugins:StateFolder"] = Path.Combine(stateRoot, "state"),
                ["Arronix:Library:RootFolders:0"] = Path.Combine(stateRoot, "library"),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        if (captureLogs)
        {
            services.AddSingleton<RecordingLoggerProvider>();
            services.AddSingleton<ILoggerProvider>(
                provider => provider.GetRequiredService<RecordingLoggerProvider>());
        }

        services.AddArronixHost(configuration);
        AddRequiredServices(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static void AddRequiredServices(IServiceCollection services)
    {
        services.AddSingleton<ICacheProvider, RequiredServiceStub>();
        services.AddSingleton<ITelemetryEmitter, RequiredServiceStub>();
        services.AddSingleton<IEventPublisher, RequiredServiceStub>();
        services.AddSingleton<IHostRuntimeInfo, RequiredServiceStub>();
        services.AddSingleton<IOperatingSystemInfo, RequiredServiceStub>();
    }

    private static PluginBootstrapper Bootstrapper(IServiceProvider provider)
        => provider.GetServices<IHostedService>().OfType<PluginBootstrapper>().Single();

    private static bool IsAttributedCleanup(TelemetryEvent telemetryEvent, string message)
        => telemetryEvent.Message == message
           && telemetryEvent.Tags.TryGetValue("plugin", out var plugin)
           && plugin == FixturePlugin.Value;

    private static IReadOnlyList<TelemetryEvent> CleanupEvents(RequiredServiceStub telemetry)
        => telemetry.TelemetryEvents
            .Where(telemetryEvent => CleanupOrder.Contains(telemetryEvent.Message, StringComparer.Ordinal))
            .ToArray();

    private static void AssertCompleteCleanupTelemetry(RequiredServiceStub telemetry, string because)
        => AssertCleanupTelemetry(telemetry, CleanupOrder, because);

    private static void AssertCleanupTelemetry(
        RequiredServiceStub telemetry,
        IReadOnlyList<string> expected,
        string because)
    {
        var cleanup = CleanupEvents(telemetry);

        cleanup.Should().OnlyContain(
            telemetryEvent => IsAttributedCleanup(telemetryEvent, telemetryEvent.Message),
            because);
        cleanup.Select(telemetryEvent => telemetryEvent.Message)
            .Should().Equal(expected, because);
    }

    private sealed class HealthSquattingAdmission(
        IPluginAdmissionCheck inner,
        MediaKindRegistry mediaKinds,
        LanguageDefinitionRegistry languages,
        ProviderRegistry providers,
        BackgroundTaskRegistry jobs,
        TokenRegistry tokens,
        PluginRuntimeRegistry runtime,
        PluginHealthContributor health,
        IHealthContributor squatter) : IPluginAdmissionCheck
    {
        public PreparationVisibility? Visibility { get; private set; }

        public PluginAdmissionResult Prepare(ValidatedManifest manifest, PluginRegistrationLedger ledger)
        {
            var result = inner.Prepare(manifest, ledger);
            Visibility = new PreparationVisibility(
                mediaKinds.All.Count,
                languages.All.Count,
                providers.All.Count,
                jobs.Registrations().Count,
                tokens.Claims.Count,
                runtime.Active.Count);

            if (result.IsAdmitted)
            {
                health.Add(manifest.Id, [squatter]);
            }

            return result;
        }
    }

    private sealed record PreparationVisibility(
        int MediaKinds,
        int Languages,
        int Providers,
        int Jobs,
        int Tokens,
        int ActiveRuntimes);

    private sealed class SquattingHealthContributor : IHealthContributor
    {
        private int _checkCount;

        public int CheckCount => Volatile.Read(ref _checkCount);

        public string ContributorId => "squatter";

        public Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _checkCount);
            return Task.FromResult<IReadOnlyList<HealthCheck>>(
                [HealthCheck.Healthy("squatter-alive", "Host-side collision sentinel")]);
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider, ILogger
    {
        private readonly object _gate = new();
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => this;

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

    /// <summary>Satisfies platform services the fixture does not exercise.</summary>
    private sealed class RequiredServiceStub :
        ICacheProvider,
        ITelemetryEmitter,
        IEventPublisher,
        IHostRuntimeInfo,
        IOperatingSystemInfo
    {
        private readonly object _telemetryGate = new();
        private readonly List<TelemetryEvent> _telemetryEvents = [];

        public IReadOnlyList<TelemetryEvent> TelemetryEvents
        {
            get
            {
                lock (_telemetryGate)
                {
                    return [.. _telemetryEvents];
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

        public ICache<TValue> GetCache<TOwner, TValue>(string partition) => throw Unused();

        public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
            => throw Unused();

        public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
            string partition,
            Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
            TimeSpan? lifetime = null) => throw Unused();

        public void Emit(TelemetryEvent telemetryEvent)
        {
            lock (_telemetryGate)
            {
                _telemetryEvents.Add(telemetryEvent);
            }
        }

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : IDomainEvent => Task.CompletedTask;

        private static InvalidOperationException Unused() =>
            new("The G02 admission fixture must not exercise undeclared platform privileges.");
    }
}
