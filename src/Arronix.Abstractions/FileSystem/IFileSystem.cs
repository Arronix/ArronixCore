using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Arronix.Abstractions.FileSystem;

/// <summary>
/// The file system as an extension sees it: probe, read, write and enumerate, within granted roots.
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberately narrow view. The host's own file system surface is several times larger, and
/// includes emptying folders, deleting arbitrary trees, changing permissions and enumerating every
/// mount on the machine. None of that belongs in a contract handed to code loaded from disk at runtime,
/// so none of it is here.
/// </para>
/// <para>
/// Every path argument is checked against the granted roots before the operation runs. A path outside
/// them fails with <see cref="UnauthorizedAccessException"/> regardless of what the underlying platform
/// permissions would have allowed — the check is the enforcement point, not a hint.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.FileSystem, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IFileSystem
{
    /// <summary>
    /// Determines whether a file exists.
    /// </summary>
    /// <param name="path">The path to probe.</param>
    /// <returns><see langword="true"/> when the file exists.</returns>
    bool FileExists(string path);

    /// <summary>
    /// Determines whether a folder exists.
    /// </summary>
    /// <param name="path">The path to probe.</param>
    /// <returns><see langword="true"/> when the folder exists.</returns>
    bool FolderExists(string path);

    /// <summary>
    /// Determines whether a folder can actually be written to, by writing and removing a probe file.
    /// </summary>
    /// <param name="path">The folder to test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the folder accepted the write.</returns>
    /// <remarks>
    /// Reported permissions are not the same as an accepted write on a network share or an exported
    /// volume, which is why this probes rather than inspects.
    /// </remarks>
    Task<bool> IsFolderWritableAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the free space available on the volume holding a path, in bytes.
    /// </summary>
    /// <param name="path">A path on the volume.</param>
    /// <returns>The free space, or <see langword="null"/> when it cannot be determined.</returns>
    long? GetAvailableSpace(string path);

    /// <summary>
    /// Gets the total capacity of the volume holding a path, in bytes.
    /// </summary>
    /// <param name="path">A path on the volume.</param>
    /// <returns>The capacity, or <see langword="null"/> when it cannot be determined.</returns>
    long? GetTotalSize(string path);

    /// <summary>
    /// Gets the size of a file, in bytes.
    /// </summary>
    /// <param name="path">The file to measure.</param>
    /// <returns>The size in bytes.</returns>
    long GetFileSize(string path);

    /// <summary>
    /// Gets the last write time of a file, in UTC.
    /// </summary>
    /// <param name="path">The file to inspect.</param>
    /// <returns>The last write time.</returns>
    DateTimeOffset GetLastWriteTimeUtc(string path);

    /// <summary>
    /// Sets the last write time of a file, in UTC.
    /// </summary>
    /// <param name="path">The file to change.</param>
    /// <param name="lastWriteTimeUtc">The timestamp to apply.</param>
    void SetLastWriteTimeUtc(string path, DateTimeOffset lastWriteTimeUtc);

    /// <summary>
    /// Enumerates the files in a folder.
    /// </summary>
    /// <param name="path">The folder to enumerate.</param>
    /// <param name="recursive">Whether to descend into subfolders.</param>
    /// <returns>The file paths, enumerated lazily.</returns>
    IEnumerable<string> EnumerateFiles(string path, bool recursive = false);

    /// <summary>
    /// Enumerates the immediate subfolders of a folder.
    /// </summary>
    /// <param name="path">The folder to enumerate.</param>
    /// <returns>The folder paths, enumerated lazily.</returns>
    IEnumerable<string> EnumerateDirectories(string path);

    /// <summary>
    /// Reads a whole file as text.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file contents.</returns>
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes text to a file, replacing anything already there.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="contents">The text to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the file has been written.</returns>
    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file for reading.
    /// </summary>
    /// <param name="path">The file to open.</param>
    /// <returns>A readable stream the caller owns and must dispose.</returns>
    Stream OpenRead(string path);

    /// <summary>
    /// Opens a file for writing, creating it or truncating it.
    /// </summary>
    /// <param name="path">The file to open.</param>
    /// <returns>A writable stream the caller owns and must dispose.</returns>
    Stream OpenWrite(string path);

    /// <summary>
    /// Writes a stream to a file.
    /// </summary>
    /// <param name="source">The stream to read from. Not disposed.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the stream has been written.</returns>
    Task SaveStreamAsync(Stream source, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a folder and any missing parents. Creating an existing folder is not an error.
    /// </summary>
    /// <param name="path">The folder to create.</param>
    void EnsureFolder(string path);

    /// <summary>
    /// Deletes a file. Deleting a file that is not there is not an error.
    /// </summary>
    /// <param name="path">The file to delete.</param>
    void DeleteFile(string path);

    /// <summary>
    /// Copies a file.
    /// </summary>
    /// <param name="sourcePath">The file to copy.</param>
    /// <param name="destinationPath">Where to copy it.</param>
    /// <param name="overwrite">Whether an existing destination may be replaced.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the copy has finished.</returns>
    /// <exception cref="DestinationExistsException">
    /// The destination exists and <paramref name="overwrite"/> is <see langword="false"/>.
    /// </exception>
    Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a file, falling back to a copy and delete when the two paths are on different volumes.
    /// </summary>
    /// <param name="sourcePath">The file to move.</param>
    /// <param name="destinationPath">Where to move it.</param>
    /// <param name="overwrite">Whether an existing destination may be replaced.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the move has finished.</returns>
    /// <exception cref="DestinationExistsException">
    /// The destination exists and <paramref name="overwrite"/> is <see langword="false"/>.
    /// </exception>
    Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to create a hard link.
    /// </summary>
    /// <param name="sourcePath">The existing file.</param>
    /// <param name="linkPath">The link to create.</param>
    /// <returns>
    /// <see langword="true"/> when the link was created. A hard link across volumes, or on a file system
    /// that has no such concept, returns <see langword="false"/> rather than throwing: the caller is
    /// expected to fall back, not to fail.
    /// </returns>
    bool TryCreateHardLink(string sourcePath, string linkPath);

    /// <summary>
    /// Determines whether a file is currently held open by another process.
    /// </summary>
    /// <param name="path">The file to test.</param>
    /// <returns><see langword="true"/> when the file could not be opened for exclusive access.</returns>
    bool IsFileLocked(string path);

    /// <summary>
    /// Gets the mount a path lives on.
    /// </summary>
    /// <param name="path">The path to locate.</param>
    /// <returns>The mount, or <see langword="null"/> when it cannot be determined.</returns>
    IStorageMount? GetMount(string path);
}
