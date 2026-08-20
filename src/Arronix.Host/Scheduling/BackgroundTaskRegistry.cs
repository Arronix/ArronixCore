using System.Collections.Concurrent;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;


namespace Arronix.Host.Scheduling;

/// <summary>
/// Every background job the platform knows about, and how each of them is scheduled.
/// </summary>
/// <remarks>
/// <para>
/// This implements the stable registration contract rather than superseding it. One scheduler, one queue:
/// a second command model alongside this one would mean two answers to "what is running?" and two places a
/// concurrency ceiling could be applied, and the surveyed applications each show what happens when that goes
/// unresolved.
/// </para>
/// <para>
/// The contract's registration method takes a schedule string and no owner, so the host-side overload is the
/// one the loader calls: the owning extension is what the throttle keys are synthesized from, and work that
/// arrived with no owner could not be attributed or limited.
/// </para>
/// </remarks>
public sealed class BackgroundTaskRegistry : IBackgroundTaskRegistry
{
    /// <summary>
    /// The owner recorded for jobs the platform itself registers.
    /// </summary>
    public static readonly PluginId HostOwner = PluginId.FromString("arronix.host");

    private readonly ConcurrentDictionary<string, RegisteredJob> _jobs = new(StringComparer.Ordinal);
    private readonly JobQueue _queue;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Creates a registry.
    /// </summary>
    /// <param name="queue">The queue triggered work is placed on.</param>
    /// <param name="clock">The clock schedules are measured against.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public BackgroundTaskRegistry(JobQueue queue, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(clock);

        _queue = queue;
        _clock = clock;
    }

    /// <inheritdoc />
    /// <exception cref="ArronixException">
    /// The schedule text is not one of the four accepted forms. Carries
    /// <see cref="CoreErrorCode.JobSchedulingFailed"/>.
    /// </exception>
    public void RegisterJob(IScheduledJob job, string schedule)
        => RegisterJob(HostOwner, CapabilitySet.None, job, schedule, null);

    /// <summary>
    /// Registers a job on behalf of an extension.
    /// </summary>
    /// <param name="owner">The extension.</param>
    /// <param name="capabilities">Its granted capabilities, which the throttle keys are synthesized from.</param>
    /// <param name="job">The job.</param>
    /// <param name="schedule">When it runs.</param>
    /// <param name="mediaKind">The media kind its work concerns, when it concerns one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="job"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArronixException">
    /// The schedule text is not one of the four accepted forms, or a job with the same identifier is already
    /// registered.
    /// </exception>
    public void RegisterJob(
        PluginId owner,
        CapabilitySet capabilities,
        IScheduledJob job,
        string schedule,
        MediaKindId? mediaKind)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!JobScheduleParser.TryParse(schedule, out var parsed, out var error))
        {
            throw new ArronixException(
                CoreErrorCode.JobSchedulingFailed,
                $"Job '{job.JobId}' cannot be scheduled: {error}");
        }

        var registered = new RegisteredJob(
            job,
            parsed,
            owner,
            capabilities.WithImplied(),
            mediaKind,
            ConcurrencyGovernor.KeysFor(owner, capabilities.WithImplied(), mediaKind?.Value));

        if (!_jobs.TryAdd(job.JobId, registered))
        {
            throw new ArronixException(
                CoreErrorCode.JobSchedulingFailed,
                $"A job with identifier '{job.JobId}' is already registered.");
        }
    }

    /// <inheritdoc />
    public void UnregisterJob(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        if (_jobs.TryRemove(jobId, out _))
        {
            _queue.DropJobs([jobId]);
        }
    }

    /// <inheritdoc />
    public IScheduledJob? GetJob(string jobId)
        => jobId is not null && _jobs.TryGetValue(jobId, out var registered) ? registered.Job : null;

    /// <inheritdoc />
    public IReadOnlyList<IScheduledJob> GetAllJobs()
        => [.. _jobs.Values.OrderBy(job => job.Job.JobId, StringComparer.Ordinal).Select(job => job.Job)];

    /// <inheritdoc />
    /// <remarks>
    /// Triggering queues the work rather than running it, so the concurrency ceilings that apply to
    /// scheduled work apply identically to work a user asked for. An operator pressing a button twice does
    /// not get two concurrent runs of a job that declared it allows one.
    /// </remarks>
    public Task<bool> TriggerJobAsync(
        string jobId,
        IReadOnlyDictionary<string, object>? parameters = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (jobId is null || !_jobs.TryGetValue(jobId, out var registered))
        {
            return Task.FromResult(false);
        }

        Enqueue(registered, parameters, _clock.GetUtcNow(), correlationId);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Removes every job an extension registered.
    /// </summary>
    /// <param name="owner">The extension.</param>
    /// <returns>How many jobs were removed.</returns>
    public int RemoveByPlugin(PluginId owner)
    {
        var owned = _jobs.Values
            .Where(job => job.Owner == owner)
            .Select(job => job.Job.JobId)
            .ToList();

        foreach (var jobId in owned)
        {
            _jobs.TryRemove(jobId, out _);
        }

        _queue.DropJobs(owned);
        return owned.Count;
    }

    /// <summary>
    /// Gets every registration, for the scheduler and for reporting.
    /// </summary>
    /// <returns>The registrations, ordered by job identifier.</returns>
    public IReadOnlyList<RegisteredJob> Registrations()
        => [.. _jobs.Values.OrderBy(job => job.Job.JobId, StringComparer.Ordinal)];

    /// <summary>
    /// Looks up one registration.
    /// </summary>
    /// <param name="jobId">The job's identifier.</param>
    /// <returns>The registration, or <see langword="null"/> when there is none.</returns>
    public RegisteredJob? Find(string jobId)
        => jobId is not null && _jobs.TryGetValue(jobId, out var registered) ? registered : null;

    /// <summary>
    /// Describes every registration for a consumer.
    /// </summary>
    /// <returns>One view per job, ordered by identifier.</returns>
    public IReadOnlyList<JobView> Describe()
    {
        var now = _clock.GetUtcNow();

        return
        [
            .. Registrations().Select(registered => new JobView(
                registered.Job.JobId,
                registered.Job.Name,
                registered.Job.Description,
                registered.Owner.ToString(),
                registered.Schedule.Raw,
                registered.LastRun,
                registered.Schedule.NextRun(now, registered.LastRun),
                registered.LastSucceeded,
                registered.Job.Priority)),
        ];
    }

    /// <summary>
    /// Places a registration's work on the queue.
    /// </summary>
    /// <param name="registered">The registration.</param>
    /// <param name="parameters">Parameters specific to this job.</param>
    /// <param name="now">The current time.</param>
    /// <param name="correlationId">The wider operation this belongs to; one is minted when absent.</param>
    /// <param name="items">The items the run concerns, when it is scoped to particular ones.</param>
    /// <returns>The entry that was queued.</returns>
    /// <remarks>
    /// The correlation identifier, the media kind and the item list are carried by the entry and reach the
    /// job as typed members of its context. They are deliberately not merged into
    /// <paramref name="parameters"/>: that bag is the caller's, and a platform fact hidden in it under a
    /// published string key is a fact nothing can check.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="registered"/> is <see langword="null"/>.</exception>
    public JobQueueEntry Enqueue(
        RegisteredJob registered,
        IReadOnlyDictionary<string, object>? parameters,
        DateTimeOffset now,
        string? correlationId,
        IReadOnlyList<MediaItemRef>? items = null)
    {
        ArgumentNullException.ThrowIfNull(registered);

        var entry = new JobQueueEntry
        {
            EntryId = Guid.NewGuid(),
            JobId = registered.Job.JobId,
            Owner = registered.Owner,
            CorrelationId = correlationId ?? CorrelationContext.New(),
            Parameters = parameters is null
                ? new Dictionary<string, object>(StringComparer.Ordinal)
                : new Dictionary<string, object>(parameters, StringComparer.Ordinal),
            ThrottleKeys = registered.ThrottleKeys,
            EnqueuedAt = now,
            NotBefore = now,
            Attempt = 1,
            Priority = registered.Job.Priority,
            MediaKind = registered.MediaKind,
            Items = items ?? [],
        };

        registered.MarkQueued(now);
        _queue.Enqueue(entry);
        return entry;
    }
}
