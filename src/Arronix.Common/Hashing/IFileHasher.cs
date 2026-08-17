using System.IO;

namespace Arronix.Common.Hashing;

/// <summary>
/// Computes the digest of a file or a stream.
/// </summary>
/// <remarks>
/// <para>
/// The contract names a purpose, never an algorithm. A member called <c>ComputeMd5</c> would put the
/// implementation in the signature: every call site would then encode the choice, and replacing the
/// primitive would be a breaking change to all of them rather than a change to one class.
/// </para>
/// <para>
/// This interface is host-side. It is not published to extensions, because no extension has needed to hash
/// a file yet and a contract with no consumer cannot be shaped by one. It moves to the contract assembly
/// when a real consumer appears, which costs nothing; publishing it early could not be undone.
/// </para>
/// </remarks>
public interface IFileHasher
{
    /// <summary>
    /// Computes the digest of a file's contents.
    /// </summary>
    /// <param name="path">Full path of the file to read.</param>
    /// <param name="purpose">What the digest is for, which selects the algorithm.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The digest bytes. The length depends on the algorithm the purpose selects.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="path"/>.</exception>
    ValueTask<byte[]> ComputeAsync(
        string path,
        FileHashPurpose purpose = FileHashPurpose.ChangeDetection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the digest of a stream, reading it from its current position to its end.
    /// </summary>
    /// <param name="content">The stream to read. It is consumed but not disposed.</param>
    /// <param name="purpose">What the digest is for, which selects the algorithm.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The digest bytes. The length depends on the algorithm the purpose selects.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    ValueTask<byte[]> ComputeAsync(
        Stream content,
        FileHashPurpose purpose = FileHashPurpose.ChangeDetection,
        CancellationToken cancellationToken = default);
}
