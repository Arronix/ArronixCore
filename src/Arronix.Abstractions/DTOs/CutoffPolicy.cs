namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Defines a cutoff policy for quality upgrades.
/// Once a quality tier meets or exceeds the cutoff, no further upgrades are searched for.
/// </summary>
public record CutoffPolicy(
    QualityTier CutoffTier,
    bool EnableUpgrades = true,
    int? MaxSearchDays = null)
{
    /// <summary>
    /// Determines if a quality tier meets the cutoff threshold.
    /// </summary>
    /// <param name="quality">The quality tier to check.</param>
    /// <returns>True if the quality meets or exceeds cutoff; otherwise, false.</returns>
    public bool MeetsCutoff(QualityTier quality)
    {
        return quality.Rank >= CutoffTier.Rank;
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
