using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// The complete declaration of how one media kind is structured.
/// </summary>
/// <remarks>
/// <para>
/// Pure data: no delegates, no types, no interfaces. That is what makes one declaration serve three
/// consumers at once — it is what the extension declares, what the host publishes over the wire, and what
/// a front end renders from. Nothing is duplicated between them, so nothing can disagree.
/// </para>
/// <para>
/// It is also why a media kind can be added without changing the host or any front end: everything they
/// need to know about a kind is in this value, and everything in this value is closed vocabularies and
/// declared identifiers.
/// </para>
/// </remarks>
public sealed record MediaShape
{
    /// <summary>
    /// Gets the media kind this shape describes.
    /// </summary>
    public required MediaKindId Kind { get; init; }

    /// <summary>
    /// Gets the kind's display name for one item.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the kind's display name for several items.
    /// </summary>
    public required string PluralName { get; init; }

    /// <summary>
    /// Gets the containment hierarchy, root first.
    /// </summary>
    public required IReadOnlyList<MediaLevel> Levels { get; init; }

    /// <summary>
    /// Gets how files relate to the items they satisfy.
    /// </summary>
    public required FileBinding FileBinding { get; init; }

    /// <summary>
    /// Gets the coordinate spaces the levels address items in.
    /// </summary>
    public IReadOnlyList<CoordinateSpace> CoordinateSpaces { get; init; } = [];

    /// <summary>
    /// Gets the collections that cut across the hierarchy.
    /// </summary>
    public IReadOnlyList<GroupingAxis> GroupingAxes { get; init; } = [];

    /// <summary>
    /// Gets the format families and their quality ladders.
    /// </summary>
    public required IReadOnlyList<FormatFamily> FormatFamilies { get; init; }

    /// <summary>
    /// Gets the dimensions of "which sub-items should exist at all".
    /// </summary>
    public IReadOnlyList<SelectionFacet> SelectionFacets { get; init; } = [];

    /// <summary>
    /// Gets the ways this kind can be searched for.
    /// </summary>
    public IReadOnlyList<SearchKind> SearchKinds { get; init; } = [];

    /// <summary>
    /// Gets the naming tokens this kind contributes to templates. Reuses the stable naming-token type,
    /// and is the single source of truth the manifest's declaration is cross-checked against.
    /// </summary>
    public required IReadOnlyList<NamingToken> Tokens { get; init; }
}
