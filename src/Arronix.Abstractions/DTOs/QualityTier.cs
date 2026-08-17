// The revision axis is an experimental shape contract until 1.0.
#pragma warning disable ARX0013

using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.DTOs;

/// <summary>
/// Represents a quality tier for media content.
/// Defines source, encoding, and resolution characteristics.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Rank"/> is the tier's distinct position in its ladder and orders the ladder;
/// <see cref="Weight"/> is its semantic value and orders upgrades and cutoffs. Two tiers with equal
/// weight form a quality group: neither is an upgrade over the other, and either satisfies a cutoff set
/// at the other. When no weight is declared it defaults to the rank, which preserves the old behavior
/// exactly.
/// </para>
/// <para>
/// Size limits are stated per minute of runtime, because a plausible size scales with duration and
/// nothing else the platform reliably knows. A null maximum means no ceiling.
/// </para>
/// </remarks>
public record QualityTier(
    string Name,
    int Rank,
    string? Source = null,
    string? Resolution = null,
    string? Codec = null,
    int? BitDepth = null,
    IReadOnlyDictionary<string, string>? AdditionalAttributes = null,
    int? Weight = null,
    string? GroupName = null,
    QualityRevision? Revision = null,
    double? MinSizeMbPerMinute = null,
    double? MaxSizeMbPerMinute = null,
    double? PreferredSizeMbPerMinute = null)
{
    /// <summary>
    /// Gets the semantic weight upgrades and cutoffs compare on: the declared <see cref="Weight"/>, or
    /// the <see cref="Rank"/> when none was declared.
    /// </summary>
    public int EffectiveWeight => Weight ?? Rank;

    /// <summary>
    /// Compares this quality tier to another based on effective weight, so tiers in one quality group
    /// compare equal.
    /// </summary>
    /// <param name="other">The other quality tier to compare to.</param>
    /// <returns>
    /// A negative value if this tier is lower quality,
    /// zero if equal,
    /// a positive value if this tier is higher quality.
    /// </returns>
    public int CompareTo(QualityTier other) => EffectiveWeight.CompareTo(other.EffectiveWeight);
}
