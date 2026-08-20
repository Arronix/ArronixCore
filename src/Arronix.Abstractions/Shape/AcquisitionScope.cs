
namespace Arronix.Abstractions.Shape;

/// <summary>
/// How much of a hierarchy one acquisition covers.
/// </summary>
/// <remarks>
/// "Which level is the acquisition unit" is under-specified by a level identifier alone. The surveyed
/// applications search for a single item, for a whole run along a sequence axis — which is not a level and
/// never has rows — and for everything under an ancestor. The core needs only the three shapes; which run
/// and which ancestor is the kind's business.
/// </remarks>
public sealed record AcquisitionScope
{
    /// <summary>
    /// Gets the breadth of the acquisition.
    /// </summary>
    public required AcquisitionScopeKind Kind { get; init; }

    /// <summary>
    /// Gets the <see cref="SequenceAxis.AxisId"/> the run is measured along. Set when and only when
    /// <see cref="Kind"/> is <see cref="AcquisitionScopeKind.SequenceSpan"/>.
    /// </summary>
    public string? SequenceAxisId { get; init; }

    /// <summary>
    /// Gets the ancestor level the acquisition covers everything beneath. Set when and only when
    /// <see cref="Kind"/> is <see cref="AcquisitionScopeKind.Ancestor"/>.
    /// </summary>
    public MediaLevelId? AncestorLevelId { get; init; }
}

/// <summary>
/// The breadth of an acquisition.
/// </summary>
public enum AcquisitionScopeKind
{
    /// <summary>One item at the target level.</summary>
    Single = 0,

    /// <summary>Every item in one run along a declared sequence axis.</summary>
    SequenceSpan = 1,

    /// <summary>Every item beneath one ancestor.</summary>
    Ancestor = 2
}
