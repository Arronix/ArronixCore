using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Naming;

/// <summary>
/// Defines the folder structure and organization strategy for a media library.
/// Determines where media files should be stored based on metadata.
/// </summary>
public interface ILibraryLayout
{
    /// <summary>
    /// Gets the media kind this library layout applies to.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Generates the folder path for a media item.
    /// </summary>
    /// <param name="itemId">The media item identifier.</param>
    /// <param name="pathSpec">The library path specification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated folder path.</returns>
    Task<string> GenerateFolderPathAsync(
        MediaItemId itemId,
        LibraryPathSpec pathSpec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a library path specification.
    /// </summary>
    /// <param name="pathSpec">The path specification to validate.</param>
    /// <returns>True if the specification is valid; otherwise, false.</returns>
    bool ValidatePathSpec(LibraryPathSpec pathSpec);
}
