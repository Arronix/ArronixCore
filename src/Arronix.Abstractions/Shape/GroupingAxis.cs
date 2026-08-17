using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// A collection that cuts across the containment hierarchy rather than nesting inside it.
/// </summary>
/// <remarks>
/// Groupings are richer than the hierarchy suggests: the surveyed forms include a true many-to-many link
/// whose position is a <i>string</i> because half the real values are not numbers, a designated primary
/// member because single-valued naming needs one, reference-counted lifetime with an orphan sweep, its own
/// monitoring and profiles, and its own metadata fetch path. All of that is declared here so the host can
/// offer it generically instead of a kind re-implementing it.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record GroupingAxis
{
    /// <summary>
    /// Gets the identifier browse axes and stored membership reference this axis by.
    /// </summary>
    public required string AxisId { get; init; }

    /// <summary>
    /// Gets the axis's display name for one group.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the axis's display name for several groups.
    /// </summary>
    public required string PluralName { get; init; }

    /// <summary>
    /// Gets the level whose items are members of a group.
    /// </summary>
    public required MediaLevelId MemberLevelId { get; init; }

    /// <summary>
    /// Gets whether an item may belong to several groups or to at most one.
    /// </summary>
    public required GroupingArity Arity { get; init; }

    /// <summary>
    /// Gets whether membership carries a position, and of what shape.
    /// </summary>
    public required MemberPosition Position { get; init; }

    /// <summary>
    /// Gets a value indicating whether one member may be designated primary. Requires
    /// <see cref="GroupingArity.ManyToMany"/>, since with at most one group per item the primary group is
    /// the only group.
    /// </summary>
    public bool HasPrimaryMember { get; init; }

    /// <summary>
    /// Gets a value indicating whether the group itself carries monitoring and profile state.
    /// </summary>
    public bool IsMonitorable { get; init; }

    /// <summary>
    /// Gets a value indicating whether the group can act as a source of items to add.
    /// </summary>
    public bool IsDiscoverySource { get; init; }

    /// <summary>
    /// Gets whether the group outlives its members.
    /// </summary>
    public required GroupLifetime Lifetime { get; init; }

    /// <summary>
    /// Gets a value indicating whether the group has its own metadata fetch path rather than being
    /// assembled from its members.
    /// </summary>
    public bool HasOwnMetadata { get; init; }

    /// <summary>
    /// Gets the fields a group on this axis carries. A group with its own metadata is a record, not a
    /// label, and its record needs a schema the host can read — a group whose fields live in a private
    /// naming convention cannot be rendered, matched on or refreshed generically. Grouping axes are also
    /// match inputs: an entry-resolution key template may range over these fields.
    /// </summary>
    public IReadOnlyList<FieldDescriptor> Fields { get; init; } = [];

    /// <summary>
    /// Gets the external catalogs that assign identifiers to groups on this axis.
    /// </summary>
    public IReadOnlyList<ExternalIdScheme> ExternalIds { get; init; } = [];
}

/// <summary>
/// How many groups on one axis an item may belong to.
/// </summary>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum GroupingArity
{
    /// <summary>An item may belong to several groups.</summary>
    ManyToMany = 0,

    /// <summary>An item belongs to at most one group.</summary>
    ManyToOne = 1
}

/// <summary>
/// The shape of a member's position within its group.
/// </summary>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum MemberPosition
{
    /// <summary>Membership carries no position; the group is a set.</summary>
    None = 0,

    /// <summary>Membership carries a numeric position.</summary>
    Ordinal = 1,

    /// <summary>Membership carries a free-text position, which may be a range or a fraction.</summary>
    Labeled = 2
}

/// <summary>
/// Whether a group outlives its members.
/// </summary>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum GroupLifetime
{
    /// <summary>The group ceases to exist when its last member leaves, and orphan links are swept.</summary>
    RefCounted = 0,

    /// <summary>The group exists independently, with state of its own.</summary>
    Independent = 1
}
