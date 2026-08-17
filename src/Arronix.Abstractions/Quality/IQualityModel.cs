using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Quality;

/// <summary>
/// Defines quality evaluation and comparison logic for a media kind.
/// Determines quality tiers, cutoff policies, and upgrade decisions.
/// </summary>
public interface IQualityModel
{
    /// <summary>
    /// Gets the media kind this quality model applies to.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Evaluates the quality of a parsed release.
    /// </summary>
    /// <param name="parsedRelease">The parsed release to evaluate.</param>
    /// <returns>A quality tier describing the release quality.</returns>
    QualityTier EvaluateQuality(ParsedRelease parsedRelease);

    /// <summary>
    /// Determines if a new release is an upgrade over an existing one.
    /// </summary>
    /// <param name="currentQuality">The quality of the existing release.</param>
    /// <param name="newQuality">The quality of the new release.</param>
    /// <returns>True if the new release is an upgrade; otherwise, false.</returns>
    bool IsUpgrade(QualityTier currentQuality, QualityTier newQuality);

    /// <summary>
    /// Determines if a quality tier meets the cutoff threshold.
    /// </summary>
    /// <param name="quality">The quality tier to check.</param>
    /// <param name="cutoff">The cutoff policy to apply.</param>
    /// <returns>True if the quality meets cutoff; otherwise, false.</returns>
    bool MeetsCutoff(QualityTier quality, CutoffPolicy cutoff);
}
