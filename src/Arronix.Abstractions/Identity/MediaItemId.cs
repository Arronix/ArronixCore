namespace Arronix.Abstractions.Identity;

/// <summary>
/// Unique identifier for a specific media item within a media kind.
/// This is a stable, internal identifier used to track media items across the system.
/// </summary>
/// <param name="Value">The surrogate identifier for the media item.</param>
/// <remarks>
/// The width is 64-bit because the column that stores it is, and a narrower runtime type in front of a
/// wider column is a truncation waiting to be discovered by the row that first exceeds it. The identifier
/// is a host-minted surrogate: the host assigns it when a catalog answers for an item, which is what makes
/// two searches for one work agree about what it will be called before anyone has added it. No provider
/// contract carries one. Values are issued from one upward, so zero and below name nothing.
/// </remarks>
public readonly record struct MediaItemId(long Value)
{
    /// <summary>
    /// Gets the string representation of this media item identifier.
    /// </summary>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Creates a MediaItemId from a numeric value.
    /// </summary>
    /// <param name="value">The surrogate identifier for the media item.</param>
    /// <returns>A new MediaItemId instance.</returns>
    public static MediaItemId FromInt64(long value) => new(value);

    /// <summary>
    /// Converts this MediaItemId to its numeric representation.
    /// </summary>
    /// <returns>The numeric value of this media item identifier.</returns>
    public long ToInt64() => Value;
}
