namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Represents a quality tier for media content.
/// Defines source, encoding, and resolution characteristics.
/// </summary>
public record QualityTier(
    string Name,
    int Rank,
    string? Source = null,
    string? Resolution = null,
    string? Codec = null,
    int? BitDepth = null,
    IReadOnlyDictionary<string, string>? AdditionalAttributes = null)
{
    /// <summary>
    /// Compares this quality tier to another based on rank.
    /// </summary>
    /// <param name="other">The other quality tier to compare to.</param>
    /// <returns>
    /// A negative value if this tier is lower quality,
    /// zero if equal,
    /// a positive value if this tier is higher quality.
    /// </returns>
    public int CompareTo(QualityTier other) => Rank.CompareTo(other.Rank);
}
