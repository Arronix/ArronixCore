using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// Provides metadata for media items from external sources.
/// Examples: TVDB, TMDB, TheTVDB, TheMovieDB, MusicBrainz
/// </summary>
public interface IMetadataProvider
{
    /// <summary>
    /// Gets the media kind this provider supports.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Gets the unique identifier for this metadata provider.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Gets the human-readable name of this provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Searches for media items by title.
    /// </summary>
    /// <param name="title">The title to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of matching media items with their external identifiers.</returns>
    Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(
        string title,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets detailed metadata for a media item by its external identifier.
    /// </summary>
    /// <param name="externalId">The external identifier from this provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detailed metadata for the media item, or null if not found.</returns>
    Task<MetadataItem?> GetMetadataAsync(string externalId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a search result from a metadata provider.
/// </summary>
public record MetadataSearchResult(string ExternalId, string Title, int? Year, string? Description);

/// <summary>
/// Represents detailed metadata for a media item.
/// </summary>
public record MetadataItem(
    string ExternalId,
    string Title,
    int? Year,
    string? Description,
    IReadOnlyDictionary<string, string> AdditionalData);
