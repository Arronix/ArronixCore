using System.Collections.Concurrent;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Plugins.Registry;


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
    private readonly PluginPublicationGate _publication;

    /// <summary>
    /// Creates a registry.
    /// </summary>
    /// <param name="queue">The queue triggered work is placed on.</param>
    /// <param name="clock">The clock schedules are measured against.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public BackgroundTaskRegistry(JobQueue queue, TimeProvider clock)
        : this(queue, clock, new PluginPublicationGate())
    {
    }

    /// <summary>Creates a task registry participating in the supplied publication boundary.</summary>
    public BackgroundTaskRegistry(
        JobQueue queue,
        TimeProvider clock,
        PluginPublicationGate publication)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(clock);

        _queue = queue;
        _clock = clock;
        _publication = publication ?? throw new ArgumentNullException(nameof(publication));
    }

    /// <summary>Gets the publication boundary this registry participates in.</summary>
    internal PluginPublicationGate PublicationGate => _publication;

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
    internal void RegisterJob(
        PluginId owner,
        CapabilitySet capabilities,
        IScheduledJob job,
        string schedule,
        MediaKindId? mediaKind)
    {
        if (!TryPrepare(owner, capabilities, job, schedule, mediaKind, out var candidate, out var error))
        {
            throw new ArronixException(
                CoreErrorCode.JobSchedulingFailed,
                error!);
        }

        if (!TryPublish(candidate!, out error))
        {
            throw new ArronixException(
                CoreErrorCode.JobSchedulingFailed,
                error!);
        }
    }

    /// <summary>Parses and validates one scheduled-job candidate without publishing it.</summary>
    internal bool TryPrepare(
        PluginId owner,
        CapabilitySet capabilities,
        IScheduledJob job,
        string schedule,
        MediaKindId? mediaKind,
        out RegisteredJob? candidate,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(job);

        // These are extension getters. Read each exactly once while admission is still a quarantine
        // boundary, then serve and schedule only the captured values.
        var jobId = job.JobId;
        var name = job.Name;
        var description = job.Description;
        var priority = job.Priority;
        var maxConcurrency = job.MaxConcurrency;
        var shutdownDeadline = JobScheduler.ClampShutdownDeadline(job.ShutdownDeadline);

        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);

        if (!JobScheduleParser.TryParse(schedule, out var parsed, out var parseError))
        {
            candidate = null;
            error = $"Job '{jobId}' cannot be scheduled: {parseError}";
            return false;
        }

        var granted = capabilities.WithImplied();
        candidate = new RegisteredJob(
            jobId,
            name,
            description,
            priority,
            maxConcurrency,
            shutdownDeadline,
            job,
            parsed,
            owner,
            granted,
            mediaKind,
            ConcurrencyGovernor.KeysFor(owner, granted, mediaKind?.Value));

        using var publication = _publication.EnterRead();
        if (_jobs.ContainsKey(jobId))
        {
            candidate = null;
            error = $"A job with identifier '{jobId}' is already registered.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Publishes one already-built job candidate.</summary>
    internal bool TryPublish(RegisteredJob candidate, out string? error)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        using var publication = _publication.EnterWrite();
        if (!_jobs.TryAdd(candidate.RegistrationId, candidate))
        {
            error = $"A job with identifier '{candidate.RegistrationId}' is already registered.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Removes exactly one published job and drops only that job's queued work.</summary>
    internal bool Remove(RegisteredJob candidate)
    {
        using var publication = _publication.EnterWrite();
        var removed = ((ICollection<KeyValuePair<string, RegisteredJob>>)_jobs)
            .Remove(new KeyValuePair<string, RegisteredJob>(candidate.RegistrationId, candidate));

        if (removed)
        {
            _queue.DropJobs([candidate.RegistrationId]);
        }

        return removed;
    }

    /// <inheritdoc />
    public void UnregisterJob(string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        using var publication = _publication.EnterWrite();
        if (!_jobs.TryGetValue(jobId, out var registered))
        {
            return;
        }

        if (registered.Owner != HostOwner)
        {
            throw new InvalidOperationException(
                $"Job '{jobId}' belongs to active extension '{registered.Owner}' and can be withdrawn only "
                + "through that extension's exact lifecycle receipt.");
        }

        if (((ICollection<KeyValuePair<string, RegisteredJob>>)_jobs)
            .Remove(new KeyValuePair<string, RegisteredJob>(jobId, registered)))
        {
            _queue.DropJobs([jobId]);
        }
    }

    /// <inheritdoc />
    public IScheduledJob? GetJob(string jobId)
    {
        using var publication = _publication.EnterRead();
        return jobId is not null && _jobs.TryGetValue(jobId, out var registered) ? registered.Job : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<IScheduledJob> GetAllJobs()
    {
        using var publication = _publication.EnterRead();
        return [.. _jobs.Values.OrderBy(job => job.RegistrationId, StringComparer.Ordinal).Select(job => job.Job)];
    }

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

        using (_publication.EnterRead())
        {
            if (jobId is null || !_jobs.TryGetValue(jobId, out var registered))
            {
                return Task.FromResult(false);
            }

            // Keep the same publication snapshot through entry construction and insertion. A withdrawal
            // cannot replace this identifier between lookup and queuing and leave an envelope carrying the
            // previous owner's throttle or media metadata behind.
            return Task.FromResult(TryEnqueueCurrent(
                registered,
                parameters,
                _clock.GetUtcNow(),
                correlationId,
                items: null,
                out _));
        }
    }

    /// <summary>
    /// Removes every job an extension registered.
    /// </summary>
    /// <param name="owner">The extension.</param>
    /// <returns>How many jobs were removed.</returns>
    internal int RemoveByPlugin(PluginId owner)
    {
        using var publication = _publication.EnterWrite();
        var owned = _jobs.Values
            .Where(job => job.Owner == owner)
            .Select(job => job.RegistrationId)
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
    internal IReadOnlyList<RegisteredJob> Registrations()
    {
        using var publication = _publication.EnterRead();
        return [.. _jobs.Values.OrderBy(job => job.RegistrationId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Looks up one registration.
    /// </summary>
    /// <param name="jobId">The job's identifier.</param>
    /// <returns>The registration, or <see langword="null"/> when there is none.</returns>
    internal RegisteredJob? Find(string jobId)
    {
        using var publication = _publication.EnterRead();
        return jobId is not null && _jobs.TryGetValue(jobId, out var registered) ? registered : null;
    }

    /// <summary>Determines whether this exact registration remains the published authority for its id.</summary>
    internal bool IsCurrent(RegisteredJob registered)
    {
        ArgumentNullException.ThrowIfNull(registered);

        using var publication = _publication.EnterRead();
        return _jobs.TryGetValue(registered.RegistrationId, out var current)
            && ReferenceEquals(current, registered);
    }

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
                registered.RegistrationId,
                registered.Name,
                registered.Description,
                registered.Owner.ToString(),
                registered.Schedule.Raw,
                registered.LastRun,
                registered.Schedule.NextRun(now, registered.LastRun),
                registered.LastSucceeded,
                registered.Priority)),
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
    internal JobQueueEntry Enqueue(
        RegisteredJob registered,
        IReadOnlyDictionary<string, object>? parameters,
        DateTimeOffset now,
        string? correlationId,
        IReadOnlyList<MediaItemRef>? items = null)
    {
        if (!TryEnqueueCurrent(registered, parameters, now, correlationId, items, out var entry))
        {
            throw new InvalidOperationException(
                $"Job registration '{registered.RegistrationId}' is no longer the published authority.");
        }

        return entry;
    }

    /// <summary>
    /// Queues work only while the exact registration remains published.
    /// </summary>
    /// <remarks>
    /// The read lease spans both the identity check and queue insertion. Withdrawal takes the matching
    /// write lease and drops queued entries before it returns, so a stale registration can neither enqueue
    /// after withdrawal nor target a replacement which reused its textual identifier.
    /// </remarks>
    internal bool TryEnqueueCurrent(
        RegisteredJob registered,
        IReadOnlyDictionary<string, object>? parameters,
        DateTimeOffset now,
        string? correlationId,
        IReadOnlyList<MediaItemRef>? items,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JobQueueEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(registered);

        using var publication = _publication.EnterRead();
        if (!_jobs.TryGetValue(registered.RegistrationId, out var current)
            || !ReferenceEquals(current, registered))
        {
            entry = null;
            return false;
        }

        entry = new JobQueueEntry
        {
            EntryId = Guid.NewGuid(),
            JobId = registered.RegistrationId,
            Owner = registered.Owner,
            CorrelationId = correlationId ?? CorrelationContext.New(),
            Parameters = parameters is null
                ? new Dictionary<string, object>(StringComparer.Ordinal)
                : new Dictionary<string, object>(parameters, StringComparer.Ordinal),
            ThrottleKeys = registered.ThrottleKeys,
            EnqueuedAt = now,
            NotBefore = now,
            Attempt = 1,
            Priority = registered.Priority,
            MediaKind = registered.MediaKind,
            Items = items is null ? [] : [.. items],
            Registration = registered,
        };

        registered.MarkQueued(now);
        _queue.Enqueue(entry);
        return true;
    }
}
