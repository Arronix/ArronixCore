
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Defines a cutoff policy for quality upgrades.
/// Once a quality tier meets or exceeds the cutoff, no further upgrades are searched for.
/// </summary>
/// <remarks>
/// The cutoff compares <see cref="QualityTier.EffectiveWeight"/>, never rank: two tiers in one quality
/// group carry equal weight, so either satisfies a cutoff set at the other. Comparing rank here gave the
/// domain-wrong answer for every grouped rung.
/// </remarks>
public record CutoffPolicy(
    QualityTier CutoffTier,
    bool EnableUpgrades = true,
    int? MaxSearchDays = null)
{
    /// <summary>
    /// Gets what a re-issued release means once the cutoff is already met.
    /// </summary>
    public ProperHandling ProperHandling { get; init; } = ProperHandling.PreferProper;

    /// <summary>
    /// Determines if a quality tier meets the cutoff threshold.
    /// </summary>
    /// <param name="quality">The quality tier to check.</param>
    /// <returns>True if the quality meets or exceeds cutoff; otherwise, false.</returns>
    public bool MeetsCutoff(QualityTier quality)
    {
        return quality.EffectiveWeight >= CutoffTier.EffectiveWeight;
    }

    /// <summary>
    /// Determines if upgrades should still be searched for given the current quality.
    /// </summary>
    /// <param name="currentQuality">The current quality tier.</param>
    /// <returns>True if upgrades should be searched; otherwise, false.</returns>
    public bool ShouldSearchForUpgrade(QualityTier currentQuality)
    {
        return EnableUpgrades && !MeetsCutoff(currentQuality);
    }
}
