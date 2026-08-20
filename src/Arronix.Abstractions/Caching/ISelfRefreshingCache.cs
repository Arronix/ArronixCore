
namespace Arronix.Abstractions.Caching;

/// <summary>
/// A cache whose entire contents are fetched in one go and replaced wholesale when stale, rather than
/// filled key by key.
/// </summary>
/// <typeparam name="TValue">The cached value type.</typeparam>
/// <remarks>
/// This is the right shape when the backing source is cheaper to read in bulk than per key — a mapping
/// table behind one remote call, for instance. It has no equivalent in the framework's caching
/// primitives, which are all per-entry.
/// </remarks>
public interface ISelfRefreshingCache<TValue> : ICache
{
    /// <summary>
    /// Returns the value for a key, refreshing first if the contents are stale.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The value for the key.</returns>
    /// <exception cref="KeyNotFoundException">The key is absent after a refresh.</exception>
    Task<TValue> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the value for a key from the current contents without refreshing.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The value, or <see langword="null"/> when the key is absent.</returns>
    TValue? Find(string key);

    /// <summary>
    /// Replaces the contents by invoking the fetch delegate, whether or not they are stale.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the contents have been replaced.</returns>
    /// <remarks>
    /// Concurrent callers observe a single fetch. Allowing them to stampede the source is the defect
    /// this contract is shaped to prevent.
    /// </remarks>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the contents only if they are stale.
    /// </summary>
    /// <param name="timeToLive">
    /// The staleness threshold, or <see langword="null"/> to use the cache's configured lifetime.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the contents are known to be fresh.</returns>
    Task RefreshIfExpiredAsync(TimeSpan? timeToLive = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the contents with a caller-supplied set and marks them fresh.
    /// </summary>
    /// <param name="items">The new contents.</param>
    void Update(IReadOnlyDictionary<string, TValue> items);

    /// <summary>
    /// Marks the current contents as freshly loaded without refetching them.
    /// </summary>
    /// <remarks>
    /// Named for what it does: the lifetime is not extended, the clock that measures staleness is reset.
    /// </remarks>
    void Touch();

    /// <summary>
    /// Determines whether the contents are older than a threshold.
    /// </summary>
    /// <param name="timeToLive">The staleness threshold.</param>
    /// <returns><see langword="true"/> when the contents are stale.</returns>
    bool IsExpired(TimeSpan timeToLive);
}
