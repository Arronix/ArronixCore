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
    public override string ToString() => Value.ToString();

    /// <summary>
    /// Implicitly converts an integer to a MediaItemId.
    /// </summary>
    public static implicit operator int(MediaItemId id) => id.Value;

    /// <summary>
    /// Implicitly converts a MediaItemId to an integer.
    /// </summary>
    public static implicit operator MediaItemId(int value) => new(value);
}
