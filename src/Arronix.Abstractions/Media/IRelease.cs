
namespace Arronix.Abstractions.Media;

/// <summary>
/// An interpreted publication of a media kind.
/// </summary>
/// <remarks>
/// A release is not an indexer row. An indexer row is transport-shaped data that still has to be
/// interpreted by a media kind. Implementations belong to media extensions because only the extension
/// owns the typed identity, coverage and media-specific facts a publication can state.
/// </remarks>
public interface IRelease
{
    /// <summary>Gets the media title stated by the publication.</summary>
    string Title { get; }

    /// <summary>Gets the year stated by the publication, when present.</summary>
    int? Year { get; }

    /// <summary>Gets the edition or cut stated by the publication, when present.</summary>
    string? Edition { get; }
}
