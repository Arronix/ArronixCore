namespace Arronix.Abstractions.Identity;

/// <summary>
/// Unique identifier for a media kind/type (e.g., "tv", "movies", "music").
/// Media kinds are defined by plugins and represent different types of media content.
/// </summary>
/// <param name="Value">The string identifier for the media kind.</param>
public readonly record struct MediaKindId(string Value)
{
    /// <summary>
    /// Gets the string representation of this media kind identifier.
    /// </summary>
    public override string ToString() => Value;

    /// <summary>
    /// Creates a MediaKindId from a string value.
    /// </summary>
    /// <param name="value">The string identifier for the media kind.</param>
    /// <returns>A new MediaKindId instance.</returns>
    public static MediaKindId FromString(string value) => new(value);

    /// <summary>
    /// Converts this MediaKindId to its string representation.
    /// </summary>
    /// <returns>The string value of this media kind identifier.</returns>
    public string ToMediaKindId() => Value;

    /// <summary>
    /// Implicitly converts a string to a MediaKindId.
    /// </summary>
    public static implicit operator string(MediaKindId id) => id.Value;

    /// <summary>
    /// Implicitly converts a MediaKindId to a string.
    /// </summary>
    public static implicit operator MediaKindId(string value) => new(value);
}
