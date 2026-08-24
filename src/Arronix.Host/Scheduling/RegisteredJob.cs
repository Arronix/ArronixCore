using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Scheduling;


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
        string registrationId,
        string name,
        string description,
        int priority,
        int maxConcurrency,
        TimeSpan shutdownDeadline,
        IScheduledJob job,
        JobSchedule schedule,
        PluginId owner,
        CapabilitySet capabilities,
        MediaKindId? mediaKind,
        IReadOnlyList<string> throttleKeys)
    {
        RegistrationId = registrationId;
        Name = name;
        Description = description;
        Priority = priority;
        MaxConcurrency = maxConcurrency;
        ShutdownDeadline = shutdownDeadline;
        Job = job;
        Schedule = schedule;
        Owner = owner;
        Capabilities = capabilities;
        MediaKind = mediaKind;
        ThrottleKeys = [.. throttleKeys];
    }

    /// <summary>Gets the job identifier captured before publication.</summary>
    public string RegistrationId { get; }

    /// <summary>Gets the display name captured during admission.</summary>
    public string Name { get; }

    /// <summary>Gets the description captured during admission.</summary>
    public string Description { get; }

    /// <summary>Gets the queue priority captured during admission.</summary>
    public int Priority { get; }

    /// <summary>Gets the concurrency ceiling captured during admission.</summary>
    public int MaxConcurrency { get; }

    /// <summary>Gets the Host-clamped shutdown deadline captured during admission.</summary>
    public TimeSpan ShutdownDeadline { get; }

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
    public bool HasCapacity() => MaxConcurrency <= 0 || Running < MaxConcurrency;

    internal void MarkQueued(DateTimeOffset when)
        => Interlocked.Exchange(ref _lastRunTicks, when.UtcTicks);

    /// <summary>Atomically reserves one execution slot.</summary>
    internal bool TryMarkStarted()
    {
        while (true)
        {
            var running = Volatile.Read(ref _running);
            if (MaxConcurrency > 0 && running >= MaxConcurrency)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _running, running + 1, running) == running)
            {
                return true;
            }
        }
    }

    /// <summary>Releases a reserved slot when queue or global-capacity admission did not complete.</summary>
    internal void ReleaseStart() => Interlocked.Decrement(ref _running);

    internal void MarkFinished(bool succeeded)
    {
        Interlocked.Decrement(ref _running);
        Volatile.Write(ref _succeeded, succeeded ? 1 : 0);
    }
}
