using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;
using Arronix.Abstractions.FileSystem;


namespace Arronix.Common.Hashing;

/// <summary>
/// Reads a file through the platform's file system and hashes its contents.
/// </summary>
/// <remarks>
/// <para>
/// Reading goes through the file system contract rather than the framework's file APIs so that a hash taken
/// of a path inside an extension's granted roots is subject to the same scoping as every other read that
/// extension makes. A hasher that opened files itself would be a way around that.
/// </para>
/// <para>
/// Both algorithms consume the stream incrementally, so hashing a multi-gigabyte file costs a fixed and
/// small amount of memory. The read is asynchronous throughout: hashing a large file was previously enough
/// to hold a pool thread for the whole of it.
/// </para>
/// </remarks>
/// <param name="fileSystem">The file system the contents are read through.</param>
public sealed class FileHasher(IFileSystem fileSystem) : IFileHasher
{
    private readonly IFileSystem _fileSystem = fileSystem;

    /// <inheritdoc />
    public ValueTask<byte[]> ComputeAsync(
        string path,
        FileHashPurpose purpose = FileHashPurpose.ChangeDetection,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return ComputeFileAsync(path, purpose, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<byte[]> ComputeAsync(
        Stream content,
        FileHashPurpose purpose = FileHashPurpose.ChangeDetection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        return purpose switch
        {
            FileHashPurpose.ChangeDetection => ComputeFastAsync(content, cancellationToken),
            FileHashPurpose.Integrity => ComputeCryptographicAsync(content, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(purpose),
                purpose,
                "The hash purpose is not one this platform knows how to satisfy."),
        };
    }

    private async ValueTask<byte[]> ComputeFileAsync(
        string path,
        FileHashPurpose purpose,
        CancellationToken cancellationToken)
    {
        var stream = _fileSystem.OpenRead(path);

        await using (stream.ConfigureAwait(false))
        {
            return await ComputeAsync(stream, purpose, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<byte[]> ComputeFastAsync(Stream content, CancellationToken cancellationToken)
    {
        var hash = new XxHash128();

        await hash.AppendAsync(content, cancellationToken).ConfigureAwait(false);

        return hash.GetCurrentHash();
    }

    private static async ValueTask<byte[]> ComputeCryptographicAsync(
        Stream content,
        CancellationToken cancellationToken) =>
        await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
}
