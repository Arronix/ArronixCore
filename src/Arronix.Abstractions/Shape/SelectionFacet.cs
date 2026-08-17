using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// One dimension of "which sub-items of a library entry should exist at all".
/// </summary>
/// <remarks>
/// <para>
/// The third profile axis, after "how good does a file have to be" and "which releases are preferred".
/// Two surveyed applications ship it as a first-class profile and the other two do not need it; without
/// it, a kind whose catalog includes material the user never wants has no way to say so.
/// </para>
/// <para>
/// The three kinds are not interchangeable: the surveyed profiles use four flags, two numeric thresholds
/// and one enumeration, and an enumeration-only vocabulary cannot express "at least this many pages".
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record SelectionFacet
{
    /// <summary>
    /// Gets the identifier a resolved policy references this facet by.
    /// </summary>
    public required string FacetId { get; init; }

    /// <summary>
    /// Gets the facet's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the level the facet filters.
    /// </summary>
    public required MediaLevelId AppliesToLevelId { get; init; }

    /// <summary>
    /// Gets the shape of the answer the facet takes.
    /// </summary>
    public required SelectionFacetKind Kind { get; init; }

    /// <summary>
    /// Gets the permitted values. Populated when <see cref="Kind"/> is
    /// <see cref="SelectionFacetKind.Enumerated"/>.
    /// </summary>
    public IReadOnlyList<FacetValue> Values { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether several values may be allowed at once.
    /// </summary>
    public bool MultiValued { get; init; }

    /// <summary>
    /// Gets a value indicating whether the declared <see cref="Values"/> form an ordered scale, so that
    /// choosing one value means it and everything after it. An ordered enumeration is a threshold over
    /// named values, not a set membership, and a consumer that renders it as independent choices is
    /// answering the wrong question.
    /// </summary>
    public bool ValuesAreOrdered { get; init; }

    /// <summary>
    /// Gets the values allowed when the user expresses no preference.
    /// </summary>
    public IReadOnlyList<string> DefaultAllowed { get; init; } = [];

    /// <summary>
    /// Gets which side of the threshold is kept. Meaningful when <see cref="Kind"/> is
    /// <see cref="SelectionFacetKind.Threshold"/>.
    /// </summary>
    public ThresholdDirection ThresholdDirection { get; init; }

    /// <summary>
    /// Gets the threshold applied when the user expresses no preference.
    /// </summary>
    public double? DefaultNumber { get; init; }

    /// <summary>
    /// Gets the unit the threshold is expressed in, for presentation only.
    /// </summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Gets the value applied when the user expresses no preference. Meaningful when <see cref="Kind"/>
    /// is <see cref="SelectionFacetKind.Flag"/>.
    /// </summary>
    public bool DefaultFlag { get; init; }

    /// <summary>
    /// Gets when the filter takes effect.
    /// </summary>
    public FacetApplication Application { get; init; }
}

/// <summary>
/// The shape of the answer a selection facet takes.
/// </summary>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum SelectionFacetKind
{
    /// <summary>A choice among declared values.</summary>
    Enumerated = 0,

    /// <summary>A numeric bound.</summary>
    Threshold = 1,

    /// <summary>A two-state answer.</summary>
    Flag = 2
}

/// <summary>
/// Which side of a numeric threshold is kept.
/// </summary>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum ThresholdDirection
{
    /// <summary>Values at or above the threshold are kept.</summary>
    AtLeast = 0,

    /// <summary>Values at or below the threshold are kept.</summary>
    AtMost = 1
}

/// <summary>
/// When a selection facet takes effect.
/// </summary>
/// <remarks>
/// The distinction is not cosmetic and is the direct answer to a surveyed source of user surprise:
/// filtering at materialization means excluded rows are never created and existing ones are removed on
/// the next refresh, so relaxing a profile later cannot recover what it deleted. Making a kind state
/// which one it means turns a silent behavior into a declared one.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum FacetApplication
{
    /// <summary>Excluded items are not created, and existing ones are removed on refresh.</summary>
    Materialization = 0,

    /// <summary>Excluded items exist but are hidden.</summary>
    Visibility = 1,

    /// <summary>
    /// Excluded items exist, are visible, and are refused acquisition.
    /// </summary>
    /// <remarks>
    /// The third answer, and the one an availability threshold actually wants: a film that has not been
    /// released yet is neither uncreated nor hidden — its row exists, the user sees it and can plan around
    /// it, and only a grab is refused. Without this member a kind in that position has to choose the less
    /// destructive of two wrong answers, which is what the surveyed declaration did.
    /// </remarks>
    Acquisition = 2
}

/// <summary>
/// One permitted value of an enumerated facet, dimension, field or setting.
/// </summary>
/// <param name="Value">The stored value.</param>
/// <param name="Name">The display name.</param>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct FacetValue(string Value, string Name);
