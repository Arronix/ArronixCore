using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Arronix.Common.Archives;

/// <summary>
/// Packs and unpacks archives using the framework's own compression and tar support.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every entry's destination is resolved and checked before anything is written.</strong> An archive
/// is untrusted input — it arrives over the network as an update or an extension package — and an entry name
/// is just a string chosen by whoever built it. An entry called <c>../../etc/something</c> or
/// <c>C:\Windows\something</c> combines into a path outside the folder being extracted to, which turns
/// "unpack this download" into "let the download overwrite any file this process can write". The check is
/// made here, explicitly, rather than left as an implicit property of whichever extraction helper is called,
/// so that it is visible to a reviewer and provable by a test.
/// </para>
/// <para>
/// Entries that are links, devices or pipes are skipped rather than recreated. A symbolic link inside an
/// archive is the same escape by another route — the link is created inside the destination but points
/// anywhere, and the next write through it lands outside — and nothing the host extracts has ever needed
/// one.
/// </para>
/// <para>
/// This replaces a third-party compression library. Beyond removing a dependency, the framework's zip
/// extraction and this type's own resolution check together close a traversal defect the previous
/// implementation had: it combined the entry name with the destination and wrote, with no check at all.
/// </para>
/// </remarks>
public sealed partial class ArchiveService : IArchiveService
{
    /// <summary>
    /// Copy buffer size, matching the framework's own default for stream copies.
    /// </summary>
    private const int CopyBufferSize = 81920;

    /// <summary>
    /// The zip format stores timestamps in the MS-DOS encoding, which cannot represent anything earlier
    /// than this. A file older than that is stored without a timestamp rather than failing the backup.
    /// </summary>
    private static readonly DateTimeOffset EarliestStorableTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ILogger<ArchiveService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveService"/> class.
    /// </summary>
    /// <param name="logger">Receives the progress and the skipped-entry reports.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public ArchiveService(ILogger<ArchiveService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <summary>
    /// The archive layouts this service reads, as distinguished by the file extension.
    /// </summary>
    private enum ArchiveKind
    {
        Zip,
        Tar,
        GzippedTar,
    }

    /// <inheritdoc />
    public async Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);

        var kind = DetectKind(archivePath);

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"No archive exists at '{archivePath}'.", archivePath);
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationFolder));
        Directory.CreateDirectory(root);

        LogExtracting(archivePath, root);

        if (kind == ArchiveKind.Zip)
        {
            await ExtractZipAsync(archivePath, root, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ExtractTarAsync(archivePath, root, kind, cancellationToken).ConfigureAwait(false);
        }

        LogExtracted(archivePath);
    }

    /// <inheritdoc />
    public async Task CreateZipAsync(
        string archivePath,
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(filePaths);

        var fullPath = Path.GetFullPath(archivePath);
        var folder = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        LogCreating(fullPath);

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var file = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            useAsync: true);

        await using (file.ConfigureAwait(false))
        {
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);

            foreach (var path in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await AddEntryAsync(archive, path, claimed, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static ArchiveKind DetectKind(string archivePath)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveKind.Zip;
        }

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveKind.GzippedTar;
        }

        if (archivePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            return ArchiveKind.Tar;
        }

        throw new NotSupportedException(
            $"'{Path.GetFileName(archivePath)}' is not an archive this platform reads. Supported extensions are .zip, .tar, .tar.gz and .tgz.");
    }

    /// <summary>
    /// Resolves where an entry would be written and refuses any entry that would land outside the
    /// destination.
    /// </summary>
    /// <param name="root">The destination folder, already fully resolved and without a trailing separator.</param>
    /// <param name="entryName">The entry's name as recorded in the archive.</param>
    /// <returns>The full path the entry may be written to.</returns>
    /// <exception cref="IOException">The entry has no name, or it resolves outside <paramref name="root"/>.</exception>
    private static string ResolveEntryPath(string root, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            throw new IOException("The archive holds an entry with no name.");
        }

        // Archives written on Windows sometimes record a backslash as the separator. Normalizing it first
        // means a traversal spelled with backslashes is resolved — and therefore caught — on every platform,
        // rather than being treated as a single oddly named file on one of them.
        var resolved = Path.GetFullPath(Path.Combine(root, entryName.Replace('\\', '/')));

        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new IOException(
                $"The archive holds an entry named '{entryName}', which resolves outside the destination folder. The archive was not extracted.");
        }

        return resolved;
    }

    private static async Task AddEntryAsync(
        ZipArchive archive,
        string path,
        HashSet<string> claimed,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var name = Path.GetFileName(path);

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException($"'{path}' names no file to store.", nameof(path));
        }

        if (!claimed.Add(name))
        {
            throw new IOException(
                $"Two of the supplied files are both named '{name}'; a zip entry name can only be used once.");
        }

        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);

        if (lastWrite >= EarliestStorableTimestamp)
        {
            entry.LastWriteTime = lastWrite;
        }

        var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            useAsync: true);

        await using (source.ConfigureAwait(false))
        {
            var target = entry.Open();

            await using (target.ConfigureAwait(false))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteEntryAsync(Stream source, string target, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(target);

        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var destination = new FileStream(
            target,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            useAsync: true);

        await using (destination.ConfigureAwait(false))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ExtractZipAsync(string archivePath, string root, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        // Every destination is resolved before the first byte is written, so an archive carrying a traversal
        // entry leaves nothing at all behind rather than a half-unpacked folder.
        var targets = new List<(ZipArchiveEntry Entry, string Target)>(archive.Entries.Count);

        foreach (var entry in archive.Entries)
        {
            targets.Add((entry, ResolveEntryPath(root, entry.FullName)));
        }

        foreach (var (entry, target) in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A directory entry is recorded as a name ending in the separator, which leaves an empty
            // file-name part.
            if (entry.Name.Length == 0)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            var content = entry.Open();

            await using (content.ConfigureAwait(false))
            {
                await WriteEntryAsync(content, target, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ExtractTarAsync(
        string archivePath,
        string root,
        ArchiveKind kind,
        CancellationToken cancellationToken)
    {
        var file = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            useAsync: true);

        await using (file.ConfigureAwait(false))
        {
            if (kind == ArchiveKind.Tar)
            {
                await ReadTarAsync(file, root, cancellationToken).ConfigureAwait(false);
                return;
            }

            var decompressed = new GZipStream(file, CompressionMode.Decompress);

            await using (decompressed.ConfigureAwait(false))
            {
                await ReadTarAsync(decompressed, root, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadTarAsync(Stream content, string root, CancellationToken cancellationToken)
    {
        var reader = new TarReader(content);

        await using (reader.ConfigureAwait(false))
        {
            while (await reader.GetNextEntryAsync(copyData: false, cancellationToken).ConfigureAwait(false)
                is { } entry)
            {
                var target = ResolveEntryPath(root, entry.Name);

                if (entry.EntryType == TarEntryType.Directory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                {
                    LogSkippedEntry(entry.Name, entry.EntryType.ToString());
                    continue;
                }

                // A zero-length file is recorded with no data stream at all. It is still a file the archive
                // asked for, so it is created empty rather than skipped.
                var data = entry.DataStream ?? Stream.Null;

                await WriteEntryAsync(data, target, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Extracting archive {ArchivePath} into {DestinationFolder}.")]
    private partial void LogExtracting(string archivePath, string destinationFolder);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Extracted archive {ArchivePath}.")]
    private partial void LogExtracted(string archivePath);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Creating archive {ArchivePath}.")]
    private partial void LogCreating(string archivePath);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Skipped archive entry {EntryName}: an entry of type {EntryType} is not extracted.")]
    private partial void LogSkippedEntry(string entryName, string entryType);
}
