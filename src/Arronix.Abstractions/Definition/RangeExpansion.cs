
namespace Arronix.Abstractions.Definition;

/// <summary>
/// How a pattern's range match fans out into units.
/// </summary>
/// <remarks>
/// A run written as first-through-last becomes either one span reading or one reading per member. The
/// span cap is carried as data because the surveyed parsers cap it deliberately: an absurd range is a
/// mis-parse, not a very large release.
/// </remarks>
public sealed record RangeExpansion
{
    /// <summary>
    /// Gets the capture group holding the first member of the range.
    /// </summary>
    public required string FromGroup { get; init; }

    /// <summary>
    /// Gets the capture group holding the last member of the range.
    /// </summary>
    public required string ToGroup { get; init; }

    /// <summary>
    /// Gets the widest range accepted before the match is refused as a mis-parse.
    /// </summary>
    public int MaxSpan { get; init; } = 25;

    /// <summary>
    /// Gets a value indicating whether the range emits one span reading rather than one reading per
    /// member.
    /// </summary>
    public bool EmitAsSpan { get; init; }
}
