using System.IO;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Plugins;


namespace Arronix.Plugins.Scoping;

/// <summary>
/// Confines an extension's file access to the roots it was granted.
/// </summary>
/// <remarks>
/// <para>
/// The file-system contract already documents this behavior as guaranteed. Until now nothing delivered it,
/// which meant an extension holding the storage privilege held it over the whole machine. This decorator is
/// where the promise becomes true.
/// </para>
/// <para>
/// Confinement is checked on the fully-resolved path, so a relative segment cannot walk out of a root, and
/// a root is only matched at a directory boundary, so a grant over <c>/media/library</c> does not
/// accidentally grant <c>/media/library-backup</c>. A path outside every root is refused regardless of what
/// the operating system's own permissions would have allowed — the platform's answer does not depend on how
/// the host process happens to be running.
/// </para>
/// <para>
/// The refusal is the framework's own unauthorized-access failure rather than a platform-specific one. A
/// caller that already handles the operating system refusing it handles this identically, which is the
/// behavior a decorator should have.
/// </para>
/// </remarks>
public sealed class ScopedFileSystem : IFileSystem
{
    private readonly IFileSystem _inner;
    private readonly string[] _roots;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopedFileSystem"/> class.
    /// </summary>
    /// <param name="inner">The unconfined file system.</param>
    /// <param name="plugin">The extension being confined.</param>
    /// <param name="grantedRoots">
    /// The folders the extension may touch. An empty grant confines it to nothing, which is the correct
    /// default for a privilege nobody configured.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> or <paramref name="grantedRoots"/> is <see langword="null"/>.</exception>
    public ScopedFileSystem(IFileSystem inner, PluginId plugin, IReadOnlyList<string> grantedRoots)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(grantedRoots);

        _inner = inner;
        Plugin = plugin;

        var roots = new List<string>(grantedRoots.Count);
        foreach (var root in grantedRoots)
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                roots.Add(Normalize(Path.GetFullPath(root)));
            }
        }

        _roots = [.. roots];
    }

    /// <summary>
    /// Gets the extension being confined.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Gets the folders the extension may touch.
    /// </summary>
    public IReadOnlyList<string> GrantedRoots => _roots;

    /// <summary>
    /// Determines whether a path is inside a granted root.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the path is inside one of the roots.</returns>
    public bool IsWithinGrant(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string full;
        try
        {
            full = Normalize(Path.GetFullPath(path));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var root in _roots)
        {
            if (full.Equals(root, comparison))
            {
                return true;
            }

            // The separator is required so that a grant over a folder never leaks to a sibling whose name
            // merely starts with the same characters.
            if (full.Length > root.Length
                && full.StartsWith(root, comparison)
                && full[root.Length] == Path.DirectorySeparatorChar)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool FileExists(string path) => _inner.FileExists(Guard(path));

    /// <inheritdoc />
    public bool FolderExists(string path) => _inner.FolderExists(Guard(path));

    /// <inheritdoc />
    public Task<bool> IsFolderWritableAsync(string path, CancellationToken cancellationToken = default)
        => _inner.IsFolderWritableAsync(Guard(path), cancellationToken);

    /// <inheritdoc />
    public long? GetAvailableSpace(string path) => _inner.GetAvailableSpace(Guard(path));

    /// <inheritdoc />
    public long? GetTotalSize(string path) => _inner.GetTotalSize(Guard(path));

    /// <inheritdoc />
    public long GetFileSize(string path) => _inner.GetFileSize(Guard(path));

    /// <inheritdoc />
    public DateTimeOffset GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(Guard(path));

    /// <inheritdoc />
    public void SetLastWriteTimeUtc(string path, DateTimeOffset lastWriteTimeUtc)
        => _inner.SetLastWriteTimeUtc(Guard(path), lastWriteTimeUtc);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string path, bool recursive = false)
        => _inner.EnumerateFiles(Guard(path), recursive);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string path) => _inner.EnumerateDirectories(Guard(path));

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => _inner.ReadAllTextAsync(Guard(path), cancellationToken);

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
        => _inner.WriteAllTextAsync(Guard(path), contents, cancellationToken);

    /// <inheritdoc />
    public Stream OpenRead(string path) => _inner.OpenRead(Guard(path));

    /// <inheritdoc />
    public Stream OpenWrite(string path) => _inner.OpenWrite(Guard(path));

    /// <inheritdoc />
    public Task SaveStreamAsync(Stream source, string path, CancellationToken cancellationToken = default)
        => _inner.SaveStreamAsync(source, Guard(path), cancellationToken);

    /// <inheritdoc />
    public void EnsureFolder(string path) => _inner.EnsureFolder(Guard(path));

    /// <inheritdoc />
    public void DeleteFile(string path) => _inner.DeleteFile(Guard(path));

    /// <inheritdoc />
    public Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
        => _inner.CopyFileAsync(Guard(sourcePath), Guard(destinationPath), overwrite, cancellationToken);

    /// <inheritdoc />
    public Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
        => _inner.MoveFileAsync(Guard(sourcePath), Guard(destinationPath), overwrite, cancellationToken);

    /// <inheritdoc />
    public bool TryCreateHardLink(string sourcePath, string linkPath)
        => _inner.TryCreateHardLink(Guard(sourcePath), Guard(linkPath));

    /// <inheritdoc />
    public bool IsFileLocked(string path) => _inner.IsFileLocked(Guard(path));

    /// <inheritdoc />
    public IStorageMount? GetMount(string path) => _inner.GetMount(Guard(path));

    private static string Normalize(string path)
        => path.Length > 1 && path.EndsWith(Path.DirectorySeparatorChar)
            ? path.TrimEnd(Path.DirectorySeparatorChar)
            : path;

    private string Guard(string path)
    {
        if (IsWithinGrant(path))
        {
            return path;
        }

        throw new UnauthorizedAccessException(
            $"Extension '{Plugin}' may not access '{path}'. Extensions are confined to the folders granted to them.");
    }
}
