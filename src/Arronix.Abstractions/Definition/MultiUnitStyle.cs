
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One style of writing several units in one name.
/// </summary>
/// <remarks>
/// The surveyed style set is a handful of data rows over four properties: what joins consecutive
/// ordinals, whether each repeats its prefix, whether a run collapses to first-through-last, and whether
/// the outer coordinate is restated per unit.
/// </remarks>
public sealed record MultiUnitStyle
{
    /// <summary>
    /// Gets the style's identifier, referenced by templates and settings.
    /// </summary>
    public required string StyleId { get; init; }

    /// <summary>
    /// Gets the text joining consecutive ordinals.
    /// </summary>
    public required string Joiner { get; init; }

    /// <summary>
    /// Gets the prefix each repeated ordinal restates, when each does.
    /// </summary>
    public string? RepeatPrefix { get; init; }

    /// <summary>
    /// Gets a value indicating whether a contiguous run collapses to its first and last ordinals.
    /// </summary>
    public bool RangeOnly { get; init; }

    /// <summary>
    /// Gets a value indicating whether the outer coordinate is restated for every unit rather than
    /// stated once.
    /// </summary>
    public bool RestateOuter { get; init; }
}
