using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// One way a media kind can be searched for.
/// </summary>
/// <remarks>
/// It names no release-source concept: no protocol mode, no wire parameter, no source identifier. A kind
/// declares what it needs to be able to ask; a source declares what it can be asked. Eligibility is the
/// intersection of the two, computed by the host, and neither side ever names the other's vocabulary.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record SearchKind
{
    /// <summary>
    /// Gets the identifier a query plan references this search by.
    /// </summary>
    public required string SearchKindId { get; init; }

    /// <summary>
    /// Gets the search's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the level the search targets.
    /// </summary>
    public required MediaLevelId TargetLevelId { get; init; }

    /// <summary>
    /// Gets how much of the hierarchy one result covers.
    /// </summary>
    public required AcquisitionScope Scope { get; init; }

    /// <summary>
    /// Gets the terms a source must support to be eligible for this search.
    /// </summary>
    public IReadOnlyList<SearchTerm> RequiredTerms { get; init; } = [];

    /// <summary>
    /// Gets the terms the search uses when a source supports them, and omits otherwise.
    /// </summary>
    public IReadOnlyList<SearchTerm> OptionalTerms { get; init; } = [];

    /// <summary>
    /// Gets the categories results are expected in. The host gates on these before dispatching, so a
    /// source that cannot carry any of them is never called.
    /// </summary>
    public required IReadOnlyList<CategoryId> Categories { get; init; }
}
