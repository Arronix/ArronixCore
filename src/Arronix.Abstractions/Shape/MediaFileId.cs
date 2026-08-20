using System.Globalization;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Identifies a file the platform has taken responsibility for.
/// </summary>
/// <param name="Value">The surrogate identifier, minted by the host.</param>
/// <remarks>
/// <para>
/// Files are host-owned — the host discovers, moves, hashes and links them — so this is an opaque
/// surrogate rather than a path or a hash. A path changes on rename and a hash changes on repair, and
/// either would break every link that referenced it.
/// </para>
/// <para>
/// Sixty-four bits, matching the column that stores it and matching
/// <see cref="Arronix.Abstractions.Identity.MediaItemId"/>. A library that scans a large tree repeatedly
/// mints file rows faster than it mints item rows, so this is the identifier with the least headroom to
/// spare, not the most.
/// </para>
/// </remarks>
public readonly record struct MediaFileId(long Value)
{
    /// <summary>
    /// Creates a file identifier.
    /// </summary>
    /// <param name="value">The surrogate identifier.</param>
    /// <returns>The identifier.</returns>
    public static MediaFileId FromInt64(long value) => new(value);

    /// <summary>
    /// Gets the identifier's decimal form.
    /// </summary>
    /// <returns>The identifier text.</returns>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
