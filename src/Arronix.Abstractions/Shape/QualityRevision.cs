using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// The revision axis of a quality: how many times the release was re-issued, and why.
/// </summary>
/// <param name="Version">The stated re-issue number. A first issue is 1; each PROPER-style re-issue increments it.</param>
/// <param name="Real">How many times the release was re-issued because the previous issue was mislabeled rather than defective.</param>
/// <param name="IsRepack">Whether the current issue is a repack of the same encode rather than a new one.</param>
/// <remarks>
/// <para>
/// An entire upgrade axis with no surface until now: two files on the same rung differ only here, and a
/// flattened encoding silently loses the difference — a re-issue of a repack must outrank the plain
/// repack it corrects. The three members are carried separately because the surveyed sources spell them
/// separately and each participates in ordering.
/// </para>
/// <para>
/// Ordering is total and deliberate: version first, then the mislabel count, then repack-ness.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct QualityRevision(int Version, int Real, bool IsRepack)
{
    /// <summary>
    /// Gets the revision every release starts at: version one, no mislabel re-issues, not a repack.
    /// </summary>
    public static QualityRevision Initial { get; } = new(1, 0, false);

    /// <summary>
    /// Compares this revision to another: version first, then mislabel re-issues, then repack-ness.
    /// </summary>
    /// <param name="other">The revision to compare to.</param>
    /// <returns>Negative when this revision is earlier, zero when equal, positive when later.</returns>
    public int CompareTo(QualityRevision other)
    {
        var byVersion = Version.CompareTo(other.Version);
        if (byVersion != 0)
        {
            return byVersion;
        }

        var byReal = Real.CompareTo(other.Real);
        return byReal != 0 ? byReal : IsRepack.CompareTo(other.IsRepack);
    }
}
