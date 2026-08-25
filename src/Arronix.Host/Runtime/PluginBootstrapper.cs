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
using Arronix.Common.Contributions;
using Arronix.Common.Lifetimes;
using Arronix.Host.Health;
using Arronix.Host.Languages;
using Arronix.Host.Media;
using Arronix.Host.Media.Catalog;
using Arronix.Host.Providers;
using Arronix.Host.Scheduling;
using Arronix.Plugins.Dependencies;
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
/// Activation order is the resolved package graph's order: a package follows everything it requires, and
/// packages that require nothing of each other are ordered by identifier. Teardown reverses the order
/// packages were actually published in. Order alone is not the safety property, though — a dependency is
/// released only when nothing still depends on it, which is checked under the same lease that withdraws it.
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
    private readonly PackageDependencyRegistry _dependencies;
    private readonly SharedContractStore _contracts;
    private readonly IPluginAdmissionCheck _admission;

    private readonly ConcurrentDictionary<PluginId, HostAdmissionAttempt> _committed = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private int _lifecycleState;

    internal enum LifecycleState
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
    /// <remarks>
    /// The package dependency registry is taken from the loader rather than injected beside it. Teardown
    /// has to read exactly the pins and edges admission wrote; two instances that merely resolved from the
    /// same container would satisfy any equality check written about them and still be two registries.
    /// </remarks>
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

        if (!ReferenceEquals(loader.Dependencies.PublicationGate, publication)
            || !ReferenceEquals(loader.PublicationGate, publication)
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
        _dependencies = loader.Dependencies;
        _contracts = loader.SharedContracts;
        _admission = new AdmissionCheck(this);
    }

    /// <summary>Gets the internal loader-to-Host admission seam for lifecycle tests.</summary>
    internal IPluginAdmissionCheck Admission => _admission;

    /// <summary>Gets where the extension lifecycle has got to.</summary>
    internal LifecycleState ShutdownState => CurrentLifecycleState;

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

            var results = await _loader.LoadAllAsync(_admission, cancellationToken).ConfigureAwait(false);

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

            // Taken under the same write lease a publication takes, so the two are a total order: a
            // package either commits wholly before shutdown claims the gate, or it observes Stopping inside
            // the lease and is refused. A state write outside the gate would leave a third outcome, in
            // which a package roots itself after Stop has begun and nothing withdraws it.
            using (_publication.EnterWrite())
            {
                SetLifecycleState(LifecycleState.Stopping);
            }

            var failures = new List<Exception>();
            IReadOnlyList<PluginId> incomplete = [];

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
                incomplete = await TeardownExtensionsAsync().ConfigureAwait(false);
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

            SetLifecycleState(
                IsWithdrawalComplete(failures.Count, incomplete)
                    ? LifecycleState.Stopped
                    : LifecycleState.StopIncomplete);

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

    /// <summary>
    /// Decides whether extension shutdown genuinely finished.
    /// </summary>
    /// <param name="infrastructureFailures">How many Host infrastructure faults shutdown accumulated.</param>
    /// <param name="incomplete">The packages this pass could not finish withdrawing.</param>
    /// <returns><see langword="true"/> only when the platform is holding nothing.</returns>
    /// <remarks>
    /// <para>
    /// An empty active set is not sufficient, and this is the one place that is easy to get wrong. A package
    /// is recorded stopped before its instances are disposed and its context unloaded, because the runtime
    /// record has to stop naming it before anything runs outside the gate. So a package whose disposal,
    /// unload, or contract release then failed is absent from the active set while its receipt, its
    /// identifier, and its dependency pins are all still held.
    /// </para>
    /// <para>
    /// The retained set is what closes that gap, and it stays true across repeated stops: a package held back
    /// on one pass is no longer active, so no later pass would see it in the active set either.
    /// </para>
    /// </remarks>
    private bool IsWithdrawalComplete(int infrastructureFailures, IReadOnlyList<PluginId> incomplete)
    {
        if (infrastructureFailures > 0 || incomplete.Count > 0 || _runtime.Active.Count > 0)
        {
            return false;
        }

        var retained = _dependencies.RetainedPackages;

        if (retained.Count > 0)
        {
            ExtensionWithdrawalsRetained(
                _log,
                string.Join(", ", retained.Select(package => package.ToString())));
            return false;
        }

        // The installation's shared contract context is the last thing holding extension types. Every
        // package has given up its hold by here, so the store either accepts the unload request or names
        // what is still holding it; reporting a clean stop while it is still serving would be the same
        // untruth as reporting an active extension as stopped.
        if (!_contracts.TryRequestUnload(out var refusal))
        {
            SharedContractsRetained(_log, refusal ?? "no reason was given");
            return false;
        }

        return _contracts.UnloadRequested;
    }

    private LifecycleState CurrentLifecycleState
        => (LifecycleState)Volatile.Read(ref _lifecycleState);

    private bool AdmissionMayPublish
        => CurrentLifecycleState is LifecycleState.Created or LifecycleState.Starting or LifecycleState.Started;

    private void SetLifecycleState(LifecycleState state)
        => Volatile.Write(ref _lifecycleState, (int)state);

    /// <summary>
    /// Withdraws every active extension, dependants before their dependencies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The order is the reverse of the order packages were actually published in, taken from the dependency
    /// registry rather than recomputed from declarations at shutdown, so a file edited while the host ran
    /// cannot reorder teardown. For an installation whose packages require nothing of each other this is
    /// exactly reverse identifier order, which is what it has always been.
    /// </para>
    /// <para>
    /// Order is not the safety property. Deferral is precisely the case where the order is not followed, so
    /// the precondition for releasing a package is checked rather than assumed: no run of its own still in
    /// flight, and no dependant still holding it. A deferred package keeps its edges, so everything it can
    /// reach stays rooted transitively; an authority mismatch is treated as not-withdrawn for the same
    /// reason, because a bookkeeping anomaly must not silently unpin a dependency.
    /// </para>
    /// <para>
    /// Each package is withdrawn in two phases around its disposal, because disposal runs extension code
    /// and unloads a context and neither may happen under the gate. Under the gate it is hidden, closed to
    /// new invocations and marked withdrawing while keeping every dependency it holds; outside the gate the
    /// invocations already running are waited for, then Host-activated objects, then extension objects, the
    /// cache namespace and the context are released; and only a release that reported no failure re-enters
    /// the gate to give up its dependencies. Releasing them at the first step would unpin a dependency
    /// while this package's own disposers were still executing against its types, and would leave it
    /// unpinned outright when that unload failed.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<PluginId>> TeardownExtensionsAsync()
    {
        var incomplete = new List<PluginId>();

        foreach (var active in WithdrawalOrder())
        {
            var plugin = active.Id
                ?? throw new InvalidOperationException("An active extension result must carry its identifier.");

            var packageLease = active.PackageLease;
            var receipt = packageLease?.Receipt;
            HostAdmissionAttempt? attempt = null;
            PackageAdmissionLease? lifetime = null;
            var deferred = false;
            IReadOnlyList<PluginId> pinnedBy = [];

            using (_publication.EnterWrite())
            {
                // Tick holds the matching read lease until an exact registration is represented in its
                // in-flight table. Checking under the writer makes start versus withdrawal a total order:
                // either this sees the run and preserves its code, or no later run can start from it.
                if (_scheduler.HasInFlight(plugin))
                {
                    deferred = true;
                }
                // The same question one level up, and for the same reason. A dependant that is still
                // rooted holds resolved type handles from this package; unloading it would not fail
                // loudly, it would mark a context for a collection that cannot happen and leave the
                // dependant holding types from a context the platform has declared gone.
                else if (receipt is not null && _dependencies.HasLiveDependants(receipt, out var pinning))
                {
                    deferred = true;
                    pinnedBy = pinning;
                }
                else if (!TryMatchCommittedAuthority(plugin, packageLease?.Runtime, out attempt))
                {
                    ExtensionTeardownAuthorityMismatch(_log, plugin.ToString());
                    incomplete.Add(plugin);
                    continue;
                }
                else
                {
                    // Phase one: nothing new binds to this package from here, and it goes on holding
                    // everything it depends on until its own code is gone. The runtime stop below closes it
                    // to invocation inside this same writer transition, so there is no instant at which a
                    // contribution is gone but a direct wrapper could still enter.
                    attempt?.Unpublish();

                    if (receipt is not null && !_dependencies.BeginWithdrawal(receipt, out var acquired))
                    {
                        throw new InvalidOperationException(
                            $"Extension '{plugin}' acquired dependants [{string.Join(", ", acquired)}] while "
                            + "holding the publication gate.");
                    }

                    if (!_runtime.TryStop(active, _clock.GetUtcNow(), out lifetime))
                    {
                        throw new InvalidOperationException(
                            $"Extension '{plugin}' changed runtime authority while holding the publication gate.");
                    }
                }
            }

            if (deferred)
            {
                if (pinnedBy.Count > 0)
                {
                    ExtensionTeardownPinned(
                        _log,
                        plugin.ToString(),
                        string.Join(", ", pinnedBy.Select(dependant => dependant.ToString())));
                }
                else
                {
                    ExtensionTeardownDeferred(_log, plugin.ToString());
                }

                incomplete.Add(plugin);
                continue;
            }

            // Phase two, outside the gate, and the order inside it is the safety property. The gate closed
            // this package to new invocations; this waits for the ones already running. Only then may the
            // providers and languages Host activated be disposed, because a callback still executing is
            // still using them.
            if (packageLease is not null)
            {
                await packageLease.DrainInvocationsAsync().ConfigureAwait(false);
            }

            // The package lifetime disposes what Host activated, in reverse activation order and before
            // the extension's own registered values, so there is one order and one owner for all of it.
            var released = true;

            // The package lease disposes the executable half and then gives up this package's hold on the
            // installation contract context - deliberately before anything is unpinned, because a package's
            // own contract assembly may reference contracts its dependencies published. A shared contract
            // other admitted packages still hold survives it; only this package's hold is released.
            if (lifetime is not null)
            {
                foreach (var failure in await lifetime.DisposeAsync(released).ConfigureAwait(false))
                {
                    released = false;
                    ExtensionCleanupFailed(_log, plugin.ToString(), failure);
                }
            }

            if (receipt is null)
            {
                if (!released)
                {
                    incomplete.Add(plugin);
                }

                continue;
            }

            if (!released)
            {
                // A disposer that threw, a context that would not unload, and a contract hold that could
                // not be given up all mean the same thing: something of this package may still be resident
                // and reachable. Its dependencies stay pinned rather than being released on the assumption
                // that the failure was harmless. Retrying would not learn anything new, so this state is
                // deliberately terminal for the process, and the loop continues with the next package.
                // Rooted, not merely reported: the platform has just said this package's objects may
                // still be resident, and the lifetime holding them is a local that would otherwise fall
                // out of scope the moment this loop moved on. It is the exact lifetime the runtime
                // registry handed over when it withdrew the published result — cleanup can only have
                // failed through it.
                _dependencies.RetainFailedAttempt(
                    receipt,
                    lifetime ?? throw new InvalidOperationException(
                        $"Extension '{plugin}' reported an incomplete release without a package lifetime to "
                        + "retain, which would leave its code unowned."));
                ExtensionWithdrawalIncomplete(_log, plugin.ToString());
                incomplete.Add(plugin);
                continue;
            }

            // Phase three: with this package's code and contract hold definitively released, it stops
            // holding what it depended on.
            _dependencies.CompleteWithdrawal(receipt);
        }

        return incomplete;
    }

    /// <summary>
    /// Orders the active extensions dependants-first, by reversing the order they were published in.
    /// </summary>
    private IReadOnlyList<PluginLoadResult> WithdrawalOrder()
        =>
        [
            .. _runtime.Active
                .Select(active => new
                {
                    Active = active,
                    Published = _dependencies.PublicationOrderOf(active.PackageLease?.Receipt),
                })
                .OrderByDescending(entry => entry.Published ?? long.MinValue)
                .ThenByDescending(
                    entry => entry.Active.Id?.ToString() ?? entry.Active.Source,
                    StringComparer.Ordinal)
                .Select(entry => entry.Active),
        ];

    /// <summary>
    /// Determines whether this runtime lease and Host's committed attempt name the same object.
    /// </summary>
    /// <param name="plugin">The extension being withdrawn.</param>
    /// <param name="lease">Its runtime lifetime receipt.</param>
    /// <param name="attempt">Host's committed attempt, when there is one.</param>
    /// <returns><see langword="false"/> when the two sides disagree and neither may be withdrawn alone.</returns>
    /// <remarks>
    /// A package that contributes no executable code has no attempt on either side, which agrees. What
    /// this refuses is a runtime lease and a committed attempt that name different objects, or one side
    /// holding an attempt the other does not.
    /// </remarks>
    private bool TryMatchCommittedAuthority(
        PluginId plugin,
        PluginRuntimeLease? lease,
        out HostAdmissionAttempt? attempt)
    {
        attempt = _committed.TryGetValue(plugin, out var committed) ? committed : null;
        return ReferenceEquals(lease?.AdmissionAttempt, attempt);
    }

    private async Task<bool> RecoverFailedStartupAsync()
    {
        var recovered = true;
        IReadOnlyList<PluginId> incomplete = [];

        try
        {
            incomplete = await TeardownExtensionsAsync().ConfigureAwait(false);
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

        // The same criterion shutdown uses, for the same reason: recovery that unwound everything it could
        // is not the same thing as a platform holding nothing.
        return IsWithdrawalComplete(recovered ? 0 : 1, incomplete);
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

            // Before anything is activated, and independently of whether this extension supplies a provider:
            // an item type owned by two kinds makes every lookup keyed on it order-dependent.
            if (!TryCheckItemTypeOwnership(mediaKinds, out defects))
            {
                return PluginAdmissionResult.Refused(CoreErrorCode.MediaItemTypeConflict, defects);
            }

            var inventory = new AdmittedInventory(mediaKinds.Select(Inventory).ToArray());
            // The ledger goes in with it: every object this scope constructs is owned from the instant it
            // exists, whatever happens to it afterwards.
            var activation = ledger.ActivationContext is { } context
                ? new PluginActivationScope(context, ledger)
                : null;

            if (!TryPrepareLanguages(manifest, ledger, activation, out languages, out defects))
            {
                return PluginAdmissionResult.Refused(CoreErrorCode.PluginLoadFailure, defects);
            }

            if (!TryPrepareJobs(manifest, inventory, ledger, out var jobs, out defects))
            {
                return PluginAdmissionResult.Refused(CoreErrorCode.JobSchedulingFailed, defects);
            }

            if (!TryPrepareProviders(manifest, mediaKinds, ledger, activation, out providers, out defects, out var providerCode))
            {
                return PluginAdmissionResult.Refused(providerCode, defects);
            }

            // Every candidate takes this extension's invocation lifetime, so a runtime call into any of
            // these objects can be waited for before teardown disposes it.
            var health = _pluginHealth.Prepare(
                manifest.Id,
                ledger.Registered<IHealthContributor>(),
                ledger.Invocation);
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
// implementation quarantines this extension. Nothing is disposed here: a refusal returns no attempt, so the
// objects activated so far are owned by this package's lifetime and released with it.
#pragma warning disable CA1031
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
#pragma warning restore CA1031
        {
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
            && !TryPrepareTypedKinds(manifest, typed, registered, ledger.Invocation, out errorCode, out defects))
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

            if (_kinds.TryPrepare(contribution, out var admittedKind, out var shapeDefects, ledger.Invocation))
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
    /// Proves no two typed media kinds are closed over the same item type.
    /// </summary>
    /// <param name="admitted">The kinds this attempt admitted, which are not published yet.</param>
    /// <param name="defects">One defect per collision, naming both kinds.</param>
    /// <returns><see langword="true"/> when every admitted item type has one owner.</returns>
    /// <remarks>
    /// <para>
    /// An item type is how the platform finds a kind: a paired cataloger or curator resolves to one this
    /// way, and so does external-identifier recognition. Two kinds closed over one type therefore make those
    /// answers a property of whichever kind a dictionary happened to see first, which is not an answer at
    /// all. The check runs whether or not the extension supplies a provider, because the ambiguity is in the
    /// kinds.
    /// </para>
    /// <para>
    /// The already-active kinds are walked first and in registry order, so an incumbent is always named as
    /// the owner and the diagnostic does not depend on the order the attempt's own kinds arrive in. A kind
    /// colliding with itself is not a collision: an extension re-preparing a kind it already supplies is the
    /// duplicate-kind check's business, and reporting it here would rename an existing failure.
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "One private step returning a verdict and the complete defect list.")]
    private bool TryCheckItemTypeOwnership(
        IReadOnlyList<RegisteredMediaKind> admitted,
        out IReadOnlyList<string> defects)
    {
        var found = new List<string>();
        var owners = new Dictionary<Type, MediaKindId>();

        foreach (var kind in _kinds.All.Concat(admitted))
        {
            if (kind.MediaType is not { } runtime)
            {
                continue;
            }

            if (!owners.TryGetValue(runtime.ItemType, out var owner))
            {
                owners[runtime.ItemType] = kind.Kind;
                continue;
            }

            if (owner == kind.Kind)
            {
                continue;
            }

            found.Add(
                $"kind[{kind.Kind}]: its item type '{runtime.ItemType.AssemblyQualifiedName}' is already "
                + $"supplied by media kind '{owner}'. A media item type identifies exactly one kind, because "
                + "paired providers and identifier recognition resolve a kind from it; give this kind its "
                + "own item type.");
        }

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
        IInvocationLifetime? invocation,
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

            if (_mediaTypes.TryPrepare(contribution, out var admittedKind, out var kindDefects, invocation))
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

                if (!_languages.TryPrepare(manifest.Id, language, out var candidate, out var error, ledger.Invocation))
                {
                    found.Add($"language[{registration.ImplementationType.Name}]: {error}");
                    continue;
                }

                if (languages.Any(entry => string.Equals(
                        entry.Code,
                        candidate!.Code,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add($"language[{candidate!.Code}]: the extension registers this language more than once.");
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
            }
        }

        if (found.Count > 0)
        {
            languages.Clear();
        }

        prepared = languages;
        defects = found;
        return found.Count == 0;
    }

    /// <remarks>
    /// <para>
    /// The media pairing is checked across every registration before any implementation is constructed. A
    /// provider whose closed item type no installed media kind supplies has nothing to be right about, and
    /// running its constructor to find that out would run extension code the installation has already
    /// decided it cannot use.
    /// </para>
    /// <para>
    /// The item types compared are the ones actually supplied: the kinds this attempt just admitted, plus
    /// the kinds already active in the installation. A declared package dependency is a separate and earlier
    /// check; it proves a package is present, not that a media kind closed over that exact CLR type is.
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "One private step returning a verdict, what it prepared, a machine-readable code and the complete defect list.")]
    private bool TryPrepareProviders(
        ValidatedManifest manifest,
        IReadOnlyList<RegisteredMediaKind> mediaKinds,
        PluginRegistrationLedger ledger,
        PluginActivationScope? activation,
        out IReadOnlyList<RegisteredProvider> prepared,
        out IReadOnlyList<string> defects,
        out CoreErrorCode errorCode)
    {
        var found = new List<string>();
        var providers = new List<RegisteredProvider>();
        var registrations = ledger.Registered<ProviderTypeRegistration>();

        errorCode = CoreErrorCode.PluginLoadFailure;

        if (!TryCheckProviderContracts(mediaKinds, registrations, out var contractDefects, out var contractCode))
        {
            prepared = [];
            defects = contractDefects;
            errorCode = contractCode;
            return false;
        }

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
                    continue;
                }

                if (!_providers.TryPrepare(
                    manifest.Id,
                    registration.Family,
                    registration.Descriptor,
                    provider,
                    registration.MediaItemType,
                    out var candidate,
                    out var error,
                    ledger.Invocation))
                {
                    found.Add($"provider[{registration.Descriptor.LocalId}]: {error}");
                    continue;
                }

                // Registration captures the cataloger's declaration once. Validate and route by that
                // captured value; a mutable implementation getter cannot change the contract after
                // admission.
                if (candidate.Provider is ICataloger
                    && !CatalogIdentity.IsCanonicalScheme(candidate.CatalogScheme))
                {
                    found.Add(
                        $"provider[{registration.Descriptor.LocalId}]: a cataloger declares the external "
                        + "identifier scheme it is the authority for, lower-case and without white space; "
                        + $"'{candidate.CatalogScheme}' is not one.");
                    continue;
                }

                if (providers.Any(entry => entry.Id == candidate.Id))
                {
                    found.Add($"provider[{registration.Descriptor.LocalId}]: the extension registers this provider more than once.");
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
            }
        }

        if (found.Count > 0)
        {
            providers.Clear();
        }

        prepared = providers;
        defects = found;
        return found.Count == 0;
    }

    /// <summary>The closed contract each media-paired provider family is registered under.</summary>
    /// <remarks>
    /// A family absent from this table has no media pairing at all, which is a fact worth checking in both
    /// directions: an indexer that arrived carrying an item type has had a pairing invented for it
    /// somewhere.
    /// </remarks>
    private static readonly IReadOnlyDictionary<ProviderFamily, Type> PairedContracts =
        new Dictionary<ProviderFamily, Type>
        {
            [ProviderFamily.Cataloger] = typeof(ICataloger<>),
            [ProviderFamily.Curator] = typeof(ICurator<>),
        };

    /// <summary>
    /// Proves every provider registration's contract, item type and implementation agree, and that an
    /// installed media kind supplies the item type it pairs with.
    /// </summary>
    /// <param name="mediaKinds">The kinds this attempt admitted, which are not published yet.</param>
    /// <param name="registrations">Every provider the extension registered.</param>
    /// <param name="defects">One actionable defect per unsound registration.</param>
    /// <param name="errorCode">
    /// The failure class, meaningful only when this returns false:
    /// <see cref="CoreErrorCode.PluginProviderContractInvalid"/> when a registration does not describe one
    /// coherent relationship, and <see cref="CoreErrorCode.PluginMediaPairingUnsatisfied"/> when it does but
    /// no admitted kind supplies its item type. They are different operator problems and stay different
    /// diagnoses.
    /// </param>
    /// <returns><see langword="true"/> when every registration is sound and pairs with an installed kind.</returns>
    /// <remarks>
    /// <para>
    /// Both halves run before anything is constructed, and the structural half runs first because a
    /// registration that does not describe its own implementation cannot be asked a meaningful question
    /// about media kinds. The registration is built by <see cref="ProviderTypeRegistration"/> from the
    /// implementation's own interface list, so a sound one is what Host expects; this repeats the check at
    /// the boundary that constructs, because that is where being wrong costs an extension constructor call.
    /// </para>
    /// <para>
    /// The item types compared are the ones actually supplied: the kinds this attempt just admitted, plus
    /// the kinds already active in the installation. A declared package dependency is a separate and earlier
    /// check; it proves a package is present, not that a media kind closed over that exact CLR type is.
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "One private step returning a verdict, the complete defect list and a machine-readable code.")]
    private bool TryCheckProviderContracts(
        IReadOnlyList<RegisteredMediaKind> mediaKinds,
        IReadOnlyList<ProviderTypeRegistration> registrations,
        out IReadOnlyList<string> defects,
        out CoreErrorCode errorCode)
    {
        errorCode = CoreErrorCode.PluginProviderContractInvalid;

        var structural = registrations
            .Select(registration => (Registration: registration, Defect: ContractDefectOf(registration)))
            .Where(entry => entry.Defect is not null)
            .Select(entry => $"provider[{entry.Registration.Descriptor.LocalId}]: {entry.Defect}")
            .ToList();

        if (structural.Count > 0)
        {
            defects = structural;
            return false;
        }

        // Only a typed kind publishes an item type, so a legacy shape-declared kind pairs with nothing and
        // says so rather than appearing to match everything. Item types are unique across admitted kinds by
        // the invariant TryCheckItemTypeOwnership enforces, so this map cannot depend on iteration order.
        var supplied = new Dictionary<Type, MediaKindId>();

        foreach (var kind in mediaKinds.Concat(_kinds.All))
        {
            if (kind.MediaType is { } runtime)
            {
                supplied.TryAdd(runtime.ItemType, kind.Kind);
            }
        }

        var found = new List<string>();

        foreach (var registration in registrations)
        {
            if (registration.MediaItemType is not { } item || supplied.ContainsKey(item))
            {
                continue;
            }

            var installed = supplied.Count == 0
                ? "no typed media kind is installed"
                : $"the installed typed kinds supply {string.Join(", ", supplied.Select(entry => $"'{entry.Value}' ({entry.Key.FullName})").Order(StringComparer.Ordinal))}";

            found.Add(
                $"provider[{registration.Descriptor.LocalId}]: '{registration.ImplementationType.Name}' closed "
                + $"its {registration.Family} contract over '{item.AssemblyQualifiedName}', which no active "
                + $"media kind supplies — {installed}. Install the media package that declares that item "
                + "type, or pair the provider with a kind this installation has.");
        }

        defects = found;
        errorCode = CoreErrorCode.PluginMediaPairingUnsatisfied;
        return found.Count == 0;
    }

    /// <summary>
    /// Says what is wrong with one registration's contract relationship, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The whole relationship, not one link of it. A contract that is assignable from the implementation
    /// still proves nothing on its own: the pairing is only sound when the family expects a closed contract,
    /// the recorded contract is exactly one construction of it, its type argument is exactly the recorded
    /// item type, and the implementation implements that one contract and no sibling of it.
    /// </remarks>
    private static string? ContractDefectOf(ProviderTypeRegistration registration)
    {
        var implementation = registration.ImplementationType;

        if (!PairedContracts.TryGetValue(registration.Family, out var openContract))
        {
            return registration.MediaItemType is null
                ? null
                : $"a {registration.Family} has no media pairing, but this registration carries the item "
                    + $"type '{registration.MediaItemType.FullName}'.";
        }

        if (registration.MediaItemType is not { } item)
        {
            return $"a {registration.Family} pairs with a media item type, and this registration carries none.";
        }

        var contract = registration.ContractType;

        if (!contract.IsInterface
            || !contract.IsGenericType
            || contract.IsGenericTypeDefinition
            || contract.GetGenericTypeDefinition() != openContract)
        {
            return $"the recorded contract '{contract.FullName}' is not a closed '{openContract.Name}'.";
        }

        if (contract.GetGenericArguments() is not [var argument] || argument != item)
        {
            return $"the recorded contract '{contract.FullName}' is not closed over the recorded item type "
                + $"'{item.FullName}'.";
        }

        var implemented = implementation
            .GetInterfaces()
            .Where(candidate => candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == openContract)
            .OrderBy(candidate => candidate.AssemblyQualifiedName, StringComparer.Ordinal)
            .ToArray();

        if (implemented.Length == 0)
        {
            return $"'{implementation.FullName}' implements no '{openContract.Name}', so the recorded "
                + $"contract '{contract.FullName}' is a claim rather than a relationship.";
        }

        if (implemented.Length > 1)
        {
            return $"'{implementation.FullName}' implements {implemented.Length} {registration.Family} "
                + $"contracts ({string.Join(", ", implemented.Select(candidate => candidate.FullName))}), so "
                + "the pairing is ambiguous.";
        }

        return implemented[0] == contract
            ? null
            : $"'{implementation.FullName}' implements '{implemented[0].FullName}', not the recorded "
                + $"contract '{contract.FullName}'.";
    }

    private static TContract? Pick<TContract>(PluginRegistrationLedger ledger)
        where TContract : class
        {
        var registered = ledger.Registered<TContract>();
        return registered.Count == 0 ? null : registered[0];
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

        /// <inheritdoc />
        /// <remarks>
        /// Publication only. A rollback runs while the caller holds the publication write gate, and running
        /// an extension's disposer there would hold a thread-affine gate across third-party code; the
        /// package lifetime disposes what this attempt activated, outside it.
        /// </remarks>
        public void Rollback() => Unpublish();

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

        /// <remarks>
        /// Read inside the lease that would publish, so a package cannot slip between the check and the
        /// write. A package with no executable half never reaches <c>Prepare</c>, and this is the one gate
        /// it does pass.
        /// </remarks>
        bool IPluginAdmissionCheck.MayPublish(PluginId package, out string? refusal)
        {
            if (owner.AdmissionMayPublish)
            {
                refusal = null;
                return true;
            }

            refusal = $"Extension '{package}' cannot become active after Host extension shutdown has begun.";
            return false;
        }
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
            result.Manifest?.Name,
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
            result.Manifest?.Name,
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
        EventId = 9210,
        Level = LogLevel.Error,
        Message = "Extension shutdown finished with the installation's shared contract context still "
            + "serving: {Refusal}")]
    private static partial void SharedContractsRetained(ILogger logger, string refusal);

    [LoggerMessage(
        EventId = 9209,
        Level = LogLevel.Error,
        Message = "Extension shutdown finished with [{Packages}] still held, so their code may remain "
            + "resident and every extension they depend on remains rooted.")]
    private static partial void ExtensionWithdrawalsRetained(ILogger logger, string packages);

    [LoggerMessage(
        EventId = 9208,
        Level = LogLevel.Error,
        Message = "Extension '{Extension}' was hidden and stopped, but its instances or load context could "
            + "not be released, so it goes on holding every extension it depends on.")]
    private static partial void ExtensionWithdrawalIncomplete(ILogger logger, string extension);

    [LoggerMessage(
        EventId = 9207,
        Level = LogLevel.Error,
        Message = "Extension '{Extension}' is still required by [{Dependants}]; its registrations, "
            + "instances, and load context remain live until every dependant has been withdrawn.")]
    private static partial void ExtensionTeardownPinned(
        ILogger logger,
        string extension,
        string dependants);

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
