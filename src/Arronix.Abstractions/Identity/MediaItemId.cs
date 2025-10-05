namespace Arronix.Abstractions.Identity;

/// <summary>
/// Unique identifier for a specific media item within a media kind.
/// This is a stable, internal identifier used to track media items across the system.
/// </summary>
/// <param name="Value">The integer identifier for the media item.</param>
public readonly record struct MediaItemId(int Value)
{
    /// <summary>
    /// Gets the string representation of this media item identifier.
    /// </summary>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Creates a MediaItemId from an integer value.
    /// </summary>
    /// <param name="value">The integer identifier for the media item.</param>
    /// <returns>A new MediaItemId instance.</returns>
    public static MediaItemId FromInt(int value) => new(value);

    /// <summary>
    /// Converts this MediaItemId to its integer representation.
    /// </summary>
    /// <returns>The integer value of this media item identifier.</returns>
    public int ToMediaItemId() => Value;

    /// <summary>
    /// Implicitly converts an integer to a MediaItemId.
    /// </summary>
    public static implicit operator int(MediaItemId id) => id.Value;

    /// <summary>
    /// Implicitly converts a MediaItemId to an integer.
    /// </summary>
    public static implicit operator MediaItemId(int value) => new(value);
}
