using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Import;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;
using Arronix.Host.Health;
using Arronix.Host.Media;
using Arronix.Host.Providers;
using Arronix.Host.Scheduling;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Every contract the loader hands over is experimental; the bootstrapper is where they are committed into
// the host's registries.
#pragma warning disable ARX0003
#pragma warning disable ARX0006
#pragma warning disable ARX0009
#pragma warning disable ARX0020
#pragma warning disable ARX0013
#pragma warning disable ARX0014
#pragma warning disable ARX0015
#pragma warning disable ARX0016

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
public sealed partial class PluginBootstrapper : IHostedService, IPluginAdmissionCheck
{
    private readonly PluginLoader _loader;
    private readonly MediaKindRegistry _kinds;
    private readonly MediaTypeBinder _mediaTypes;
    private readonly ProviderRegistry _providers;
    private readonly ProviderDefinitionStore _definitions;
    private readonly BackgroundTaskRegistry _jobs;
    private readonly PluginHealthContributor _pluginHealth;
    private readonly IHealthAggregator _health;
    private readonly JobScheduler _scheduler;
    private readonly TimeProvider _clock;
    private readonly ILogger<PluginBootstrapper> _log;

    private readonly ConcurrentDictionary<PluginId, PluginRuntimeState> _states = new();
    private readonly ConcurrentBag<PluginId> _committed = [];

    /// <summary>
    /// Creates a bootstrapper.
    /// </summary>
    /// <param name="loader">The load pipeline.</param>
    /// <param name="kinds">Where admitted media kinds go.</param>
    /// <param name="mediaTypes">The binder that turns a typed registration into an admitted kind.</param>
    /// <param name="providers">Where admitted provider implementations go.</param>
    /// <param name="definitions">Where configured definitions are reconciled against implementations.</param>
    /// <param name="jobs">Where admitted background jobs go.</param>
    /// <param name="pluginHealth">Where admitted health contributors go.</param>
    /// <param name="health">The report whose cache is invalidated when extension state changes.</param>
    /// <param name="scheduler">The scheduler, told when startup-scheduled work may begin.</param>
    /// <param name="clock">The clock state changes are stamped with.</param>
    /// <param name="log">Where the lifecycle reports what it did.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public PluginBootstrapper(
        PluginLoader loader,
        MediaKindRegistry kinds,
        MediaTypeBinder mediaTypes,
        ProviderRegistry providers,
        ProviderDefinitionStore definitions,
        BackgroundTaskRegistry jobs,
        PluginHealthContributor pluginHealth,
        IHealthAggregator health,
        JobScheduler scheduler,
        TimeProvider clock,
        ILogger<PluginBootstrapper> log)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(mediaTypes);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(pluginHealth);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);

        _loader = loader;
        _kinds = kinds;
        _mediaTypes = mediaTypes;
        _providers = providers;
        _definitions = definitions;
        _jobs = jobs;
        _pluginHealth = pluginHealth;
        _health = health;
        _scheduler = scheduler;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Gets what became of every extension, ordered by identifier.
    /// </summary>
    public IReadOnlyList<PluginRuntimeState> States
        => [.. _states.Values.OrderBy(state => state.Id.Value, StringComparer.Ordinal)];

    /// <inheritdoc />
    /// <remarks>
    /// Runs the whole pipeline once. The loader owns steps one to fifteen; this class owns the host's own
    /// checks inside them and the commit that follows, and it is the only place where a registration becomes
    /// visible to the rest of the platform.
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var results = _loader.LoadAll(this);

        // Anything the loader quarantined after admission had already been committed, so it is withdrawn
        // here. Admission cannot itself be the commit point: three of the pipeline's checks run after it,
        // and a media kind left registered by an extension that never activated would be a catalog nobody
        // can serve.
        foreach (var quarantined in results.Where(result => !result.IsActive && result.Id is { } id && _committed.Contains(id)))
        {
            Withdraw(quarantined.Id!.Value);
        }

        foreach (var result in results)
        {
            Record(result);
        }

        await _definitions.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        _kinds.Refresh(_definitions.Query(ProviderFamily.Indexer, enabledOnly: true).Count > 0);
        _health.Invalidate();
        _scheduler.ReleaseStartupJobs();

        ActivationComplete(
            _log,
            results.Count(result => result.IsActive),
            results.Count,
            _kinds.All.Count);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Teardown reverses activation. There is deliberately no stop method on the extension contract: an
    /// extension that could refuse to stop would be an extension that could keep the host alive, so the host
    /// simply withdraws everything it registered and disposes what asked to be disposed.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var state in States.Reverse())
        {
            Withdraw(state.Id);

            _states[state.Id] = state with
            {
                State = PluginState.Quarantined,
                ChangedAt = _clock.GetUtcNow(),
            };
        }

        await _definitions.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        _health.Invalidate();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The host's own admission checks, run inside the loader's pipeline: the declared shape is resolved,
    /// the declared surface is checked against it, and everything the extension registered is committed into
    /// the host's registries. Every one of those can refuse, and refusing here quarantines the extension with
    /// the full defect list rather than with the first fault found.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "The signature is the loader's, not this class's; it returns a verdict, a machine-readable code and the complete defect list.")]
    public bool TryAdmit(
        ValidatedManifest manifest,
        PluginRegistrationLedger ledger,
        out CoreErrorCode errorCode,
        out IReadOnlyList<string> defects)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(ledger);

        errorCode = CoreErrorCode.PluginShapeInvalid;

        if (!TryAdmitMediaKinds(manifest, ledger, out errorCode, out defects))
        {
            Withdraw(manifest.Id);
            return false;
        }

        if (!TryAdmitJobs(manifest, ledger, out defects))
        {
            errorCode = CoreErrorCode.JobSchedulingFailed;
            Withdraw(manifest.Id);
            return false;
        }

        if (!TryAdmitProviders(manifest, ledger, out defects))
        {
            errorCode = CoreErrorCode.PluginIdConflict;
            Withdraw(manifest.Id);
            return false;
        }

        _pluginHealth.Add(manifest.Id, ledger.Registered<IHealthContributor>());
        _committed.Add(manifest.Id);

        defects = [];
        return true;
    }

    private bool TryAdmitMediaKinds(
        ValidatedManifest manifest,
        PluginRegistrationLedger ledger,
        out CoreErrorCode errorCode,
        out IReadOnlyList<string> defects)
    {
        errorCode = CoreErrorCode.PluginShapeInvalid;

        var typed = ledger.Registered<IMediaTypeRegistration>();
        var shapes = ledger.Registered<IMediaShapeProvider>();

        if (typed.Count == 0 && shapes.Count == 0)
        {
            defects = [];
            return true;
        }

        if (typed.Count > 0
            && !TryAdmitTypedKinds(manifest, typed, out errorCode, out defects))
        {
            return false;
        }

        if (shapes.Count == 0)
        {
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

            if (!_kinds.TryRegister(contribution, out _, out var shapeDefects))
            {
                errorCode = shapeDefects.Count > 0 ? shapeDefects[0].Code : CoreErrorCode.PluginShapeInvalid;
                found.AddRange(shapeDefects.Select(defect => $"{defect.Path}: {defect.Message}"));
            }
        }

        defects = found;
        return found.Count == 0;
    }

    /// <summary>
    /// Admits every typed media kind the extension registered.
    /// </summary>
    /// <remarks>
    /// The binder does the work: it reopens the registration's type arguments, derives the descriptors, puts
    /// them through the same gate a hand-written shape goes through, builds the host engines and admits the
    /// result into the same registry. Nothing here knows what a movie is, which is the point — this method
    /// would be identical for every kind that will ever exist.
    /// </remarks>
    private bool TryAdmitTypedKinds(
        ValidatedManifest manifest,
        IReadOnlyList<IMediaTypeRegistration> registrations,
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

            if (!_mediaTypes.TryRegister(contribution, out _, out var kindDefects))
            {
                errorCode = kindDefects.Count > 0 ? kindDefects[0].Code : CoreErrorCode.PluginShapeInvalid;
                found.AddRange(kindDefects.Select(defect => $"{defect.Path}: {defect.Message}"));
            }
        }

        defects = found;
        return found.Count == 0;
    }

    private bool TryAdmitJobs(
        ValidatedManifest manifest,
        PluginRegistrationLedger ledger,
        out IReadOnlyList<string> defects)
    {
        var found = new List<string>();
        var kind = manifest.MediaKinds.Count == 1 ? manifest.MediaKinds[0] : (MediaKindId?)null;

        foreach (var registration in ledger.ScheduledJobs)
        {
            try
            {
                _jobs.RegisterJob(
                    manifest.Id,
                    manifest.GrantedCapabilities,
                    registration.Job,
                    registration.Schedule,
                    kind);
            }
            catch (ArronixException failure)
            {
                found.Add($"job[{registration.Job.JobId}]: {failure.Message}");
            }
        }

        defects = found;
        return found.Count == 0;
    }

    private bool TryAdmitProviders(
        ValidatedManifest manifest,
        PluginRegistrationLedger ledger,
        out IReadOnlyList<string> defects)
    {
        var found = new List<string>();

        Register<IndexerRegistration>(
            ledger, ProviderFamily.Indexer, manifest, found, r => (r.Descriptor, r.Provider));
        Register<DownloaderRegistration>(
            ledger, ProviderFamily.Downloader, manifest, found, r => (r.Descriptor, r.Provider));
        Register<NotifierRegistration>(
            ledger, ProviderFamily.Notifier, manifest, found, r => (r.Descriptor, r.Provider));
        Register<CatalogerRegistration>(
            ledger, ProviderFamily.Cataloger, manifest, found, r => (r.Descriptor, r.Provider));
        Register<CuratorRegistration>(
            ledger, ProviderFamily.Curator, manifest, found, r => (r.Descriptor, r.Provider));

        defects = found;
        return found.Count == 0;
    }

    private void Register<TRegistration>(
        PluginRegistrationLedger ledger,
        ProviderFamily family,
        ValidatedManifest manifest,
        List<string> found,
        Func<TRegistration, (ProviderDescriptor Descriptor, IProvider Provider)> unpack)
        where TRegistration : class
    {
        foreach (var registration in ledger.Registered<TRegistration>())
        {
            var (descriptor, provider) = unpack(registration);

            try
            {
                _providers.Register(manifest.Id, family, descriptor, provider);
            }
            catch (ArronixException failure)
            {
                found.Add($"provider[{descriptor.LocalId}]: {failure.Message}");
            }
        }
    }

    private static TContract? Pick<TContract>(PluginRegistrationLedger ledger)
        where TContract : class
        {
        var registered = ledger.Registered<TContract>();
        return registered.Count == 0 ? null : registered[0];
    }

    private void Withdraw(PluginId plugin)
    {
        _kinds.RemoveByPlugin(plugin);
        _providers.RemoveByPlugin(plugin);
        _jobs.RemoveByPlugin(plugin);
        _pluginHealth.RemoveByPlugin(plugin);
    }

    private void Record(PluginLoadResult result)
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

        _states[id] = state;

        if (result.IsActive)
        {
            ExtensionActivated(_log, id.ToString(), state.Version ?? "unknown");
        }
        else
        {
            ExtensionQuarantined(_log, id.ToString(), result.ErrorCode?.ToString() ?? "unknown", result.Message);
        }
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
}
