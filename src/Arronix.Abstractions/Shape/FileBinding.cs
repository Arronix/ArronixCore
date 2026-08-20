
namespace Arronix.Abstractions.Shape;

/// <summary>
/// Declares how files relate to the items they satisfy.
/// </summary>
/// <remarks>
/// <para>
/// The storage representation is always the join <c>(item, file, ordinal?)</c>; the booleans below are
/// uniqueness constraints the host enforces over that join, never a switch between schemas. Every
/// surveyed application is a degenerate projection of the triple, and adopting any one of their
/// relationship directions as the platform default would misrepresent the other two.
/// </para>
/// <para>
/// <see cref="AnchorLevelId"/> and <see cref="UnitLevelId"/> differ in exactly one surveyed kind, and not
/// by accident: the file row hangs at the acquisition level while the items it satisfies sit two levels
/// below, so file ownership survives a change of selected variant. A single file-bearing level cannot
/// express that.
/// </para>
/// </remarks>
public sealed record FileBinding
{
    /// <summary>
    /// Gets the level the file record hangs from.
    /// </summary>
    public required MediaLevelId AnchorLevelId { get; init; }

    /// <summary>
    /// Gets the level whose items the file satisfies. A descendant of, or the same as,
    /// <see cref="AnchorLevelId"/>.
    /// </summary>
    public required MediaLevelId UnitLevelId { get; init; }

    /// <summary>
    /// Gets a value indicating whether an item may be satisfied by at most one file.
    /// </summary>
    public bool AtMostOneFilePerUnit { get; init; }

    /// <summary>
    /// Gets a value indicating whether a file may satisfy at most one item.
    /// </summary>
    public bool AtMostOneUnitPerFile { get; init; }

    /// <summary>
    /// Gets a value indicating whether the ordinal on a link is meaningful — that is, whether the files
    /// satisfying one item are an ordered sequence rather than a set. Requires
    /// <see cref="AtMostOneFilePerUnit"/> to be <see langword="false"/>.
    /// </summary>
    public bool OrdinalIsMeaningful { get; init; }

    /// <summary>
    /// Gets the coordinate components a single file may or may not straddle.
    /// </summary>
    public IReadOnlyList<SpanConstraint> SpanConstraints { get; init; } = [];
}

/// <summary>
/// Declares whether one file may straddle two values of a coordinate component.
/// </summary>
/// <param name="SpaceId">The <see cref="CoordinateSpace.SpaceId"/> the component belongs to.</param>
/// <param name="ComponentId">The <see cref="CoordinateComponent.ComponentId"/> constrained.</param>
/// <param name="Rule">Whether straddling is permitted.</param>
/// <remarks>
/// One surveyed application throws a dedicated exception for exactly this case: a file may cover several
/// leaf items but never cross the grouping run above them. Declared rather than thrown, the host enforces
/// it for any kind without knowing what the run means.
/// </remarks>
public readonly record struct SpanConstraint(string SpaceId, string ComponentId, SpanRule Rule);

/// <summary>
/// Whether a file may straddle two values of a coordinate component.
/// </summary>
public enum SpanRule
{
    /// <summary>A single file must not cover items with different values for the component.</summary>
    MustNotSpan = 0,

    /// <summary>A single file may cover items with different values for the component.</summary>
    MaySpan = 1
}
