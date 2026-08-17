using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Intent;

/// <summary>
/// Declares one way a media kind's items can be traversed.
/// </summary>
/// <remarks>
/// The declaration says what the traversal <i>is</i> — descend the hierarchy, follow a sequence, follow a
/// cross-cutting collection, partition by a field — and not how it is offered. A schedule of what is
/// coming next, for instance, is a sequence traversal over a date field; declaring the traversal lets one
/// consumer present it as a calendar and another as an ordered list, from the same declaration.
/// </remarks>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record BrowseAxis
{
    /// <summary>
    /// Gets the identifier a request references this traversal by.
    /// </summary>
    public required string AxisId { get; init; }

    /// <summary>
    /// Gets the traversal's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets what kind of traversal this is.
    /// </summary>
    public required BrowseAxisKind Kind { get; init; }

    /// <summary>
    /// Gets the level being traversed, when the traversal is bound to one.
    /// </summary>
    public MediaLevelId? LevelId { get; init; }

    /// <summary>
    /// Gets the <see cref="GroupingAxis.AxisId"/> followed. Set when <see cref="Kind"/> is
    /// <see cref="BrowseAxisKind.Grouping"/>.
    /// </summary>
    public string? GroupingAxisId { get; init; }

    /// <summary>
    /// Gets the <see cref="SequenceAxis.AxisId"/> followed. Set when <see cref="Kind"/> is
    /// <see cref="BrowseAxisKind.Sequence"/> and the sequence is an ordinal run.
    /// </summary>
    public string? SequenceAxisId { get; init; }

    /// <summary>
    /// Gets the <see cref="FieldDescriptor.FieldId"/> partitioned or ordered by. Set when
    /// <see cref="Kind"/> is <see cref="BrowseAxisKind.Facet"/>, or when the sequence runs over a date
    /// field rather than an ordinal axis.
    /// </summary>
    public string? FieldId { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is the traversal used when the user has expressed no
    /// preference. At most one per kind.
    /// </summary>
    public bool IsDefault { get; init; }
}

/// <summary>
/// What kind of traversal a browse axis describes.
/// </summary>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum BrowseAxisKind
{
    /// <summary>Descend the containment hierarchy, one level at a time.</summary>
    Hierarchy = 0,

    /// <summary>Follow a sequence axis or a date field, in order.</summary>
    Sequence = 1,

    /// <summary>Follow a collection that cuts across the hierarchy.</summary>
    Grouping = 2,

    /// <summary>Partition by the values of a field the shape marks groupable.</summary>
    Facet = 3,

    /// <summary>No partition at all.</summary>
    Flat = 4
}
