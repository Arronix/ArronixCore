using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Declares one way the items at a level can be addressed.
/// </summary>
/// <remarks>
/// <para>
/// Addressing is <b>not</b> a property of a level. The surveyed applications carry several numbering
/// schemes on the same item simultaneously, decide between them per release rather than per level, and
/// flag one of them as possibly extrapolated. A level therefore <i>admits</i> a set of spaces and an item
/// carries a bag of readings, zero or more populated.
/// </para>
/// <para>
/// An alternative numbering taken from release names is not a different kind of addressing: it is a
/// non-canonical, provenance-sensitive, possibly-unverified space over the same ordinal shape. The three
/// flags below say exactly that, which is why no fifth scheme has to be invented for it.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record CoordinateSpace
{
    /// <summary>
    /// Gets the identifier levels and readings reference this space by. Unique within the shape.
    /// </summary>
    public required string SpaceId { get; init; }

    /// <summary>
    /// Gets the space's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the shape of the values this space is addressed by.
    /// </summary>
    public required CoordinateKind Kind { get; init; }

    /// <summary>
    /// Gets one entry per tuple component, outermost first. Empty unless <see cref="Kind"/> is
    /// <see cref="CoordinateKind.Ordinal"/>.
    /// </summary>
    public IReadOnlyList<CoordinateComponent> Components { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether this is the space identity and completeness are measured in.
    /// Exactly one canonical space per level that declares any.
    /// </summary>
    public bool IsCanonical { get; init; }

    /// <summary>
    /// Gets a value indicating whether a reading is only meaningful when its source text came from a
    /// release name rather than from a catalog or a file.
    /// </summary>
    public bool IsProvenanceSensitive { get; init; }

    /// <summary>
    /// Gets a value indicating whether values in this space may be extrapolated rather than mapped, and
    /// so may carry <see cref="CoordinateConfidence.Unverified"/>.
    /// </summary>
    public bool MayBeUnverified { get; init; }

    /// <summary>
    /// Gets a value indicating whether the sequence has no intentional holes, so that a gap means
    /// something is missing rather than something never existed.
    /// </summary>
    public bool IsDense { get; init; }
}

/// <summary>
/// One component of an ordinal coordinate space.
/// </summary>
/// <param name="ComponentId">The identifier constraints and axes reference the component by.</param>
/// <param name="Name">The component's display name.</param>
/// <param name="Required">Whether a reading in this space must populate the component.</param>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record CoordinateComponent(string ComponentId, string Name, bool Required);
