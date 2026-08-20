
namespace Arronix.Abstractions.FileSystem;

/// <summary>
/// The path grammar a <see cref="PlatformPath"/> is written in.
/// </summary>
public enum PlatformPathKind
{
    /// <summary>
    /// The grammar could not be determined — typically a bare relative path with no separator, which is
    /// valid in both grammars.
    /// </summary>
    Unknown = 0,

    /// <summary>Backslash-separated, with drive letters and UNC roots, compared without regard to case.</summary>
    Windows = 1,

    /// <summary>Slash-separated, rooted at a single slash, compared with regard to case.</summary>
    Unix = 2
}
