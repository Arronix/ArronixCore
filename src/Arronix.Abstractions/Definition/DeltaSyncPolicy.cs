using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// The changed-since window policy of a catalog that supports delta synchronization.
/// </summary>
/// <remarks>
/// Carried as parameters because the caching behavior it compensates for is the catalog's, not the
/// host's: a catalog that caches on time boundaries silently drops updates for an exact-instant request
/// straddling one, so the request backs off and floors.
/// </remarks>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record DeltaSyncPolicy
{
    /// <summary>
    /// Gets how many minutes before the last synchronization the changed-since request asks from.
    /// </summary>
    public int BackoffMinutes { get; init; }

    /// <summary>
    /// Gets the boundary the request instant is floored to.
    /// </summary>
    public TimeFloor FloorTo { get; init; } = TimeFloor.None;
}
