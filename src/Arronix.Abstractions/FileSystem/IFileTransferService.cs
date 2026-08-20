
namespace Arronix.Abstractions.FileSystem;

/// <summary>
/// Moves and copies content with verification, rollback and hard-link negotiation.
/// </summary>
/// <remarks>
/// <para>
/// Placing a file safely is harder than it looks: the destination may be on another volume, a hard link
/// may or may not be possible, a partial transfer must not be left behind under the destination name,
/// and the result must be verified before the source is released. Nothing that places files should
/// reimplement that, which is why the contract crosses the boundary rather than staying host-side.
/// </para>
/// <para>
/// Every path argument is subject to the same scoping as the file system contract: a caller can only
/// transfer within the roots it was granted.
/// </para>
/// </remarks>
public interface IFileTransferService
{
    /// <summary>
    /// Transfers a single file.
    /// </summary>
    /// <param name="sourcePath">The file to transfer.</param>
    /// <param name="targetPath">Where to place it.</param>
    /// <param name="mode">The transfer to attempt.</param>
    /// <param name="overwrite">Whether an existing destination may be replaced.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The transfer that actually took place, which may be narrower than the one requested — a
    /// <see cref="FileTransferMode.HardLinkOrCopy"/> request returns whichever of the two succeeded.
    /// </returns>
    /// <exception cref="DestinationExistsException">
    /// The destination exists and <paramref name="overwrite"/> is <see langword="false"/>.
    /// </exception>
    Task<FileTransferMode> TransferFileAsync(
        string sourcePath,
        string targetPath,
        FileTransferMode mode,
        bool overwrite = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transfers a folder and its contents.
    /// </summary>
    /// <param name="sourcePath">The folder to transfer.</param>
    /// <param name="targetPath">Where to place it.</param>
    /// <param name="mode">The transfer to attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transfer that actually took place.</returns>
    Task<FileTransferMode> TransferFolderAsync(
        string sourcePath,
        string targetPath,
        FileTransferMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the destination folder match the source folder, transferring what differs and removing
    /// what the source no longer has.
    /// </summary>
    /// <param name="sourcePath">The folder to mirror from.</param>
    /// <param name="targetPath">The folder to mirror to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of files transferred.</returns>
    Task<int> MirrorFolderAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken = default);
}
