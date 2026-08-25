using Arronix.Abstractions.Caching;

namespace Arronix.Common.Caching;

/// <summary>
/// The two things a cache has to be able to do for the provider that owns it, and that no caller outside
/// this assembly may ask of it.
/// </summary>
/// <remarks>
/// Both members deliberately sit beside the contract rather than on it. The public surface is gated by the
/// owning namespace, so once release has closed that namespace every public member refuses — which is
/// exactly what release needs, and exactly what would stop release from being able to finish if it had to
/// go through the same door.
/// </remarks>
internal interface INamespacedCache : ICache
{
    /// <summary>
    /// Removes expired entries when the owning namespace still admits work, and does nothing when it does
    /// not.
    /// </summary>
    /// <remarks>
    /// The provider's sweep runs on a timer and cannot be ordered against a release. Refusing is the wrong
    /// answer there: a timer callback that throws is an unobserved failure, and a namespace being released
    /// is having every entry dropped anyway.
    /// </remarks>
    void SweepIfAdmitted();

    /// <summary>
    /// Drops every value and every caller-supplied delegate this cache holds, without asking the gate.
    /// </summary>
    /// <remarks>
    /// Called by <see cref="CacheNamespace.ReleaseAsync"/> only, and only after the gate is closed and
    /// everything it admitted has finished. Values, keys and fetch delegates can all come from a
    /// collectible load context, so this is the step that decides whether that context can be unloaded.
    /// </remarks>
    void ReleaseReferences();
}
