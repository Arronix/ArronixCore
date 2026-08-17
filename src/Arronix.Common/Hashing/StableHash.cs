using System.Buffers;
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace Arronix.Common.Hashing;

/// <summary>
/// Derives a non-cryptographic identity from text or bytes that is the same on every machine, in every
/// culture and in every process.
/// </summary>
/// <remarks>
/// <para>
/// The digest is used to give something a stable synthetic identity — a cache partition key, a deduplication
/// key, a correlation id derived from a URL. It is deliberately <em>not</em> a security primitive: it is not
/// collision-resistant against an adversary who chooses the input, so nothing that authenticates, signs or
/// verifies content may use it. Where integrity actually matters, use a cryptographic hash through
/// <see cref="IFileHasher"/> with <see cref="FileHashPurpose.Integrity"/>.
/// </para>
/// <para>
/// Three properties are load-bearing and each replaces a defect in the code this replaces:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     Text is encoded as UTF-8, never with the ambient encoding. An ambient encoding makes the digest a
///     function of the machine's code page, so the same string hashes differently on two installations and
///     any persisted value derived from it stops matching after a migration.
///     </description>
///   </item>
///   <item>
///     <description>
///     The algorithm is a pure static function with no shared instance and therefore no lock. Hashing from
///     many threads is genuinely parallel rather than serialized behind one mutable algorithm object.
///     </description>
///   </item>
///   <item>
///     <description>
///     The algorithm is xxHash3, not a cryptographic digest. Using a cryptographic hash for a
///     non-cryptographic identity costs an order of magnitude in throughput and misleads a reader into
///     believing the value carries a security guarantee.
///     </description>
///   </item>
/// </list>
/// <para>
/// The digest is stable across releases: changing the algorithm would invalidate every value ever persisted
/// from it, so it is part of this type's contract rather than an implementation detail.
/// </para>
/// </remarks>
public static class StableHash
{
    /// <summary>
    /// Longest UTF-8 encoding, in bytes, that is built on the stack rather than in a pooled buffer.
    /// </summary>
    private const int StackBufferSize = 256;

    /// <summary>
    /// Computes the 64-bit digest of a string.
    /// </summary>
    /// <param name="value">The text to hash. It is encoded as UTF-8 before hashing.</param>
    /// <returns>The digest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static ulong Compute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Compute(value.AsSpan());
    }

    /// <summary>
    /// Computes the 64-bit digest of a span of text.
    /// </summary>
    /// <param name="value">The text to hash. It is encoded as UTF-8 before hashing.</param>
    /// <returns>The digest.</returns>
    /// <remarks>
    /// Unpaired surrogates encode to the Unicode replacement character rather than throwing, so a digest can
    /// be taken of a release title or a file name that arrived from the network already malformed.
    /// </remarks>
    public static ulong Compute(ReadOnlySpan<char> value)
    {
        var upperBound = Encoding.UTF8.GetMaxByteCount(value.Length);

        if (upperBound <= StackBufferSize)
        {
            Span<byte> stackBuffer = stackalloc byte[StackBufferSize];
            var stackWritten = Encoding.UTF8.GetBytes(value, stackBuffer);

            return Compute(stackBuffer[..stackWritten]);
        }

        var rented = ArrayPool<byte>.Shared.Rent(upperBound);

        try
        {
            var written = Encoding.UTF8.GetBytes(value, rented);

            return Compute(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Computes the 64-bit digest of a span of bytes.
    /// </summary>
    /// <param name="value">The bytes to hash.</param>
    /// <returns>The digest.</returns>
    public static ulong Compute(ReadOnlySpan<byte> value) => XxHash3.HashToUInt64(value);

    /// <summary>
    /// Computes a non-negative 31-bit digest of a string.
    /// </summary>
    /// <param name="value">The text to hash.</param>
    /// <returns>A value in the range 0 to <see cref="int.MaxValue"/> inclusive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The sign bit is cleared rather than the value being cast, so the result is safe to store in a column
    /// or an identifier that rejects negative numbers. Discarding a bit doubles the collision probability,
    /// which is why the 64-bit <see cref="Compute(string)"/> is preferred wherever the width is free.
    /// </remarks>
    public static int ComputeNonNegativeInt32(string value) => (int)(Compute(value) & int.MaxValue);

    /// <summary>
    /// Computes the digest of a string and renders it as sixteen lowercase hexadecimal characters.
    /// </summary>
    /// <param name="value">The text to hash.</param>
    /// <returns>A fixed-width, lowercase, culture-independent token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The width is fixed and the alphabet is restricted to characters that are legal in a file name, a URL
    /// path segment and a header token, so the token can be used as a key in any of the three without
    /// further escaping.
    /// </remarks>
    public static string ComputeToken(string value) =>
        Compute(value).ToString("x16", CultureInfo.InvariantCulture);
}
