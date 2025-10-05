using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Parsing;

/// <summary>
/// Parses release names/titles to extract structured information about the content.
/// Each media kind plugin provides its own implementation.
/// </summary>
public interface IReleaseParser
{
    /// <summary>
    /// Gets the media kind this parser handles.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Parses a release name/title to extract structured information.
    /// </summary>
    /// <param name="releaseTitle">The release title to parse.</param>
    /// <returns>A parsed release containing extracted information, or null if parsing failed.</returns>
    ParsedRelease? Parse(string releaseTitle);

    /// <summary>
    /// Validates whether a release title can be parsed by this parser.
    /// </summary>
    /// <param name="releaseTitle">The release title to validate.</param>
    /// <returns>True if the title can be parsed; otherwise, false.</returns>
    bool CanParse(string releaseTitle);
}
