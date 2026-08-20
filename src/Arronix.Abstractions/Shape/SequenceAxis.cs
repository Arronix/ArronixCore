
namespace Arronix.Abstractions.Shape;

/// <summary>
/// A named grouping run over one component of an ordinal coordinate space.
/// </summary>
/// <remarks>
/// <para>
/// The intermediate grouping every media hierarchy seems to need is two different things fused together:
/// a grouping coordinate carried on the leaf item, and a policy record — a monitored bit, artwork, a
/// folder override — attached to the parent. One surveyed application ran the fused form as a table for
/// twenty schema migrations and then removed it. This type is the first half plus a flag for the second.
/// </para>
/// <para>
/// It is <b>not</b> a level and never gets rows of its own. Declaring it lets the host offer navigation,
/// per-run monitoring and per-run naming without any level having to exist for it.
/// </para>
/// </remarks>
public sealed record SequenceAxis
{
    /// <summary>
    /// Gets the identifier acquisition scopes and browse axes reference this axis by.
    /// </summary>
    public required string AxisId { get; init; }

    /// <summary>
    /// Gets the axis's display name for one run.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the axis's display name for several runs.
    /// </summary>
    public required string PluralName { get; init; }

    /// <summary>
    /// Gets the <see cref="CoordinateSpace.SpaceId"/> this axis names a component of. The space must be
    /// of <see cref="CoordinateKind.Ordinal"/>.
    /// </summary>
    public required string SpaceId { get; init; }

    /// <summary>
    /// Gets the index into that space's <see cref="CoordinateSpace.Components"/> the axis runs over.
    /// </summary>
    public required int ComponentIndex { get; init; }

    /// <summary>
    /// Gets a value indicating whether a per-parent, per-value policy record exists — a monitored bit,
    /// artwork, a folder override. When false the axis is purely a coordinate.
    /// </summary>
    public bool HasPolicyRecord { get; init; }

    /// <summary>
    /// Gets the values outside the dense run that mean something else.
    /// </summary>
    public IReadOnlyList<SequenceException> Exceptions { get; init; } = [];
}

/// <summary>
/// A reserved value on a sequence axis that does not belong to the dense run.
/// </summary>
/// <param name="Value">The reserved coordinate component value.</param>
/// <param name="Name">What that value means.</param>
/// <param name="ExcludedFromCompleteness">Whether items at this value are counted as wanted.</param>
/// <remarks>
/// Declared rather than hard-coded. The surveyed applications test for a magic zero in naming, in
/// statistics and in monitoring, in each case independently; one declaration replaces all of them and
/// lets the host apply the rule to a kind it has never seen.
/// </remarks>
public readonly record struct SequenceException(long Value, string Name, bool ExcludedFromCompleteness);
