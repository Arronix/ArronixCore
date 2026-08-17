using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// One orthogonal axis of "does the user want this".
/// </summary>
/// <remarks>
/// <para>
/// Monitoring is not one bit. The richest surveyed kind needs three independent answers — do I want this
/// entry's future output, do I want this particular item, and which manifestation defines complete — and
/// collapsing them loses the distinction between "not wanted" and "wanted but satisfied elsewhere". The
/// third answer is <see cref="VariantSelection"/>; the first two are dimensions.
/// </para>
/// <para>
/// Apply-once policies ("monitor all", "monitor future only", "monitor the latest") are <b>not</b>
/// dimensions. They are actions that write dimensions, and they are declared as such in the presentation
/// intent surface.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MonitorDimension
{
    /// <summary>
    /// Gets the identifier stored state and actions reference this dimension by.
    /// </summary>
    public required string DimensionId { get; init; }

    /// <summary>
    /// Gets the dimension's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets whether the dimension is a two-state answer or a choice among declared values.
    /// </summary>
    public required MonitorDimensionKind Kind { get; init; }

    /// <summary>
    /// Gets the permitted values. Populated when <see cref="Kind"/> is
    /// <see cref="MonitorDimensionKind.Enumerated"/>.
    /// </summary>
    public IReadOnlyList<FacetValue> Choices { get; init; } = [];

    /// <summary>
    /// Gets the value applied when the user expresses no preference.
    /// </summary>
    public string? DefaultChoice { get; init; }
}

/// <summary>
/// The answer shape of a monitor dimension.
/// </summary>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum MonitorDimensionKind
{
    /// <summary>A two-state answer.</summary>
    Toggle = 0,

    /// <summary>A choice among the dimension's declared values.</summary>
    Enumerated = 1
}
