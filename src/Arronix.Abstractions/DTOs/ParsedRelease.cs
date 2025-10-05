using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Represents the parsed information extracted from a release title.
/// Contains structured data about the content, quality, and other metadata.
/// </summary>
public record ParsedRelease(
    MediaKindId MediaKind,
    string Title,
    string? Year = null,
    string? Quality = null,
    string? Codec = null,
    string? AudioCodec = null,
    string? Resolution = null,
    string? ReleaseGroup = null,
    IReadOnlyList<Language>? Languages = null,
    IReadOnlyDictionary<string, string>? AdditionalMetadata = null);
