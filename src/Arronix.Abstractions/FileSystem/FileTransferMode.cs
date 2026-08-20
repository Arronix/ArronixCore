
namespace Arronix.Abstractions.FileSystem;

/// <summary>
/// How a file transfer should place its content, and — on the way back out — how it actually did.
/// </summary>
/// <remarks>
/// The values combine, so a caller can ask for a hard link with a fall back to a copy and learn from
/// the return value which one happened. That distinction matters: a hard link shares storage with the
/// source, so deleting the source later frees nothing.
/// </remarks>
[Flags]
public enum FileTransferMode
{
    /// <summary>Nothing was transferred.</summary>
    None = 0,

    /// <summary>Move the content, leaving nothing at the source.</summary>
    Move = 1,

    /// <summary>Copy the content, leaving the source in place.</summary>
    Copy = 2,

    /// <summary>Create a hard link, so source and destination share storage.</summary>
    HardLink = 4,

    /// <summary>Try a hard link and fall back to a copy when the platform or volume refuses one.</summary>
    HardLinkOrCopy = Copy | HardLink
}
