using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Resolves external media identifiers (e.g., TVDB, TMDB, IMDb) to internal MediaItemIds.
/// Each media kind plugin provides an implementation for its supported identifier systems.
/// </summary>
public interface IMediaIdResolver
{
    /// <summary>
    /// Gets the media kind this resolver is for.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Gets the external identifier system this resolver handles.
    /// Examples: "tvdb", "tmdb", "imdb"
    /// </summary>
    string IdentifierSystem { get; }

    /// <summary>
    /// Resolves an external identifier to an internal media item ID.
    /// </summary>
    /// <param name="externalId">The external identifier value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The internal media item ID, or null if not found.</returns>
    Task<MediaItemId?> ResolveAsync(string externalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves multiple external identifiers to internal media item IDs.
    /// </summary>
    /// <param name="externalIds">The external identifier values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary mapping external IDs to internal media item IDs.</returns>
    Task<IReadOnlyDictionary<string, MediaItemId>> ResolveManyAsync(
        IEnumerable<string> externalIds,
        CancellationToken cancellationToken = default);
}
