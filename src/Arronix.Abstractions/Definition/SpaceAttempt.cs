using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One unit-resolution attempt.
/// </summary>
/// <remarks>
/// The title-lookup kind exists because coordinates are not the only way a release addresses a unit: a
/// release may name a unit by its title when numbering fails, and the store-holding match engine is the
/// only place that lookup can run. It mirrors the entry-layer vocabulary — a normalized key against the
/// unit level's titles — so no second grammar is introduced.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record SpaceAttempt
{
    /// <summary>
    /// Gets the coordinate space tried, for the coordinate kind — or the space stamped on a successful
    /// title lookup.
    /// </summary>
    public required string SpaceId { get; init; }

    /// <summary>
    /// Gets what the attempt does.
    /// </summary>
    public SpaceAttemptKind Kind { get; init; } = SpaceAttemptKind.Coordinate;

    /// <summary>
    /// Gets the host normalizer a title lookup runs the candidate text through. Required for the
    /// title-lookup kind; ignored otherwise.
    /// </summary>
    public string? NormalizerId { get; init; }
}
