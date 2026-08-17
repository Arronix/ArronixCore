using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Scheduling;

// The plugin contracts are experimental; a registration records who owns the job and what they may do.
#pragma warning disable ARX0014

namespace Arronix.Host.Scheduling;

/// <summary>
/// One registered job, with everything the scheduler needs to run it and everything an operator needs to
/// understand it.
/// </summary>
/// <remarks>
/// The throttle keys are computed once, at registration, rather than per run. They are a function of the
/// owning extension's grant and the media kind the job serves, neither of which changes while the extension
/// is loaded, and recomputing them on every dequeue would put a set union in the scheduler's inner loop.
/// </remarks>
public sealed class RegisteredJob
{
    private long _lastRunTicks;
    private int _running;
    private int _succeeded = 1;

    internal RegisteredJob(
        IScheduledJob job,
        JobSchedule schedule,
        PluginId owner,
        CapabilitySet capabilities,
        MediaKindId? mediaKind,
        IReadOnlyList<string> throttleKeys)
    {
        Job = job;
        Schedule = schedule;
        Owner = owner;
        Capabilities = capabilities;
        MediaKind = mediaKind;
        ThrottleKeys = throttleKeys;
    }

    /// <summary>Gets the job itself.</summary>
    public IScheduledJob Job { get; }

    /// <summary>Gets when it runs.</summary>
    public JobSchedule Schedule { get; }

    /// <summary>Gets the extension that registered it.</summary>
    public PluginId Owner { get; }

    /// <summary>Gets that extension's granted capabilities, after implication.</summary>
    public CapabilitySet Capabilities { get; }

    /// <summary>Gets the media kind its work concerns, when it concerns one.</summary>
    public MediaKindId? MediaKind { get; }

    /// <summary>Gets the concurrency keys every run of it must hold.</summary>
    public IReadOnlyList<string> ThrottleKeys { get; }

    /// <summary>
    /// Gets when the job was last queued or run, which is what the schedule measures the next run from.
    /// </summary>
    /// <remarks>
    /// Queuing rather than completion, so that a job whose run takes longer than its interval does not
    /// immediately become due again the moment it finishes.
    /// </remarks>
    public DateTimeOffset? LastRun
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastRunTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Gets how many runs of this job are in flight.</summary>
    public int Running => Volatile.Read(ref _running);

    /// <summary>Gets a value indicating whether the last completed run succeeded.</summary>
    public bool LastSucceeded => Volatile.Read(ref _succeeded) != 0;

    /// <summary>Determines whether another run may start.</summary>
    /// <returns><see langword="true"/> when the job is below its own concurrency ceiling.</returns>
    public bool HasCapacity() => Job.MaxConcurrency <= 0 || Running < Job.MaxConcurrency;

    internal void MarkQueued(DateTimeOffset when)
        => Interlocked.Exchange(ref _lastRunTicks, when.UtcTicks);

    internal void MarkStarted() => Interlocked.Increment(ref _running);

    internal void MarkFinished(bool succeeded)
    {
        Interlocked.Decrement(ref _running);
        Volatile.Write(ref _succeeded, succeeded ? 1 : 0);
    }
}
