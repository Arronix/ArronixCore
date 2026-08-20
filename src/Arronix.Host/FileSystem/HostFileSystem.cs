using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Arronix.Abstractions.FileSystem;
using Arronix.Common.Configuration;
using Microsoft.Extensions.Options;


namespace Arronix.Host.FileSystem;

/// <summary>
/// The platform's own file system, implemented directly over the framework's file APIs.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the shared platform assembly deliberately ships no implementation of the file system
/// contract while consuming it — the hasher reads through it — so a host that registered only the shared
/// assembly could not resolve a hasher at all. The host is the right place for the implementation: it is
/// the only component that legitimately sees the whole filesystem, and everything an extension receives is
/// a scoping decorator wrapped around this one.
/// </para>
/// <para>
/// Nothing here consults an extension's granted roots. That is not an omission: confinement is the
/// decorator's job, and an unconfined implementation that the host uses for its own work is what the
/// decorator needs to wrap. Registering this type as the extension-facing file system would be the defect,
/// and the composition root is the single place that could make that mistake.
/// </para>
/// </remarks>
/// <param name="options">The platform's file system settings, for the write-probe file name.</param>
public sealed class HostFileSystem(IOptions<FileSystemOptions> options) : IFileSystem
{
    private readonly FileSystemOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.Exists(path);
    }

    /// <inheritdoc />
    public bool FolderExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.Exists(path);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Writability is proved by writing rather than by reading permissions. Permission bits, access control
    /// lists, read-only mounts and container mappings each answer the question differently and none of them
    /// answers it correctly on its own; a file that was created and removed again is proof.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A probe reports whether the folder is writable; any failure to write means it is not, and the specific exception type is not actionable to the caller.")]
    public async Task<bool> IsFolderWritableAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!Directory.Exists(path))
        {
            return false;
        }

        var probe = Path.Combine(path, _options.WriteProbeFileName);

        try
        {
            await File.WriteAllTextAsync(probe, string.Empty, cancellationToken).ConfigureAwait(false);
            File.Delete(probe);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public long? GetAvailableSpace(string path) => GetDrive(path)?.AvailableFreeSpace;

    /// <inheritdoc />
    public long? GetTotalSize(string path) => GetDrive(path)?.TotalSize;

    /// <inheritdoc />
    public long GetFileSize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileInfo(path).Length;
    }

    /// <inheritdoc />
    public DateTimeOffset GetLastWriteTimeUtc(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
    }

    /// <inheritdoc />
    public void SetLastWriteTimeUtc(string path, DateTimeOffset lastWriteTimeUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc.UtcDateTime);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string path, bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.EnumerateFiles(
            path,
            "*",
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Directory.EnumerateDirectories(path);
    }

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return File.ReadAllTextAsync(path, cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        EnsureFolder(Path.GetDirectoryName(path) ?? string.Empty);
        return File.WriteAllTextAsync(path, contents, cancellationToken);
    }

    /// <inheritdoc />
    public Stream OpenRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 0, useAsync: true);
    }

    /// <inheritdoc />
    public Stream OpenWrite(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureFolder(Path.GetDirectoryName(path) ?? string.Empty);
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 0, useAsync: true);
    }

    /// <inheritdoc />
    public async Task SaveStreamAsync(Stream source, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var destination = OpenWrite(path);
        await using (destination.ConfigureAwait(false))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void EnsureFolder(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.Delete(path);
    }

    /// <inheritdoc />
    public Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();

        GuardDestination(destinationPath, overwrite);
        EnsureFolder(Path.GetDirectoryName(destinationPath) ?? string.Empty);
        File.Copy(sourcePath, destinationPath, overwrite);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();

        GuardDestination(destinationPath, overwrite);
        EnsureFolder(Path.GetDirectoryName(destinationPath) ?? string.Empty);
        File.Move(sourcePath, destinationPath, overwrite);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Always declines, and the boolean return is why that is safe: hard links fail for entirely ordinary
    /// reasons — a different volume, a filesystem that has none, a container mapping — so every caller
    /// already has to handle a refusal, and the handling is always the same one copy.
    /// </para>
    /// <para>
    /// The framework exposes no hard-link API, so a working implementation means one platform invoke per
    /// operating system and the unsafe compilation they bring with them. This assembly is deliberately
    /// "the file system contract over the framework's file APIs" and nothing more; hard linking belongs with
    /// the platform-specific transfer service, which this milestone does not build. Recorded as a
    /// limitation rather than hidden behind an implementation that silently copies.
    /// </para>
    /// </remarks>
    public bool TryCreateHardLink(string sourcePath, string linkPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPath);
        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Determined by attempting an exclusive open, because there is no portable way to ask. The probe is
    /// read-only and closes immediately, so it cannot itself be the thing that locks the file.
    /// </remarks>
    public bool IsFileLocked(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <inheritdoc />
    public IStorageMount? GetMount(string path)
    {
        var drive = GetDrive(path);
        return drive is null ? null : new DriveStorageMount(drive);
    }

    private static void GuardDestination(string destinationPath, bool overwrite)
    {
        if (!overwrite && File.Exists(destinationPath))
        {
            throw new DestinationExistsException(destinationPath);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Resolving the volume behind a path is best-effort reporting; an unmapped, malformed or inaccessible path yields no mount rather than an error.")]
    private static DriveInfo? GetDrive(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);

            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new DriveInfo(root);
            }

            // On Unix every path shares one root, so the drive whose mount point is the longest prefix of
            // the path is the one the path actually lives on. Taking the root would report the wrong volume
            // for every mounted library folder, which is precisely the case that matters here.
            return DriveInfo.GetDrives()
                .Where(drive => full.StartsWith(drive.RootDirectory.FullName, StringComparison.Ordinal))
                .OrderByDescending(drive => drive.RootDirectory.FullName.Length)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
