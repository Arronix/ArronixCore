using Arronix.Abstractions.DTOs;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Everything the platform knows about one library file, carried as one value.
/// </summary>
/// <remarks>
/// <para>
/// The contract previously had a file identifier and nothing else, which made most naming tokens
/// unreachable: the majority of a rendered file name is file properties — quality, release group,
/// languages, technical facets — not item properties. This record is the missing subject.
/// </para>
/// </remarks>
public sealed record MediaFileFacts
{
    /// <summary>
    /// Gets the file's identifier.
    /// </summary>
    public required MediaFileId Id { get; init; }

    /// <summary>
    /// Gets the file's current path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the release name the file arrived under, when it is known.
    /// </summary>
    public string? SceneName { get; init; }

    /// <summary>
    /// Gets the original file name the file arrived under, when it is known.
    /// </summary>
    public string? OriginalFileName { get; init; }

    /// <summary>
    /// Gets the file's size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Gets the file's evaluated quality.
    /// </summary>
    public required QualityTier Quality { get; init; }

    /// <summary>
    /// Gets the group that published the release, when it is known.
    /// </summary>
    public string? ReleaseGroup { get; init; }

    /// <summary>
    /// Gets the languages the file carries.
    /// </summary>
    public IReadOnlyList<Language> Languages { get; init; } = [];

}
