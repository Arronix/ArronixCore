using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Temporary media-neutral compatibility projection for media types that have not yet moved to a typed
/// <see cref="Media.IRelease"/>.
/// </summary>
public record ParsedRelease(
    MediaKindId MediaKind,
    string Title,
    string? Year = null,
    string? Quality = null,
    string? ReleaseGroup = null,
    IReadOnlyList<Language>? Languages = null,
    IReadOnlyDictionary<string, string>? AdditionalMetadata = null);
