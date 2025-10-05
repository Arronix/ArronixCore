using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// Provides release candidates from indexer sources (e.g., torrent trackers, Usenet indexers).
/// </summary>
public interface IIndexerProvider
{
    /// <summary>
    /// Gets the media kind this indexer supports.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Gets the unique identifier for this indexer.
    /// </summary>
    string IndexerId { get; }

    /// <summary>
    /// Gets the human-readable name of this indexer.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Searches for release candidates matching the query.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of release candidates.</returns>
    Task<IReadOnlyList<ReleaseCandidate>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the capabilities of this indexer.
    /// </summary>
    IndexerCapabilities Capabilities { get; }
}

/// <summary>
/// Describes the capabilities of an indexer.
/// </summary>
public record IndexerCapabilities(
    bool SupportsSearch,
    bool SupportsRss,
    int MaxPageSize,
    IReadOnlyList<string> SupportedCategories);
