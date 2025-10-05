namespace Arronix.Abstractions.Identity;

/// <summary>
/// Unique identifier for a release candidate (e.g., a torrent or NZB).
/// This represents a specific downloadable artifact that may correspond to media content.
/// </summary>
/// <param name="Value">The string identifier for the release (typically a GUID or hash).</param>
public readonly record struct ReleaseId(string Value)
{
    /// <summary>
    /// Gets the string representation of this release identifier.
    /// </summary>
    public override string ToString() => Value;

    /// <summary>
    /// Creates a ReleaseId from a string value.
    /// </summary>
    /// <param name="value">The string identifier for the release.</param>
    /// <returns>A new ReleaseId instance.</returns>
    public static ReleaseId FromString(string value) => new(value);

    /// <summary>
    /// Converts this ReleaseId to its string representation.
    /// </summary>
    /// <returns>The string value of this release identifier.</returns>
    public string ToReleaseId() => Value;

    /// <summary>
    /// Implicitly converts a string to a ReleaseId.
    /// </summary>
    public static implicit operator string(ReleaseId id) => id.Value;

    /// <summary>
    /// Implicitly converts a ReleaseId to a string.
    /// </summary>
    public static implicit operator ReleaseId(string value) => new(value);
}
