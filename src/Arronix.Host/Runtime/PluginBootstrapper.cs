using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.ExceptionServices;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Import;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Languages;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;
using Arronix.Host.Health;
using Arronix.Host.Languages;
using Arronix.Host.Media;
using Arronix.Host.Providers;
using Arronix.Host.Scheduling;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace Arronix.Host.Runtime;

/// <summary>
/// Runs the extension lifecycle: discover, validate, load, register, activate, stop.
/// </summary>
/// <remarks>
/// <para>
/// Extensions are admitted after the service provider is built and never mutate it. That removes the entire
/// two-phase-container class of bug, in which extensions must load before the container is built and
/// therefore before configuration, logging and the file system are available to them — the reason one
/// surveyed application needs a "retry startup with extensions disabled" escape hatch.
/// </para>
/// <para>
/// Activation order is by extension identifier. Extensions have no dependencies on each other, because
/// direct interaction between them is forbidden, so a topological sort would be sorting an edgeless graph;
/// ordinal order is deterministic, which is what actually matters for reproducing a fault.
/// </para>
/// <para>
/// Failure is quarantine, never fatal. Zero extensions loading is a valid state and the host serves an empty
/// catalog. The one thing this class must never do is let a third-party fault stop the platform.
/// </para>
/// </remarks>
public sealed partial class PluginBootstrapper : IHostedService
{
    private readonly PluginLoader _loader;
    private readonly PluginRuntimeRegistry _runtime;
    private readonly MediaKindRegistry _kinds;
    private readonly MediaTypeBinder _mediaTypes;
    private readonly LanguageDefinitionRegistry _languages;
    private readonly ProviderRegistry _providers;
    private readonly ProviderDefinitionStore _definitions;
    private readonly BackgroundTaskRegistry _jobs;
    private readonly PluginHealthContributor _pluginHealth;
    private readonly IHealthAggregator _health;
    private readonly JobScheduler _scheduler;
    private readonly TimeProvider _clock;
    private readonly ILogger<PluginBootstrapper> _log;
    private readonly PluginPublicationGate _publication;
    private readonly IPluginAdmissionCheck _admission;

    private readonly ConcurrentDictionary<PluginId, HostAdmissionAttempt> _committed = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private int _lifecycleState;

    private enum LifecycleState
    {
        Created,
        Starting,
        Started,
        Stopping,
        StopIncomplete,
        Stopped,
    }

    /// <summary>
    /// Creates a bootstrapper.
    /// </summary>
    /// <param name="loader">The load pipeline.</param>
    /// <param name="runtime">The authoritative lifecycle result registry.</param>
    /// <param name="kinds">Where admitted media kinds go.</param>
    /// <param name="mediaTypes">The binder that turns a typed registration into an admitted kind.</param>
    /// <param name="languages">Where admitted language implementations go.</param>
    /// <param name="providers">Where admitted provider implementations go.</param>
    /// <param name="definitions">Where configured definitions are reconciled against implementations.</param>
    /// <param name="jobs">Where admitted background jobs go.</param>
    /// <param name="pluginHealth">Where admitted health contributors go.</param>
    /// <param name="health">The report whose cache is invalidated when extension state changes.</param>
    /// <param name="scheduler">The scheduler, told when startup-scheduled work may begin.</param>
    /// <param name="clock">The clock state changes are stamped with.</param>
    /// <param name="log">Where the lifecycle reports what it did.</param>
    /// <param name="publication">The shared extension-publication boundary.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public PluginBootstrapper(
        PluginLoader loader,
        PluginRuntimeRegistry runtime,
        MediaKindRegistry kinds,
        MediaTypeBinder mediaTypes,
        LanguageDefinitionRegistry languages,
        ProviderRegistry providers,
        ProviderDefinitionStore definitions,
        BackgroundTaskRegistry jobs,
        PluginHealthContributor pluginHealth,
        IHealthAggregator health,
        JobScheduler scheduler,
        TimeProvider clock,
        ILogger<PluginBootstrapper> log,
        PluginPublicationGate publication)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(mediaTypes);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(pluginHealth);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(publication);

        if (!ReferenceEquals(loader.PublicationGate, publication)
            || !ReferenceEquals(runtime.PublicationGate, publication)
            || !ReferenceEquals(kinds.PublicationGate, publication)
            || !ReferenceEquals(languages.PublicationGate, publication)
            || !ReferenceEquals(providers.PublicationGate, publication)
            || !ReferenceEquals(jobs.PublicationGate, publication)
            || !ReferenceEquals(pluginHealth.PublicationGate, publication))
        {
            throw new InvalidOperationException(
                "Every extension lifecycle registry must share the bootstrapper's publication boundary.");
        }

        if (!mediaTypes.Uses(kinds, providers)
            || !definitions.Uses(providers)
            || !pluginHealth.UsesRuntime(runtime)
            || !scheduler.UsesRegistry(jobs))
        {
            throw new InvalidOperationException(
                "The extension lifecycle was composed from different registry instances.");
        }

        _loader = loader;
        _runtime = runtime;
        _kinds = kinds;
        _mediaTypes = mediaTypes;
        _languages = languages;
        _providers = providers;
        _definitions = definitions;
        _jobs = jobs;
        _pluginHealth = pluginHealth;
        _health = health;
        _scheduler = scheduler;
        _clock = clock;
        _log = log;
        _publication = publication;
        _admission = new AdmissionCheck(this);
    }

    /// <summary>Gets the internal loader-to-Host admission seam for lifecycle tests.</summary>
    internal IPluginAdmissionCheck Admission => _admission;

    /// <summary>
    /// Gets what became of every extension, ordered by identifier.
    /// </summary>
    public IReadOnlyList<PluginRuntimeState> States
        =>
        [
            .. _runtime.All
                .Where(result => result.Id is not null)
                .Select(ToRuntimeState)
                .OrderBy(state => state.Id.Value, StringComparer.Ordinal),
        ];

    /// <inheritdoc />
    /// <remarks>
    /// Runs the whole pipeline once. This class prepares Host-owned candidates for the loader's semantic and
    /// ownership checks; the loader publishes the complete attempt only after every check passes.
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CurrentLifecycleState != LifecycleState.Created)
            {
                throw new InvalidOperationException("The extension lifecycle has already been started or stopped.");
            }

            SetLifecycleState(LifecycleState.Starting);

            var results = _loader.LoadAll(_admission, cancellationToken);

            foreach (var result in results)
            {
                Report(result);
            }

            await _definitions.ReconcileAsync(cancellationToken).ConfigureAwait(false);
            _kinds.Refresh(_definitions.Query(ProviderFamily.Indexer, enabledOnly: true).Count > 0);
            _health.Invalidate();

            ActivationComplete(
                _log,
                results.Count(result => result.IsActive),
                results.Count,
                _kinds.All.Count);

            // This is deliberately the final, non-throwing startup step. Nothing can fail after jobs become
            // runnable and force recovery to race work which the scheduler has already started.
            _scheduler.ReleaseStartupJobs();
            SetLifecycleState(LifecycleState.Started);
        }
        catch
        {
            if (CurrentLifecycleState == LifecycleState.Starting)
            {
                // A hosted-service start failure prevents normal StopAsync from being a reliable cleanup
                // path. Withdraw everything already committed without the canceled startup token, then
                // preserve the original exception which tells the Host why startup failed.
                var recovered = await RecoverFailedStartupAsync().ConfigureAwait(false);
                SetLifecycleState(recovered ? LifecycleState.Stopped : LifecycleState.StopIncomplete);
            }

            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Teardown reverses activation. There is deliberately no stop method on the extension contract: an
    /// extension that could refuse to stop would be an extension that could keep the host alive, so the host
    /// simply withdraws everything it registered and disposes what asked to be disposed.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Once shutdown is requested, registry and lifetime integrity is not optional. The caller's token
        // may stop other hosted services waiting, but cannot interrupt exact withdrawal halfway through.
        _ = cancellationToken;
        await _lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            if (CurrentLifecycleState == LifecycleState.Stopped)
            {
                return;
            }

            SetLifecycleState(LifecycleState.Stopping);
            var failures = new List<Exception>();

            try
            {
                // Hosted services may be stopped concurrently by Generic Host. Own scheduler quiescence
                // here as well as by registration order so no extension instance is withdrawn while one of
                // its jobs is still executing. BackgroundService stop is idempotent when Host also calls it.
                await _scheduler.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                failures.Add(failure);
            }

            try
            {
                await TeardownExtensionsAsync().ConfigureAwait(false);
            }
// Host infrastructure faults are accumulated so reconciliation and health projection still reach the
// post-withdrawal state. Third-party cleanup faults are already contained by TeardownExtensionsAsync.
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                failures.Add(failure);
            }

            try
            {
                await _definitions.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
                _kinds.Refresh(_definitions.Query(ProviderFamily.Indexer, enabledOnly: true).Count > 0);
            }
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                failures.Add(failure);
            }

            try
            {
                _health.Invalidate();
            }
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                failures.Add(failure);
            }

            var stoppedCompletely = failures.Count == 0 && _runtime.Active.Count == 0;
            SetLifecycleState(stoppedCompletely ? LifecycleState.Stopped : LifecycleState.StopIncomplete);

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            if (failures.Count > 1)
            {
                throw new AggregateException("Extension shutdown encountered multiple Host infrastructure failures.", failures);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private LifecycleState CurrentLifecycleState
        => (LifecycleState)Volatile.Read(ref _lifecycleState);

    private bool AdmissionMayPublish
        => CurrentLifecycleState is LifecycleState.Created or LifecycleState.Starting or LifecycleState.Started;

    private void SetLifecycleState(LifecycleState state)
        => Volatile.Write(ref _lifecycleState, (int)state);

    private async Task TeardownExtensionsAsync()
    {
        foreach (var active in _runtime.Active.Reverse())
        {
            var plugin = active.Id
                ?? throw new InvalidOperationException("An active extension result must carry its identifier.");

            HostAdmissionAttempt? attempt = null;
            PluginRuntimeLease? lifetime = null;
            var deferred = false;

            using (_publication.EnterWrite())
            {
                // Tick holds the matching read lease until an exact registration is represented in its
                // in-flight table. Checking under the writer makes start versus withdrawal a total order:
                // either this sees the run and preserves its code, or no later run can start from it.
                if (_scheduler.HasInFlight(plugin))
                {
                    deferred = true;
                }
                else if (!_committed.TryGetValue(plugin, out attempt)
                    || !ReferenceEquals(active.RuntimeLease?.AdmissionAttempt, attempt))
                {
                    ExtensionTeardownAuthorityMismatch(_log, plugin.ToString());
                    continue;
                }
                else
                {
                    attempt.Unpublish();
                    if (!_runtime.TryStop(active, _clock.GetUtcNow(), out lifetime))
                    {
                        throw new InvalidOperationException(
                            $"Extension '{plugin}' changed runtime authority while holding the publication gate.");
                    }
                }
            }

            if (deferred)
            {
                ExtensionTeardownDeferred(_log, plugin.ToString());
                continue;
            }

            if (attempt is not null)
            {
                await attempt.DisposeOwnedAsync().ConfigureAwait(false);
            }

            if (lifetime is not null)
            {
                foreach (var failure in await lifetime.DisposeAsync().ConfigureAwait(false))
                {
                    ExtensionCleanupFailed(_log, plugin.ToString(), failure);
                }
            }
        }
    }

    private async Task<bool> RecoverFailedStartupAsync()
    {
        var recovered = true;

        try
        {
            await TeardownExtensionsAsync().ConfigureAwait(false);
        }
// Recovery is best-effort across Host infrastructure faults. Extension cleanup itself is contained inside
// the teardown path; this catch prevents a secondary fault from hiding the original startup exception.
#pragma warning disable CA1031
        catch (Exception failure)
#pragma warning restore CA1031
        {
            recovered = false;
            StartupRecoveryFailed(_log, "extension teardown", failure.Message);
        }

        try
        {
            await _definitions.ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            _kinds.Refresh(_definitions.Query(ProviderFamily.Indexer, enabledOnly: true).Count > 0);
        }
#pragma warning disable CA1031
        catch (Exception failure)
#pragma warning restore CA1031
        {
            recovered = false;
            StartupRecoveryFailed(_log, "provider reconciliation", failure.Message);
        }

        try
        {
            _health.Invalidate();
        }
#pragma warning disable CA1031
        catch (Exception failure)
#pragma warning restore CA1031
        {
            recovered = false;
            StartupRecoveryFailed(_log, "health invalidation", failure.Message);
        }

        return recovered && _runtime.Active.Count == 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The host's own admission checks, run inside the loader's pipeline: the declared shape is resolved,
    /// the declared surface is checked against it, and every contribution is activated into an attempt-local
    /// candidate. Nothing is published here. Every preparation can refuse, and refusing quarantines the
    /// extension with the full defect list rather than with the first fault found.
    /// </para>
    /// <para>
    /// What it answers with is the inventory the host is prepared to admit, taken from the prepared kinds
    /// themselves rather than derived a second time. That is what lets the loader's remaining steps ask the
    /// platform what an extension supplies instead of asking the extension's declaration file.
    /// </para>
    /// </remarks>
    private PluginAdmissionResult Prepare(
        ValidatedManifest manifest,
        PluginRegistrationLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(ledger);

        if (!AdmissionMayPublish)
        {
            return PluginAdmissionResult.Refused(
                CoreErrorCode.PluginLoadFailure,
                [$"Extension '{manifest.Id}' cannot be prepared after Host extension shutdown has begun."]);
        }

        IReadOnlyList<LanguageDefinitionRegistry.RegisteredLanguage> languages = [];
        IReadOnlyList<RegisteredProvider> providers = [];

        try
        {
            if (!TryPrepareMediaKinds(manifest, ledger, out var mediaKinds, out var errorCode, out var defects))
            {
                return PluginAdmissionResult.Refused(errorCode, defects);
            }

            var inventory = new AdmittedInventory(mediaKinds.Select(Inventory).ToArray());
            var activation = ledger.ActivationContext is { } context
                ? new PluginActivationScope(context)
                : null;

            if (!TryPrepareLanguages(manifest, ledger, activation, out languages, out defects))
            {
                return PluginAdmissionResult.Refused(CoreErrorCode.PluginLoadFailure, defects);
            }

            if (!TryPrepareJobs(manifest, inventory, ledger, out var jobs, out defects))
            {
                DisposeActivated(languages);
                languages = [];
                return PluginAdmissionResult.Refused(CoreErrorCode.JobSchedulingFailed, defects);
            }

            if (!TryPrepareProviders(manifest, ledger, activation, out providers, out defects))
            {
                DisposeActivated(languages);
                languages = [];
                return PluginAdmissionResult.Refused(CoreErrorCode.PluginLoadFailure, defects);
            }

            var health = _pluginHealth.Prepare(manifest.Id, ledger.Registered<IHealthContributor>());
            var attempt = new HostAdmissionAttempt(
                this,
                manifest.Id,
                inventory,
                mediaKinds,
                languages,
                providers,
                jobs,
                health);

            return PluginAdmissionResult.Prepared(attempt);
        }
// Every value read here ultimately comes from extension code. A throwing getter or malformed custom
// implementation quarantines this extension and releases any Host-activated values already prepared.
#pragma warning disable CA1031
        catch (Exception failure)
#pragma warning restore CA1031
        {
            DisposeActivated(providers);
            DisposeActivated(languages);
            return PluginAdmissionResult.Refused(
                CoreErrorCode.PluginLoadFailure,
                [$"Host preparation failed: {failure.Message}"]);
        }
    }

    /// <remarks>
    /// Both registration paths end at the same registry, so both contribute to the same inventory. The kind
    /// and its tokens are read back off the admitted <see cref="RegisteredMediaKind"/> rather than recomputed
    /// from the registration, because the projection the platform will serve is the only honest answer to
    /// what was admitted.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "One private step returning a verdict, what it admitted, a machine-readable code and the complete defect list.")]
    private bool TryPrepareMediaKinds(
        ValidatedManifest manifest,
        PluginRegistrationLedger ledger,
        out IReadOnlyList<RegisteredMediaKind> admitted,
        out CoreErrorCode errorCode,
        out IReadOnlyList<string> defects)
    {
        errorCode = CoreErrorCode.PluginShapeInvalid;
        admitted = [];

        var typed = ledger.Registered<IMediaTypeRegistration>();
        var shapes = ledger.Registered<IMediaShapeProvider>();

        if (typed.Count == 0 && shapes.Count == 0)
        {
            defects = [];
            return true;
        }

        var registered = new List<RegisteredMediaKind>(typed.Count + shapes.Count);

        if (typed.Count > 0
            && !TryPrepareTypedKinds(manifest, typed, registered, out errorCode, out defects))
        {
            return false;
        }

        if (shapes.Count == 0)
        {
            admitted = registered;
            defects = [];
            return true;
        }

        var sources = ledger.Registered<IMediaItemSource>();
        var surfaces = ledger.Registered<PluginIntentSurface>();
        var found = new List<string>();

        foreach (var provider in shapes)
        {
            var shape = provider.Shape;
            var source = sources.FirstOrDefault(candidate => candidate.MediaKind == shape.Kind);

            if (source is null)
            {
                found.Add(
                    $"kind[{shape.Kind}]: a media kind that declares a shape also supplies the catalog it is a shape of.");
                continue;
            }

            var contribution = new MediaKindContribution
            {
                Plugin = manifest.Id,
                PluginVersion = manifest.Version.ToString(),
                Capabilities = manifest.GrantedCapabilities,
                Shape = shape,
                Items = source,
                Intent = surfaces.FirstOrDefault(candidate => candidate.MediaKind == shape.Kind),
                Matcher = Pick<IReleaseMatcher>(ledger),
                QueryPlanner = Pick<IReleaseQueryPlanner>(ledger),
                Parser = Pick<IReleaseParser>(ledger),
                Quality = Pick<IQualityModel>(ledger),
                Import = Pick<IImportPipeline>(ledger),
                Naming = Pick<IRenamePolicy>(ledger),
                Layout = Pick<ILibraryLayout>(ledger),
                IdResolver = Pick<IMediaIdResolver>(ledger),
            };

            if (_kinds.TryPrepare(contribution, out var admittedKind, out var shapeDefects))
            {
                if (registered.Any(candidate => candidate.Kind == admittedKind!.Kind))
                {
                    errorCode = CoreErrorCode.MediaKindConflict;
                    found.Add($"kind[{admittedKind!.Kind}]: the extension supplies the same media kind more than once.");
                }
                else
                {
                    registered.Add(admittedKind!);
                }
            }
            else
            {
                errorCode = shapeDefects.Count > 0 ? shapeDefects[0].Code : CoreErrorCode.PluginShapeInvalid;
                found.AddRange(shapeDefects.Select(defect => $"{defect.Path}: {defect.Message}"));
            }
        }

        admitted = registered;
        defects = found;
        return found.Count == 0;
    }

    /// <summary>
    /// Reads one admitted kind back as the inventory entry the loader consumes.
    /// </summary>
    private static AdmittedMediaKind Inventory(RegisteredMediaKind kind)
        => new(kind.Kind, kind.Shape.Declaration.Tokens);

    /// <summary>
    /// Admits every typed media kind the extension registered.
    /// </summary>
    /// <remarks>
    /// The binder does the work: it reopens the registration's type arguments, derives the descriptors, puts
    /// them through the same gate a hand-written shape goes through, builds the host engines and admits the
    /// result into the same registry. Nothing here knows what a movie is, which is the point — this method
    /// would be identical for every kind that will ever exist.
    /// </remarks>
    private bool TryPrepareTypedKinds(
        ValidatedManifest manifest,
        IReadOnlyList<IMediaTypeRegistration> registrations,
        List<RegisteredMediaKind> registered,
        out CoreErrorCode errorCode,
        out IReadOnlyList<string> defects)
    {
        errorCode = CoreErrorCode.PluginShapeInvalid;
        var found = new List<string>();

        foreach (var registration in registrations)
        {
            var contribution = new TypedContribution
            {
                Plugin = manifest.Id,
                PluginVersion = manifest.Version.ToString(),
                Capabilities = manifest.GrantedCapabilities,
                Registration = registration,
            };

            if (_mediaTypes.TryPrepare(contribution, out var admittedKind, out var kindDefects))
            {
                if (registered.Any(candidate => candidate.Kind == admittedKind!.Kind))
                {
                    errorCode = CoreErrorCode.MediaKindConflict;
                    found.Add($"kind[{admittedKind!.Kind}]: the extension supplies the same media kind more than once.");
                }
                else
                {
                    registered.Add(admittedKind!);
                }
            }
            else
            {
                errorCode = kindDefects.Count > 0 ? kindDefects[0].Code : CoreErrorCode.PluginShapeInvalid;
                found.AddRange(kindDefects.Select(defect => $"{defect.Path}: {defect.Message}"));
            }
        }

        defects = found;
        return found.Count == 0;
    }

    /// <remarks>
    /// A job is associated with the one kind the extension actually supplies. The kind comes only from what
    /// was just admitted; an authoritative empty inventory means no kind, because a manifest cannot become
    /// the fallback authority on a fact derived from the extension's own types.
    /// </remarks>
    private bool TryPrepareJobs(
        ValidatedManifest manifest,
        AdmittedInventory admitted,
        PluginRegistrationLedger ledger,
        out IReadOnlyList<RegisteredJob> prepared,
        out IReadOnlyList<string> defects)
    {
        var found = new List<string>();
        var jobs = new List<RegisteredJob>(ledger.ScheduledJobs.Count);
        var kind = admitted.Kinds.Count == 1 ? admitted.Kinds[0] : (MediaKindId?)null;

        foreach (var registration in ledger.ScheduledJobs)
        {
            try
            {
                if (!_jobs.TryPrepare(
                    manifest.Id,
                    manifest.GrantedCapabilities,
                    registration.Job,
                    registration.Schedule,
                    kind,
                    out var candidate,
                    out var error))
                {
                    found.Add($"job[{registration.Job.GetType().Name}]: {error}");
                    continue;
                }

                if (jobs.Any(job => string.Equals(
                        job.RegistrationId,
                        candidate!.RegistrationId,
                        StringComparison.Ordinal)))
                {
                    found.Add($"job[{candidate!.RegistrationId}]: the extension registers this job more than once.");
                    continue;
                }

                jobs.Add(candidate!);
            }
// Every metadata getter is third-party code. Its exception quarantines this extension and must not escape
// Host startup or cause another getter call while the failure is being described.
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                found.Add($"job[{registration.Job.GetType().Name}]: {failure.Message}");
            }
        }

        prepared = jobs;
        defects = found;
        return found.Count == 0;
    }

    private bool TryPrepareLanguages(
        ValidatedManifest manifest,
        PluginRegistrationLedger ledger,
        PluginActivationScope? activation,
        out IReadOnlyList<LanguageDefinitionRegistry.RegisteredLanguage> prepared,
        out IReadOnlyList<string> defects)
    {
        var found = new List<string>();
        var languages = new List<LanguageDefinitionRegistry.RegisteredLanguage>();
        var registrations = ledger.Registered<LanguageDefinitionRegistration>();

        if (activation is null && registrations.Count > 0)
        {
            prepared = [];
            defects = ["language activation: the extension's capability-scoped context was not retained"];
            return false;
        }

        foreach (var registration in registrations)
        {
            ILanguageDefinition? language = null;
            try
            {
                language = (ILanguageDefinition)activation!.CreateInstance(registration.ImplementationType);

                if (!_languages.TryPrepare(manifest.Id, language, out var candidate, out var error))
                {
                    found.Add($"language[{registration.ImplementationType.Name}]: {error}");
                    DisposeActivated(language);
                    continue;
                }

                if (languages.Any(entry => string.Equals(
                        entry.Code,
                        candidate!.Code,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add($"language[{candidate!.Code}]: the extension registers this language more than once.");
                    DisposeActivated(language);
                    continue;
                }

                languages.Add(candidate!);
            }
// Third-party construction is a quarantine boundary: an extension constructor may throw anything, and it
// must not terminate Host startup. Host validation outside the activation call still fails loudly.
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                found.Add($"language[{registration.ImplementationType.Name}]: {failure.Message}");
                if (language is not null)
                {
                    DisposeActivated(language);
                }
            }
        }

        if (found.Count > 0)
        {
            DisposeActivated(languages);
            languages.Clear();
        }

        prepared = languages;
        defects = found;
        return found.Count == 0;
    }

    private bool TryPrepareProviders(
        ValidatedManifest manifest,
        PluginRegistrationLedger ledger,
        PluginActivationScope? activation,
        out IReadOnlyList<RegisteredProvider> prepared,
        out IReadOnlyList<string> defects)
    {
        var found = new List<string>();
        var providers = new List<RegisteredProvider>();
        var registrations = ledger.Registered<ProviderTypeRegistration>();

        if (activation is null && registrations.Count > 0)
        {
            found.Add("provider activation: the extension's capability-scoped context was not retained");
        }

        foreach (var registration in registrations)
        {
            IProvider? provider = null;
            try
            {
                provider = (IProvider)activation!.CreateInstance(registration.ImplementationType);

                if (!registration.ContractType.IsInstanceOfType(provider))
                {
                    found.Add(
                        $"provider[{registration.Descriptor.LocalId}]: activated type "
                        + $"'{registration.ImplementationType.Name}' does not implement "
                        + $"'{registration.ContractType.Name}'.");
                    DisposeActivated(provider);
                    continue;
                }

                if (!_providers.TryPrepare(
                    manifest.Id,
                    registration.Family,
                    registration.Descriptor,
                    provider,
                    registration.MediaItemType,
                    out var candidate,
                    out var error))
                {
                    found.Add($"provider[{registration.Descriptor.LocalId}]: {error}");
                    DisposeActivated(provider);
                    continue;
                }

                if (providers.Any(entry => entry.Id == candidate.Id))
                {
                    found.Add($"provider[{registration.Descriptor.LocalId}]: the extension registers this provider more than once.");
                    DisposeActivated(provider);
                    continue;
                }

                providers.Add(candidate);
            }
// Third-party construction is a quarantine boundary for the same reason as language activation above.
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                found.Add($"provider[{registration.Descriptor.LocalId}]: {failure.Message}");
                if (provider is not null)
                {
                    DisposeActivated(provider);
                }
            }
        }

        if (found.Count > 0)
        {
            DisposeActivated(providers);
            providers.Clear();
        }

        prepared = providers;
        defects = found;
        return found.Count == 0;
    }

    private static TContract? Pick<TContract>(PluginRegistrationLedger ledger)
        where TContract : class
        {
        var registered = ledger.Registered<TContract>();
        return registered.Count == 0 ? null : registered[0];
    }

    private void DisposeActivated(
        IEnumerable<LanguageDefinitionRegistry.RegisteredLanguage> languages)
    {
        foreach (var language in languages.Reverse())
        {
            DisposeActivated(language.Definition);
        }
    }

    private void DisposeActivated(IEnumerable<RegisteredProvider> providers)
    {
        foreach (var provider in providers.Reverse())
        {
            DisposeActivated(provider.Provider);
        }
    }

    private void DisposeActivated(object instance)
    {
        try
        {
            switch (instance)
            {
                case IAsyncDisposable asyncDisposable:
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
// Third-party teardown is the same containment boundary as activation: one faulty disposer is reported and
// the remaining attempt-owned objects are still released.
#pragma warning disable CA1031
        catch (Exception failure)
#pragma warning restore CA1031
        {
            ExtensionCleanupFailed(_log, instance.GetType().FullName ?? instance.GetType().Name, failure.Message);
        }
    }

    /// <summary>
    /// One attempt-local Host transaction. Every candidate is fully built before this object exists; commit
    /// only performs collision rechecks and dictionary publication under the shared publication gate.
    /// </summary>
    private sealed class HostAdmissionAttempt : IPluginAdmissionAttempt
    {
        private readonly PluginBootstrapper _owner;
        private readonly IReadOnlyList<RegisteredMediaKind> _mediaKinds;
        private readonly IReadOnlyList<LanguageDefinitionRegistry.RegisteredLanguage> _languages;
        private readonly IReadOnlyList<RegisteredProvider> _providers;
        private readonly IReadOnlyList<RegisteredJob> _jobs;
        private readonly PluginHealthContributor.RegisteredHealthContribution _health;
        private int _state;
        private bool _disposed;

        internal HostAdmissionAttempt(
            PluginBootstrapper owner,
            PluginId plugin,
            AdmittedInventory inventory,
            IReadOnlyList<RegisteredMediaKind> mediaKinds,
            IReadOnlyList<LanguageDefinitionRegistry.RegisteredLanguage> languages,
            IReadOnlyList<RegisteredProvider> providers,
            IReadOnlyList<RegisteredJob> jobs,
            PluginHealthContributor.RegisteredHealthContribution health)
        {
            _owner = owner;
            Plugin = plugin;
            Inventory = inventory;
            _mediaKinds = [.. mediaKinds];
            _languages = [.. languages];
            _providers = [.. providers];
            _jobs = [.. jobs];
            _health = health;
        }

        public PluginId Plugin { get; }

        public AdmittedInventory Inventory { get; }

        public bool TryCommit(out CoreErrorCode errorCode, out IReadOnlyList<string> defects)
        {
            using var publication = _owner._publication.EnterWrite();

            lock (this)
            {
                if (!_owner.AdmissionMayPublish)
                {
                    errorCode = CoreErrorCode.PluginLoadFailure;
                    defects = [$"Extension '{Plugin}' cannot publish after Host extension shutdown has begun."];
                    return false;
                }

                if (_state != 0)
                {
                    errorCode = CoreErrorCode.PluginIdConflict;
                    defects = [$"Admission attempt for extension '{Plugin}' is no longer pending."];
                    return false;
                }

                if (_owner._committed.ContainsKey(Plugin))
                {
                    errorCode = CoreErrorCode.PluginIdConflict;
                    defects = [$"Extension '{Plugin}' already has a committed admission attempt."];
                    return false;
                }

                var publishedKinds = new List<RegisteredMediaKind>();
                var publishedLanguages = new List<LanguageDefinitionRegistry.RegisteredLanguage>();
                var publishedProviders = new List<RegisteredProvider>();
                var publishedJobs = new List<RegisteredJob>();
                var healthPublished = false;
                var found = new List<string>();
                errorCode = CoreErrorCode.PluginIdConflict;

                try
                {
                    foreach (var candidate in _mediaKinds)
                    {
                        if (_owner._kinds.TryPublish(candidate, out var kindDefects))
                        {
                            publishedKinds.Add(candidate);
                            continue;
                        }

                        errorCode = kindDefects.Count > 0
                            ? kindDefects[0].Code
                            : CoreErrorCode.MediaKindConflict;
                        found.AddRange(kindDefects.Select(defect => $"{defect.Path}: {defect.Message}"));
                        break;
                    }

                    if (found.Count == 0)
                    {
                        foreach (var candidate in _languages)
                        {
                            if (_owner._languages.TryPublish(candidate, out var error))
                            {
                                publishedLanguages.Add(candidate);
                                continue;
                            }

                            found.Add($"language[{candidate.Code}]: {error}");
                            break;
                        }
                    }

                    if (found.Count == 0)
                    {
                        foreach (var candidate in _jobs)
                        {
                            if (_owner._jobs.TryPublish(candidate, out var error))
                            {
                                publishedJobs.Add(candidate);
                                continue;
                            }

                            errorCode = CoreErrorCode.JobSchedulingFailed;
                            found.Add($"job[{candidate.RegistrationId}]: {error}");
                            break;
                        }
                    }

                    if (found.Count == 0)
                    {
                        foreach (var candidate in _providers)
                        {
                            if (_owner._providers.TryPublish(candidate, out var error))
                            {
                                publishedProviders.Add(candidate);
                                continue;
                            }

                            found.Add($"provider[{candidate.Id}]: {error}");
                            break;
                        }
                    }

                    if (found.Count == 0)
                    {
                        healthPublished = _owner._pluginHealth.TryPublish(_health);
                        if (!healthPublished)
                        {
                            found.Add($"health[{Plugin}]: contributors are already published for this extension.");
                        }
                    }

                    if (found.Count == 0 && !_owner._committed.TryAdd(Plugin, this))
                    {
                        found.Add($"Extension '{Plugin}' acquired another committed admission attempt during publication.");
                    }
                }
// Nothing in commit deliberately executes extension code, but a registry fault must still roll back the
// candidates already published by this attempt before it is reported.
#pragma warning disable CA1031
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    errorCode = CoreErrorCode.PluginLoadFailure;
                    found.Add($"Host publication failed: {failure.Message}");
                }

                if (found.Count > 0)
                {
                    RollbackPublished(
                        publishedKinds,
                        publishedLanguages,
                        publishedProviders,
                        publishedJobs,
                        healthPublished);
                    defects = found;
                    return false;
                }

                _state = 1;
                defects = [];
                return true;
            }
        }

        public void Rollback()
        {
            Unpublish();
            DisposeOwned();
        }

        internal void Unpublish()
        {
            using var publication = _owner._publication.EnterWrite();

            lock (this)
            {
                if (_state == 1)
                {
                    RollbackPublished(
                        _mediaKinds,
                        _languages,
                        _providers,
                        _jobs,
                        healthPublished: true);

                    ((ICollection<KeyValuePair<PluginId, HostAdmissionAttempt>>)_owner._committed)
                        .Remove(new KeyValuePair<PluginId, HostAdmissionAttempt>(Plugin, this));
                }

                _state = 2;
            }
        }

        internal void DisposeOwned()
        {
            lock (this)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _owner.DisposeActivated(_providers);
            _owner.DisposeActivated(_languages);
        }

        internal async ValueTask DisposeOwnedAsync()
        {
            lock (this)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            foreach (var instance in _providers
                         .Reverse()
                         .Select(provider => provider.Provider)
                         .Concat(_languages.Reverse().Select(language => (object)language.Definition)))
            {
                try
                {
                    if (instance is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    else if (instance is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
// Third-party teardown remains isolated during the awaited shutdown path too.
#pragma warning disable CA1031
                catch (Exception failure)
#pragma warning restore CA1031
                {
                    ExtensionCleanupFailed(
                        _owner._log,
                        instance.GetType().FullName ?? instance.GetType().Name,
                        failure.Message);
                }
            }
        }

        private void RollbackPublished(
            IReadOnlyList<RegisteredMediaKind> mediaKinds,
            IReadOnlyList<LanguageDefinitionRegistry.RegisteredLanguage> languages,
            IReadOnlyList<RegisteredProvider> providers,
            IReadOnlyList<RegisteredJob> jobs,
            bool healthPublished)
        {
            if (healthPublished)
            {
                _owner._pluginHealth.Remove(_health);
            }

            foreach (var provider in providers.Reverse())
            {
                _owner._providers.Remove(provider);
            }

            foreach (var job in jobs.Reverse())
            {
                _owner._jobs.Remove(job);
            }

            foreach (var language in languages.Reverse())
            {
                _owner._languages.Remove(language);
            }

            foreach (var kind in mediaKinds.Reverse())
            {
                _owner._kinds.Remove(kind);
            }
        }
    }

    private sealed class AdmissionCheck(PluginBootstrapper owner) : IPluginAdmissionCheck
    {
        PluginAdmissionResult IPluginAdmissionCheck.Prepare(
            ValidatedManifest manifest,
            PluginRegistrationLedger ledger)
            => owner.Prepare(manifest, ledger);
    }

    private void Report(PluginLoadResult result)
    {
        if (result.Id is not { } id)
        {
            return;
        }

        var state = new PluginRuntimeState(
            id,
            result.State,
            result.Manifest?.Declaration.Name,
            result.Manifest?.Version.ToString(),
            result.Manifest?.GrantedCapabilities ?? CapabilitySet.None,
            result.ErrorCode,
            result.Message,
            result.Defects,
            result.ChangedAt);

        if (result.IsActive)
        {
            ExtensionActivated(_log, id.ToString(), state.Version ?? "unknown");
        }
        else
        {
            ExtensionQuarantined(_log, id.ToString(), result.ErrorCode?.ToString() ?? "unknown", result.Message);
        }
    }

    private static PluginRuntimeState ToRuntimeState(PluginLoadResult result)
    {
        var id = result.Id ?? throw new InvalidOperationException("A runtime state requires an extension identifier.");
        return new PluginRuntimeState(
            id,
            result.State,
            result.Manifest?.Declaration.Name,
            result.Manifest?.Version.ToString(),
            result.Manifest?.GrantedCapabilities ?? CapabilitySet.None,
            result.ErrorCode,
            result.Message,
            result.Defects,
            result.ChangedAt);
    }

    [LoggerMessage(
        EventId = 9200,
        Level = LogLevel.Information,
        Message = "Extension '{Extension}' version {Version} is active.")]
    private static partial void ExtensionActivated(ILogger logger, string extension, string version);

    [LoggerMessage(
        EventId = 9201,
        Level = LogLevel.Error,
        Message = "Extension '{Extension}' is quarantined with {ErrorCode}: {Reason}")]
    private static partial void ExtensionQuarantined(
        ILogger logger,
        string extension,
        string errorCode,
        string? reason);

    [LoggerMessage(
        EventId = 9202,
        Level = LogLevel.Information,
        Message = "{Active} of {Installed} extensions activated, contributing {MediaKinds} media kinds.")]
    private static partial void ActivationComplete(
        ILogger logger,
        int active,
        int installed,
        int mediaKinds);

    [LoggerMessage(
        EventId = 9203,
        Level = LogLevel.Error,
        Message = "Extension-owned instance '{InstanceType}' failed during cleanup: {Reason}")]
    private static partial void ExtensionCleanupFailed(
        ILogger logger,
        string instanceType,
        string reason);

    [LoggerMessage(
        EventId = 9204,
        Level = LogLevel.Error,
        Message = "Extension '{Extension}' still has a scheduled job executing after its shutdown deadline; "
            + "its registrations, instances, and load context remain live until process exit.")]
    private static partial void ExtensionTeardownDeferred(ILogger logger, string extension);

    [LoggerMessage(
        EventId = 9205,
        Level = LogLevel.Critical,
        Message = "Extension '{Extension}' has mismatched Host and runtime admission authorities; teardown "
            + "was refused so neither side is withdrawn independently.")]
    private static partial void ExtensionTeardownAuthorityMismatch(ILogger logger, string extension);

    [LoggerMessage(
        EventId = 9206,
        Level = LogLevel.Error,
        Message = "Extension startup recovery failed during {Phase}: {Reason}")]
    private static partial void StartupRecoveryFailed(ILogger logger, string phase, string reason);
}
