using System.ComponentModel.DataAnnotations;

namespace Arronix.Common.Caching;

/// <summary>
/// Operator control over the platform's cache sweep.
/// </summary>
/// <remarks>
/// There is one setting, and it is about memory rather than correctness: an expired entry is never
/// returned, so a sweep that has not run yet costs bytes and never a wrong answer.
/// </remarks>
public sealed class CacheOptions
{
    /// <summary>The configuration section this options type binds from.</summary>
    public const string SectionName = "Arronix:Caching";

    /// <summary>The interval used when none is configured.</summary>
    public static readonly TimeSpan DefaultSweepInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how often expired entries are removed. <see cref="TimeSpan.Zero"/> disables the sweep,
    /// which is a deliberate choice for a host that would rather hold the memory than run the timer.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan SweepInterval { get; set; } = DefaultSweepInterval;
}
