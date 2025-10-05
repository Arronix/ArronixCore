using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Defines the identity and capabilities of a media kind (e.g., TV, Movies, Music).
/// Each plugin implements this interface to declare what it can handle.
/// </summary>
public interface IMediaKind
{
    /// <summary>
    /// Gets the unique identifier for this media kind.
    /// </summary>
    MediaKindId Id { get; }

    /// <summary>
    /// Gets the human-readable name of this media kind.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the version of this media kind implementation.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the list of capabilities this media kind provides.
    /// Examples: "indexing", "metadata", "parsing", "renaming", "import"
    /// </summary>
    IReadOnlyList<string> Capabilities { get; }

    /// <summary>
    /// Gets the list of external identifier systems this media kind supports.
    /// Examples: "tvdb", "tmdb", "imdb", "musicbrainz"
    /// </summary>
    IReadOnlyList<string> SupportedIdentifiers { get; }

    /// <summary>
    /// Gets the list of naming tokens this media kind provides.
    /// Examples: "{SeriesTitle}", "{EpisodeNumber}", "{SeasonNumber}"
    /// </summary>
    IReadOnlyList<string> NamingTokens { get; }
}
