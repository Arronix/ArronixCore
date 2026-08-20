
namespace Arronix.Abstractions.Shape;

/// <summary>
/// One level of a media kind's containment hierarchy.
/// </summary>
/// <remarks>
/// A level is an entity that has rows, an identity and a position in the hierarchy. A grouping run that
/// only ever appears as a coordinate is a <see cref="SequenceAxis"/> and not a level; a cross-cutting
/// collection is a <see cref="GroupingAxis"/> and not a level. Keeping those two out is what stops the
/// hierarchy from acquiring levels that exist to carry a single flag.
/// </remarks>
public sealed record MediaLevel
{
    /// <summary>
    /// Gets the level's identifier, unique within the shape.
    /// </summary>
    public required MediaLevelId Id { get; init; }

    /// <summary>
    /// Gets the level's display name for one item.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the level's display name for several items.
    /// </summary>
    public required string PluralName { get; init; }

    /// <summary>
    /// Gets the containing level, or <see langword="null"/> for the root. Validated acyclic, and in this
    /// contract version also validated to form a single chain.
    /// </summary>
    public MediaLevelId? Parent { get; init; }

    /// <summary>
    /// Gets what the level is for.
    /// </summary>
    public MediaLevelRoles Roles { get; init; } = MediaLevelRoles.None;

    /// <summary>
    /// Gets which kinds of record the level has.
    /// </summary>
    public required LevelIdentity Identity { get; init; }

    /// <summary>
    /// Gets the <see cref="CoordinateSpace.SpaceId"/> values an item at this level may carry a reading
    /// in. Zero or more are populated per item.
    /// </summary>
    public IReadOnlyList<string> CoordinateSpaceIds { get; init; } = [];

    /// <summary>
    /// Gets the grouping runs declared over this level's coordinates.
    /// </summary>
    public IReadOnlyList<SequenceAxis> SequenceAxes { get; init; } = [];

    /// <summary>
    /// Gets the fields an item at this level carries.
    /// </summary>
    public IReadOnlyList<FieldDescriptor> Fields { get; init; } = [];

    /// <summary>
    /// Gets the independent axes of "does the user want this" that apply at this level.
    /// </summary>
    public IReadOnlyList<MonitorDimension> MonitorDimensions { get; init; } = [];

    /// <summary>
    /// Gets the <see cref="FormatFamily.FamilyId"/> values files satisfying this level may belong to.
    /// </summary>
    public IReadOnlyList<string> FormatFamilyIds { get; init; } = [];

    /// <summary>
    /// Gets the selection semantics. Non-<see langword="null"/> when and only when <see cref="Roles"/>
    /// includes <see cref="MediaLevelRoles.VariantAxis"/>.
    /// </summary>
    public VariantSelection? Variant { get; init; }
}
