using System.IO;

namespace Arronix.Common.Archives;

/// <summary>
/// Packs and unpacks the archive formats the host itself handles: backups it writes, update packages it
/// downloads and signed extension packages it installs.
/// </summary>
/// <remarks>
/// <para>
/// This is host infrastructure with no extension audience. Extraction writes to arbitrary paths chosen by
/// whoever produced the archive, which is precisely the authority the capability model exists to withhold,
/// so the service is never handed to an extension.
/// </para>
/// <para>
/// Every operation is asynchronous and cancellable. Archives are routinely hundreds of megabytes and the
/// work is I/O all the way down; doing it synchronously holds a pool thread for the whole of an operation
/// that spends almost all its time waiting.
/// </para>
/// </remarks>
public interface IArchiveService
{
    /// <summary>
    /// Unpacks an archive into a folder.
    /// </summary>
    /// <param name="archivePath">Full path of the archive to read.</param>
    /// <param name="destinationFolder">
    /// Folder the contents are written to. It is created if it does not exist.
    /// </param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    /// <returns>A task that completes when every entry has been written.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="archivePath"/> or <paramref name="destinationFolder"/> is empty or whitespace.
    /// </exception>
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="archivePath"/>.</exception>
    /// <exception cref="NotSupportedException">
    /// The archive's extension is not one this service reads.
    /// </exception>
    /// <exception cref="InvalidDataException">The archive is damaged or is not the format it claims.</exception>
    /// <exception cref="IOException">
    /// The archive holds an entry whose path would place it outside
    /// <paramref name="destinationFolder"/>, or the write failed.
    /// </exception>
    /// <remarks>
    /// The format is chosen by the file extension: <c>.zip</c>, <c>.tar</c>, <c>.tar.gz</c> and <c>.tgz</c>.
    /// An unrecognized extension is refused rather than guessed at, so a file that is not the archive it was
    /// expected to be fails where it was downloaded instead of part-way through being unpacked over a
    /// running installation.
    /// </remarks>
    Task ExtractAsync(string archivePath, string destinationFolder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a set of files into a new zip archive.
    /// </summary>
    /// <param name="archivePath">
    /// Full path of the archive to create. An existing file at that path is replaced.
    /// </param>
    /// <param name="filePaths">
    /// Full paths of the files to include. Each is stored under its own file name, with no directory part.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the archive has been written and closed.</returns>
    /// <exception cref="ArgumentException"><paramref name="archivePath"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="filePaths"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">
    /// Two of the supplied files share a name, so one would be stored under an entry name that is already
    /// taken.
    /// </exception>
    Task CreateZipAsync(
        string archivePath,
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default);
}
