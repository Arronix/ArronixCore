using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Import;

/// <summary>
/// Defines the import pipeline for a media kind.
/// Handles validation, processing, and storage of downloaded media files.
/// </summary>
public interface IImportPipeline
{
    /// <summary>
    /// Gets the media kind this import pipeline handles.
    /// </summary>
    MediaKindId MediaKind { get; }

    /// <summary>
    /// Evaluates whether a file should be imported.
    /// </summary>
    /// <param name="filePath">The path to the file to evaluate.</param>
    /// <param name="parsedRelease">The parsed release information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An import decision indicating whether to proceed.</returns>
    Task<ImportDecision> EvaluateImportAsync(
        string filePath,
        ParsedRelease parsedRelease,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a file into the library.
    /// </summary>
    /// <param name="decision">The import decision to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if import succeeded; otherwise, false.</returns>
    Task<bool> ImportAsync(ImportDecision decision, CancellationToken cancellationToken = default);
}
