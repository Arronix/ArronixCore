
namespace Arronix.Abstractions.Definition;

/// <summary>
/// Per-kind tuning of one host-cataloged distance feature.
/// </summary>
/// <remarks>
/// Weights and thresholds are data; what the feature computes is not. This row deliberately carries no
/// operator and no subject path — the surveyed feature arguments are imperative aggregations a path
/// grammar cannot reach, and pretending otherwise was an over-claim this surface does not repeat.
/// </remarks>
public sealed record FeatureParameter
{
    /// <summary>
    /// Gets the feature's identifier in the host catalog.
    /// </summary>
    public required string FeatureId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the feature participates at all.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the feature's weight in the combined distance.
    /// </summary>
    public double Weight { get; init; } = 1.0;

    /// <summary>
    /// Gets the feature's threshold override, for features that declare one.
    /// </summary>
    public double? Threshold { get; init; }
}
