using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Naming;

/// <summary>
/// Defines the rename policy for organizing media files in the library.
/// Generates file names based on media metadata and naming templates.
/// </summary>
public interface IRenamePolicy
{
    /// <summary>
    /// Gets the media kind this rename policy applies to.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Generates a file name for a media item based on a naming template.
    /// </summary>
    /// <param name="itemId">The media item identifier.</param>
    /// <param name="namingTemplate">The naming template containing tokens.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The generated file name.</returns>
    Task<string> GenerateFileNameAsync(
        MediaItemId itemId,
        string namingTemplate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves naming tokens for a media item.
    /// </summary>
    /// <param name="itemId">The media item identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of token names to their resolved values.</returns>
    Task<IReadOnlyDictionary<string, string>> ResolveTokensAsync(
        MediaItemId itemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a naming template to ensure it only uses supported tokens.
    /// </summary>
    /// <param name="namingTemplate">The naming template to validate.</param>
    /// <returns>True if the template is valid; otherwise, false.</returns>
    bool ValidateTemplate(string namingTemplate);
}
