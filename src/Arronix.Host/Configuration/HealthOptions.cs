using System.ComponentModel.DataAnnotations;

namespace Arronix.Host.Configuration;

/// <summary>
/// Operator control over how health is collected.
/// </summary>
/// <remarks>
/// The two numbers here exist for the same reason: a health endpoint must answer. A contributor that hangs
/// is bounded by <see cref="ContributorTimeout"/> and reported as unhealthy on its own account rather than
/// stalling the report, and repeated polling is bounded by <see cref="CacheLifetime"/> so that a monitoring
/// system asking every second does not turn every contributor into a load source.
/// </remarks>
public sealed class HealthOptions
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:Health";

    /// <summary>
    /// Gets or sets how long one contributor may take before its result is replaced by a synthetic
    /// unhealthy check attributed to it.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:05:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan ContributorTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets how long a collected snapshot is reused. The cache is invalidated eagerly when an
    /// extension changes state or a provider's status changes, so this only bounds how stale a report can
    /// be when nothing has happened.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:10:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan CacheLifetime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the identifiers of contributors the operator has switched off. Suppressing a check is
    /// preferable to an operator learning to ignore it.
    /// </summary>
    public IList<string> SuppressedContributors { get; } = [];
}
