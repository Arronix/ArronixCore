using Arronix.Abstractions.Caching;

namespace Arronix.Common.Caching;

/// <summary>
/// What the owning provider may ask of a cache, beside the contract rather than on it.
/// </summary>
/// <remarks>
/// Every public member of a released cache refuses, so release and the sweep need a path that does not go
/// through the gate they are closing.
/// </remarks>
internal interface INamespacedCache : ICache
{
    /// <summary>Removes expired entries if the owning namespace still admits work; otherwise does nothing.</summary>
    /// <remarks>The sweep runs on a timer, and a timer callback that throws is a failure nobody observes.</remarks>
    void SweepIfAdmitted();

    /// <summary>Drops every value and caller-supplied delegate this cache holds, without asking the gate.</summary>
    /// <remarks>
    /// Called by <see cref="CacheNamespace.ReleaseAsync"/> only, after the gate has drained. Values, keys
    /// and fetch delegates can come from a collectible context, so this decides whether it can unload.
    /// </remarks>
    void ReleaseReferences();
}
