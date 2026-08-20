
namespace Arronix.Abstractions.Caching;

/// <summary>
/// The non-generic face of a cache, which is what lets a provider enumerate and sweep caches of
/// differing element types without knowing any of them.
/// </summary>
public interface ICache
{
    /// <summary>
    /// Gets the fully-qualified name of this cache, which includes the owning partition. Partitions are
    /// namespaced so that one extension can neither read nor evict another's entries.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the number of entries currently held, including entries that have expired but not yet been
    /// swept.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Removes every entry.
    /// </summary>
    void Clear();

    /// <summary>
    /// Removes entries whose lifetime has elapsed. Called by the provider's sweep; callers rarely need
    /// it directly because expired entries are never returned.
    /// </summary>
    void ClearExpired();

    /// <summary>
    /// Removes a single entry. Removing an absent key is not an error.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    void Remove(string key);
}
