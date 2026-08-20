using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.FileSystem;


namespace Arronix.Common.Tests.Hashing;

/// <summary>
/// A file system that only knows how to open files for reading, backed by a dictionary.
/// </summary>
/// <remarks>
/// The hasher uses exactly one member of the contract. Implementing the rest as refusals keeps the test's
/// intent visible: if the hasher ever starts calling something else, the test fails loudly rather than
/// silently exercising a second code path.
/// </remarks>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public void Add(string path, byte[] content) => _files[path] = content;

    public Stream OpenRead(string path) =>
        _files.TryGetValue(path, out var content)
            ? new MemoryStream(content, writable: false)
            : throw new FileNotFoundException($"No file at '{path}'.", path);

    public bool FileExists(string path) => _files.ContainsKey(path);

    public bool FolderExists(string path) => throw new NotSupportedException();

    public Task<bool> IsFolderWritableAsync(string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public long? GetAvailableSpace(string path) => throw new NotSupportedException();

    public long? GetTotalSize(string path) => throw new NotSupportedException();

    public long GetFileSize(string path) => throw new NotSupportedException();

    public DateTimeOffset GetLastWriteTimeUtc(string path) => throw new NotSupportedException();

    public void SetLastWriteTimeUtc(string path, DateTimeOffset lastWriteTimeUtc) =>
        throw new NotSupportedException();

    public IEnumerable<string> EnumerateFiles(string path, bool recursive = false) =>
        throw new NotSupportedException();

    public IEnumerable<string> EnumerateDirectories(string path) => throw new NotSupportedException();

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Stream OpenWrite(string path) => throw new NotSupportedException();

    public Task SaveStreamAsync(Stream source, string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void EnsureFolder(string path) => throw new NotSupportedException();

    public void DeleteFile(string path) => throw new NotSupportedException();

    public Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public bool TryCreateHardLink(string sourcePath, string linkPath) => throw new NotSupportedException();

    public bool IsFileLocked(string path) => throw new NotSupportedException();

    public IStorageMount? GetMount(string path) => throw new NotSupportedException();
}
