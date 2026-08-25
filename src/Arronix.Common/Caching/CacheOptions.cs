using System.ComponentModel.DataAnnotations;

namespace Arronix.Common.Caching;

/// <summary>
/// Operator control over the platform's cache sweep.
/// </summary>
/// <remarks>
/// About memory rather than correctness: an expired entry is never returned, so an unrun sweep costs bytes
/// and never a wrong answer.
/// </remarks>
public sealed class CacheOptions
{
    /// <summary>The configuration section this options type binds from.</summary>
    public const string SectionName = "Arronix:Caching";

    /// <summary>The interval used when none is configured.</summary>
    public static readonly TimeSpan DefaultSweepInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how often expired entries are removed. <see cref="TimeSpan.Zero"/> disables the sweep.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "1.00:00:00")]
    public TimeSpan SweepInterval { get; set; } = DefaultSweepInterval;
}
