using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Arronix.Host.Scheduling;

/// <summary>
/// The one queue.
/// </summary>
/// <remarks>
/// <para>
/// One queue for the whole host, not one per extension and not one per subsystem. Several queues would each
/// need their own concurrency ceiling, and the ceilings would multiply: a deployment configured for four
/// concurrent operations would run four per queue. A single queue with per-key throttles expresses the same
/// intent and cannot be misread.
/// </para>
/// <para>
/// Ordering is by priority, then by the earliest time an entry may run, then by when it was queued. The last
/// of the three is what makes the queue fair: two entries that are otherwise equal run in the order they
/// arrived, so nothing starves behind a steady stream of equals.
/// </para>
/// </remarks>
public sealed class JobQueue
{
    private readonly List<JobQueueEntry> _entries = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// Gets how many entries are waiting.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// Adds an entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    public void Enqueue(JobQueueEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>
    /// Takes the highest-ranked entry whose earliest-run time has arrived.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="entry">The entry, when one is ready.</param>
    /// <returns><see langword="true"/> when an entry was taken.</returns>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "The try-and-out form is the standard queue idiom and avoids a nullable return whose null the caller would have to interpret.")]
    public bool TryDequeue(DateTimeOffset now, [NotNullWhen(true)] out JobQueueEntry? entry)
    {
        lock (_gate)
        {
            var index = -1;
            JobQueueEntry? best = null;

            for (var candidate = 0; candidate < _entries.Count; candidate++)
            {
                var current = _entries[candidate];

                if (current.NotBefore > now)
                {
                    continue;
                }

                if (best is null || Ranks(current, best))
                {
                    best = current;
                    index = candidate;
                }
            }

            if (best is null)
            {
                entry = null;
                return false;
            }

            _entries.RemoveAt(index);
            entry = best;
            return true;
        }
    }

    /// <summary>
    /// Lists the entries whose earliest-run time has arrived, best first.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <returns>The ready entries, in the order they should be considered.</returns>
    /// <remarks>
    /// The scheduler walks this list rather than taking the head, because an entry it cannot start — a
    /// saturated throttle, a job already at its own concurrency ceiling — must not hold up the ones behind
    /// it. Entries are removed by identifier once a start is committed to.
    /// </remarks>
    public IReadOnlyList<JobQueueEntry> Ready(DateTimeOffset now)
    {
        lock (_gate)
        {
            return
            [
                .. _entries
                    .Where(entry => entry.NotBefore <= now)
                    .OrderByDescending(entry => entry.Priority)
                    .ThenBy(entry => entry.NotBefore)
                    .ThenBy(entry => entry.EnqueuedAt),
            ];
        }
    }

    /// <summary>
    /// Removes one entry.
    /// </summary>
    /// <param name="entryId">The entry's identifier.</param>
    /// <returns><see langword="true"/> when the entry was there to remove.</returns>
    public bool Remove(Guid entryId)
    {
        lock (_gate)
        {
            return _entries.RemoveAll(entry => entry.EntryId == entryId) > 0;
        }
    }

    /// <summary>
    /// Removes every entry belonging to an extension.
    /// </summary>
    /// <param name="jobIds">The job identifiers to drop.</param>
    /// <returns>How many entries were dropped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jobIds"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Used when an extension is stopped or quarantined. Leaving its work queued would mean the scheduler
    /// repeatedly dequeuing entries for a job that no longer exists.
    /// </remarks>
    public int DropJobs(IReadOnlyCollection<string> jobIds)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        if (jobIds.Count == 0)
        {
            return 0;
        }

        var wanted = jobIds.ToHashSet(StringComparer.Ordinal);

        lock (_gate)
        {
            return _entries.RemoveAll(entry => wanted.Contains(entry.JobId));
        }
    }

    /// <summary>
    /// Lists everything waiting, in the order it will be considered.
    /// </summary>
    /// <returns>The entries.</returns>
    public IReadOnlyList<JobQueueEntry> Snapshot()
    {
        lock (_gate)
        {
            return
            [
                .. _entries
                    .OrderByDescending(entry => entry.Priority)
                    .ThenBy(entry => entry.NotBefore)
                    .ThenBy(entry => entry.EnqueuedAt),
            ];
        }
    }

    private static bool Ranks(JobQueueEntry candidate, JobQueueEntry incumbent)
    {
        if (candidate.Priority != incumbent.Priority)
        {
            return candidate.Priority > incumbent.Priority;
        }

        if (candidate.NotBefore != incumbent.NotBefore)
        {
            return candidate.NotBefore < incumbent.NotBefore;
        }

        return candidate.EnqueuedAt < incumbent.EnqueuedAt;
    }
}
