using System.ComponentModel.DataAnnotations;

namespace Arronix.Host.Configuration;

/// <summary>
/// Operator control over the single scheduler and its queue.
/// </summary>
/// <remarks>
/// <para>
/// There is one scheduler and one queue for the whole host, so these numbers are the only place the total
/// load a deployment will offer to the outside world is decided. Three limits apply at once and all of them
/// must be free before work starts: a global ceiling, the per-job ceiling the job itself declares, and a
/// throttle per capability-scoped key.
/// </para>
/// <para>
/// The throttle defaults are deliberately asymmetric. Metadata catalogs and importers are the two things
/// a deployment can most easily overwhelm — one because catalog services are shared community
/// infrastructure, the other because it is disk-bound — while release sources tolerate more and transfer
/// clients are local.
/// </para>
/// </remarks>
public sealed class SchedulerOptions
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:Scheduler";

    /// <summary>
    /// Gets or sets the number of queue entries that may execute at once, across every job and every
    /// extension.
    /// </summary>
    [Range(1, 256)]
    public int MaxConcurrentJobs { get; set; } = 4;

    /// <summary>
    /// Gets or sets how often the queue is examined for entries whose earliest-run time has arrived. The
    /// scheduler is woken by an enqueue as well, so this only bounds how late a delayed retry can start.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:05:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets how many times an entry classified as retryable is retried before it is abandoned. The
    /// back-off ladder has ten rungs, so a value above ten simply repeats the last one.
    /// </summary>
    [Range(0, 32)]
    public int MaxAttempts { get; set; } = 6;

    /// <summary>
    /// Gets or sets the depth at which the queue is reported as backed up. It is a health signal, not a
    /// cap: refusing work would lose it, and a queue that is too long is an operator problem rather than a
    /// runtime one.
    /// </summary>
    [Range(1, 1_000_000)]
    public int BacklogWarningDepth { get; set; } = 500;

    /// <summary>
    /// Gets the per-key concurrency ceilings, keyed by the throttle key the host synthesizes. Keys not
    /// present here are unthrottled beyond the global ceiling.
    /// </summary>
    /// <remarks>
    /// Keys are the capability-scoped form <c>cap:{capability}</c>, the extension-scoped form
    /// <c>plugin:{id}</c> and the kind-scoped form <c>kind:{id}</c>. Bare capability names are accepted as a
    /// convenience and normalized to the <c>cap:</c> form when the options are read, so an operator can
    /// write <c>"indexing": 8</c> without knowing the synthesis rule.
    /// </remarks>
    public IDictionary<string, int> ThrottleLimits { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["indexing"] = 4,
        ["metadata"] = 2,
        ["import"] = 2,
        ["download"] = 8,
    };
}
