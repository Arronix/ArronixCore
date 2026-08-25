using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Plugins;
using Arronix.Common.Diagnostics;
using Arronix.Host.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arronix.Host.Scheduling;

/// <summary>
/// The one scheduler.
/// </summary>
/// <remarks>
/// <para>
/// A pass over the queue does three things: it queues whatever has come due, it starts as much ready work as
/// every applicable ceiling allows, and it puts failed work back with a delay. Everything time-dependent
/// reads the injected clock, so a test can advance a year in a millisecond and assert the exact rung of the
/// back-off ladder a fourth attempt landed on.
/// </para>
/// <para>
/// Ready work is walked rather than taken from the head. An entry that cannot start — a saturated throttle, a
/// job already running as many copies as it declared — is stepped over rather than waited on, so one busy
/// activity never stalls an unrelated one. That is the whole reason the governor offers a non-blocking
/// acquisition.
/// </para>
/// <para>
/// Startup jobs are held until extensions have been admitted. A refresh job that ran before its own
/// extension was registered would fail on its first pass and then wait a full back-off rung to try again,
/// which is a self-inflicted wound the surveyed applications each carry.
/// </para>
/// </remarks>
public sealed partial class JobScheduler : BackgroundService
{
    /// <summary>
    /// The longest cooperative-shutdown deadline the host will honor, whatever a job declares.
    /// </summary>
    /// <remarks>
    /// A job's <see cref="IScheduledJob.ShutdownDeadline"/> is a number supplied by an extension, so it is
    /// clamped for the same reason its concurrency is: an extension must not be able to hold the whole
    /// process open by declaring a large one. Past this point the host stops waiting and says so.
    /// </remarks>
    internal static readonly TimeSpan MaxShutdownDeadline = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<Guid, InFlightRun> _running = new();
    private readonly BackgroundTaskRegistry _registry;
    private readonly JobQueue _queue;
    private readonly ConcurrencyGovernor _governor;
    private readonly FailureClassifier _classifier;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobScheduler> _log;
    private readonly SchedulerOptions _options;
    private readonly Func<double> _jitter;
    private volatile bool _startupReleased;

    /// <summary>
    /// Creates a scheduler.
    /// </summary>
    /// <param name="registry">The registered jobs.</param>
    /// <param name="queue">The queue.</param>
    /// <param name="governor">The concurrency ceilings.</param>
    /// <param name="classifier">How a failure is classified.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The deployment's scheduler settings.</param>
    /// <param name="log">Where the scheduler reports what it did.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public JobScheduler(
        BackgroundTaskRegistry registry,
        JobQueue queue,
        ConcurrencyGovernor governor,
        FailureClassifier classifier,
        TimeProvider clock,
        IOptions<SchedulerOptions> options,
        ILogger<JobScheduler> log)
        : this(registry, queue, governor, classifier, clock, options, log, DefaultJitter)
    {
    }

    /// <summary>
    /// Creates a scheduler with a supplied jitter source.
    /// </summary>
    /// <param name="registry">The registered jobs.</param>
    /// <param name="queue">The queue.</param>
    /// <param name="governor">The concurrency ceilings.</param>
    /// <param name="classifier">How a failure is classified.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The deployment's scheduler settings.</param>
    /// <param name="log">Where the scheduler reports what it did.</param>
    /// <param name="jitter">
    /// A source of samples in the half-open range zero to one. Supplied rather than fixed so that a test can
    /// assert the exact delay a given attempt produced.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public JobScheduler(
        BackgroundTaskRegistry registry,
        JobQueue queue,
        ConcurrencyGovernor governor,
        FailureClassifier classifier,
        TimeProvider clock,
        IOptions<SchedulerOptions> options,
        ILogger<JobScheduler> log,
        Func<double> jitter)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(governor);
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(jitter);

        _registry = registry;
        _queue = queue;
        _governor = governor;
        _classifier = classifier;
        _clock = clock;
        _log = log;
        _options = options.Value;
        _jitter = jitter;
    }

    /// <summary>Determines whether this scheduler consumes the exact task registry supplied by its caller.</summary>
    internal bool UsesRegistry(BackgroundTaskRegistry registry) => ReferenceEquals(_registry, registry);

    /// <summary>
    /// Gets how many runs are in flight across every job.
    /// </summary>
    internal int InFlight => Volatile.Read(ref _inFlight);

    private int _inFlight;

    /// <summary>
    /// Lets startup-scheduled jobs become due.
    /// </summary>
    /// <remarks>
    /// Called once, by the extension bootstrapper, after every extension has been admitted or quarantined.
    /// </remarks>
    internal void ReleaseStartupJobs() => _startupReleased = true;

    /// <summary>
    /// Runs one pass: queue what is due, start what can start.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many runs were started.</returns>
    /// <remarks>
    /// A pass is the unit the hosted loop drives and the friend test assembly can exercise directly. It is
    /// not a public scheduling command; callers trigger declared jobs through <see cref="IBackgroundTaskRegistry"/>.
    /// </remarks>
    internal async Task<int> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();

        QueueDueJobs(now);

        var started = 0;

        foreach (var entry in _queue.Ready(now))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var registered = entry.Registration;
            IAsyncDisposable? lease = null;
            InFlightRun? run = null;

            // Publication withdrawal takes the matching write lease. Keep this read lease until the exact
            // registration has reserved capacity, its queue entry is removed and its in-flight receipt is
            // visible; stop therefore observes either no start at all or a run it must drain/defer.
            using (_registry.PublicationGate.EnterRead())
            {
                if (registered is null || !_registry.IsCurrent(registered))
                {
                    // The owning extension was quarantined, stopped or replaced between queuing and now.
                    // Dropping the stale envelope is the only terminating and identity-safe outcome.
                    _queue.Remove(entry.EntryId);
                }
                else if (registered.TryMarkStarted())
                {
                    if (_governor.TryAcquire(entry.ThrottleKeys, out lease)
                        && lease is not null
                        && _queue.Remove(entry.EntryId))
                    {
                        run = new InFlightRun(
                            entry.JobId,
                            registered.Owner,
                            registered.ShutdownDeadline);
                        _running[entry.EntryId] = run;
                        Interlocked.Increment(ref _inFlight);
                    }
                    else
                    {
                        registered.ReleaseStart();
                    }
                }
            }

            if (run is null)
            {
                if (lease is not null)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }

                continue;
            }

            started++;
            _ = RunAsync(registered!, entry, lease!, run, cancellationToken);
        }

        return started;
    }

    /// <summary>
    /// Waits for work already in flight to observe cancellation and stop.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token, for abandoning the wait early.</param>
    /// <returns>The identifiers of the jobs that were still running when their deadline passed.</returns>
    /// <remarks>
    /// <para>
    /// Each run is waited for its own job's declared deadline, and the waits run together rather than in
    /// sequence, so the whole drain costs the longest deadline and not their sum.
    /// </para>
    /// <para>
    /// A job that overruns is not stopped, because nothing can stop it: the returned identifiers are the
    /// honest report that the process still contains running extension code, and they are what an unload
    /// or a restart decision has to be made against.
    /// </para>
    /// </remarks>
    internal async Task<IReadOnlyList<string>> DrainAsync(CancellationToken cancellationToken = default)
    {
        var pending = _running.Values.ToList();

        if (pending.Count == 0)
        {
            return [];
        }

        var overran = await Task.WhenAll(pending.Select(run => WaitForAsync(run, cancellationToken)))
            .ConfigureAwait(false);

        var names = overran.Where(static jobId => jobId is not null).Select(static jobId => jobId!).ToList();

        foreach (var jobId in names)
        {
            JobOverranShutdownDeadline(_log, jobId);
        }

        return names;
    }

    /// <summary>Determines whether an extension still has code executing after the scheduler drain.</summary>
    internal bool HasInFlight(PluginId owner)
        => _running.Values.Any(run => run.Owner == owner);

    private async Task<string?> WaitForAsync(InFlightRun run, CancellationToken cancellationToken)
    {
        try
        {
            await run.Completion.Task
                .WaitAsync(run.ShutdownDeadline, _clock, cancellationToken)
                .ConfigureAwait(false);

            return null;
        }
        catch (TimeoutException)
        {
            return run.JobId;
        }
    }

    /// <summary>Constrains an extension-declared shutdown promise to the Host ceiling.</summary>
    internal static TimeSpan ClampShutdownDeadline(TimeSpan declared)
        => declared <= TimeSpan.Zero ? TimeSpan.Zero
            : declared > MaxShutdownDeadline ? MaxShutdownDeadline
            : declared;

    /// <summary>
    /// One run the scheduler started and has not yet seen finish.
    /// </summary>
    private sealed class InFlightRun(
        string jobId,
        PluginId owner,
        TimeSpan shutdownDeadline)
    {
        public string JobId { get; } = jobId;

        public PluginId Owner { get; } = owner;

        public TimeSpan ShutdownDeadline { get; } = shutdownDeadline;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobLoopStarted(_log, _options.MaxConcurrentJobs);

        using var timer = new PeriodicTimer(_options.PollInterval, _clock);

        try
        {
            do
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Shutdown. The loop stops starting work; what is already in flight observes the same token,
            // and each run gets the deadline its job declared to act on it before the host stops waiting.
        }

        await DrainAsync(CancellationToken.None).ConfigureAwait(false);

        JobLoopStopped(_log);
    }

    private void QueueDueJobs(DateTimeOffset now)
    {
        foreach (var registered in _registry.Registrations())
        {
            if (registered.Schedule.Kind == JobScheduleKind.Manual)
            {
                continue;
            }

            if (registered.Schedule.Kind == JobScheduleKind.Startup && !_startupReleased)
            {
                continue;
            }

            var due = registered.Schedule.NextRun(now, registered.LastRun);

            if (due is null || due > now)
            {
                continue;
            }

            _registry.TryEnqueueCurrent(
                registered,
                parameters: null,
                now,
                correlationId: null,
                items: null,
                out _);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A job is third-party code and anything it throws is a classification input, never a reason to stop the scheduler; the classifier decides what the exception means.")]
    private async Task RunAsync(
        RegisteredJob registered,
        JobQueueEntry entry,
        IAsyncDisposable lease,
        InFlightRun run,
        CancellationToken cancellationToken)
    {
        var succeeded = false;
        JobExecutionResult? result = null;
        Exception? failure = null;

        // The in-flight count falls only once the outcome has been dealt with, so that "nothing is in
        // flight" means "and nothing more is about to be queued". Releasing the capacity lease is a separate
        // and earlier step: capacity should be free the instant the work stops using it.
        try
        {
            try
            {
                using (CorrelationContext.Push(entry.CorrelationId))
                {
                    var startedAt = _clock.GetUtcNow();
                    var context = new JobExecutionContext(
                        entry.JobId,
                        entry.EnqueuedAt.UtcDateTime,
                        startedAt.UtcDateTime,
                        entry.CorrelationId,
                        entry.Parameters,
                        Progress: null,
                        MediaKind: entry.MediaKind,
                        Items: entry.Items);

                    result = await registered.Job.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
                    succeeded = result.Success;
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                registered.MarkFinished(succeeded);
                await lease.DisposeAsync().ConfigureAwait(false);
            }

            if (succeeded)
            {
                JobSucceeded(_log, entry.JobId, entry.CorrelationId, entry.Attempt);
                return;
            }

            Reschedule(registered, entry, result, failure);
        }
        finally
        {
            _running.TryRemove(entry.EntryId, out _);
            run.Completion.TrySetResult();
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private void Reschedule(
        RegisteredJob registered,
        JobQueueEntry entry,
        JobExecutionResult? result,
        Exception? failure)
    {
        var outcome = _classifier.Classify(result, failure);

        JobFailed(_log, entry.JobId, entry.CorrelationId, entry.Attempt, outcome.Class, failure);

        if (outcome.Class is FailureClass.Permanent or FailureClass.Configuration or FailureClass.Canceled)
        {
            return;
        }

        if (entry.Attempt >= _options.MaxAttempts)
        {
            JobAbandoned(_log, entry.JobId, entry.CorrelationId, entry.Attempt);
            return;
        }

        // What the remote itself asked for beats the ladder, because the ladder is a guess and a
        // retry-after header is not. Jitter is applied only to the guess.
        var delay = outcome.RetryAfter ?? BackoffLadder.Delay(entry.Attempt, _jitter());
        var now = _clock.GetUtcNow();

        _queue.Enqueue(entry with
        {
            Attempt = entry.Attempt + 1,
            NotBefore = now + delay,
        });

        // The next run is measured from the retry, so an interval schedule does not also fire while the
        // retry is still waiting.
        registered.MarkQueued(now);
    }

    // Full jitter over the ladder rung. Not a security decision: the value picks a moment inside a retry
    // window, and predicting it grants nothing, which is why the general-purpose generator is right here.
    [SuppressMessage(
        "Security",
        "CA5394:Do not use insecure randomness",
        Justification = "The sample chooses a moment within a retry window to decorrelate concurrent retries; it protects nothing and predicting it grants nothing.")]
    private static double DefaultJitter() => Random.Shared.NextDouble();

    [LoggerMessage(
        EventId = 9000,
        Level = LogLevel.Information,
        Message = "Job scheduler started with a global ceiling of {MaxConcurrentJobs} concurrent runs.")]
    private static partial void JobLoopStarted(ILogger logger, int maxConcurrentJobs);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Information, Message = "Job scheduler stopped.")]
    private static partial void JobLoopStopped(ILogger logger);

    [LoggerMessage(
        EventId = 9002,
        Level = LogLevel.Debug,
        Message = "Job {JobId} succeeded on attempt {Attempt} (correlation {CorrelationId}).")]
    private static partial void JobSucceeded(ILogger logger, string jobId, string correlationId, int attempt);

    [LoggerMessage(
        EventId = 9003,
        Level = LogLevel.Warning,
        Message = "Job {JobId} failed on attempt {Attempt} and was classified {FailureClass} (correlation {CorrelationId}).")]
    private static partial void JobFailed(
        ILogger logger,
        string jobId,
        string correlationId,
        int attempt,
        FailureClass failureClass,
        Exception? exception);

    [LoggerMessage(
        EventId = 9004,
        Level = LogLevel.Error,
        Message = "Job {JobId} was abandoned after {Attempt} attempts (correlation {CorrelationId}).")]
    private static partial void JobAbandoned(ILogger logger, string jobId, string correlationId, int attempt);

    [LoggerMessage(
        EventId = 9005,
        Level = LogLevel.Warning,
        Message = "Job {JobId} was still running when its shutdown deadline passed. The host has stopped "
            + "waiting for it, but its code is still in the process.")]
    private static partial void JobOverranShutdownDeadline(ILogger logger, string jobId);
}
