using System.ComponentModel.DataAnnotations;

namespace Arronix.Host.Configuration;

/// <summary>
/// Operator control over where library content lives.
/// </summary>
/// <remarks>
/// Root folders are a host concept rather than a media-kind concept: every surveyed application has them,
/// none of them means anything different by them, and an extension that could define its own would be able
/// to place files outside the roots the operator granted. The list is therefore held here and handed to the
/// scoping decorators, never declared in a shape.
/// </remarks>
public sealed class LibraryOptions
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:Library";

    /// <summary>
    /// Gets the folders content may be placed in. Empty is valid: a host with no root folder can still
    /// browse a catalog, it simply cannot import.
    /// </summary>
    public IList<string> RootFolders { get; } = [];

    /// <summary>
    /// Gets or sets the root folder used when a request does not name one. Must be one of
    /// <see cref="RootFolders"/> when set; the media registration validates that at startup rather than at
    /// the first import.
    /// </summary>
    public string? DefaultRootFolder { get; set; }

    /// <summary>
    /// Gets or sets how many items one catalog page may carry. It bounds both the API's page size and the
    /// memory a single projection can consume, which matters because the catalog is projected by
    /// extension code the host does not control.
    /// </summary>
    [Range(1, 1000)]
    public int MaxPageSize { get; set; } = 250;
}
