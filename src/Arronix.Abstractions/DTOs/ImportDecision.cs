using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Represents a decision about whether a file should be imported into the library.
/// Contains the evaluation result and any relevant metadata.
/// </summary>
public record ImportDecision(
    string FilePath,
    MediaItemId TargetItemId,
    bool ShouldImport,
    ImportReason Reason,
    string? DestinationPath = null,
    string? RejectionReason = null,
    IReadOnlyList<string>? Warnings = null);

/// <summary>
/// Indicates the reason for an import decision.
/// </summary>
public enum ImportReason
{
    /// <summary>File rejected, should not be imported.</summary>
    Rejected = 0,

    /// <summary>New content, should be imported.</summary>
    NewContent = 1,

    /// <summary>Upgrade over existing content, should be imported.</summary>
    Upgrade = 2,

    /// <summary>Duplicate of existing content with same quality.</summary>
    Duplicate = 3,

    /// <summary>Lower quality than existing content.</summary>
    Downgrade = 4
}
