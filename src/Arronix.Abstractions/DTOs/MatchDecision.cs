using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Represents a decision about whether a release matches the desired content.
/// Used during the matching phase to determine if a release should be considered.
/// </summary>
public record MatchDecision(
    ReleaseId ReleaseId,
    MediaItemId? MatchedItemId,
    bool IsMatch,
    MatchConfidence Confidence,
    string? RejectionReason = null,
    IReadOnlyList<string>? Warnings = null);

/// <summary>
/// Indicates the confidence level of a match decision.
/// </summary>
public enum MatchConfidence
{
    /// <summary>No match found.</summary>
    None = 0,

    /// <summary>Low confidence match, may be incorrect.</summary>
    Low = 1,

    /// <summary>Medium confidence match, likely correct.</summary>
    Medium = 2,

    /// <summary>High confidence match, very likely correct.</summary>
    High = 3,

    /// <summary>Exact match, definitely correct.</summary>
    Exact = 4
}
