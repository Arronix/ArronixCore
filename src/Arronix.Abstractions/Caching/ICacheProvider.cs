
namespace Arronix.Abstractions.Caching;

/// <summary>
/// Hands out caches. The only supported way to obtain one, because the provider is what enforces
/// partition isolation and what sweeps expired entries.
/// </summary>
/// <remarks>
/// <para>
/// A cache is addressed by an owning type and an explicit partition name. The owner scopes the
/// partition so two unrelated components can both use the partition name <c>"titles"</c> without
/// colliding, and the resolved partition is further namespaced by the extension identifier of whoever
/// resolved the provider.
/// </para>
/// <para>
/// There is no global <c>Clear</c> and no enumeration of every cache: both are footguns across a
/// partition boundary, and neither had a caller in the code this replaces.
/// </para>
/// </remarks>
public interface ICacheProvider
{
    /// <summary>
    /// Gets or creates a cache whose entries expire a fixed interval after they are stored.
    /// </summary>
    /// <typeparam name="TOwner">The type that owns the partition.</typeparam>
    /// <typeparam name="TValue">The cached value type.</typeparam>
    /// <param name="partition">The partition name, unique within the owner.</param>
    /// <returns>The cache for that partition.</returns>
    ICache<TValue> GetCache<TOwner, TValue>(string partition);

    /// <summary>
    /// Gets or creates a cache whose entries expire a fixed interval after they were last read.
    /// </summary>
    /// <typeparam name="TOwner">The type that owns the partition.</typeparam>
    /// <typeparam name="TValue">The cached value type.</typeparam>
    /// <param name="partition">The partition name, unique within the owner.</param>
    /// <param name="defaultLifetime">The sliding lifetime applied to entries stored without one.</param>
    /// <returns>The cache for that partition.</returns>
    ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime);

    /// <summary>
    /// Gets or creates a cache whose contents are fetched in bulk and replaced when stale.
    /// </summary>
    /// <typeparam name="TOwner">The type that owns the partition.</typeparam>
    /// <typeparam name="TValue">The cached value type.</typeparam>
    /// <param name="partition">The partition name, unique within the owner.</param>
    /// <param name="fetch">Produces the complete contents. Invoked once per refresh.</param>
    /// <param name="lifetime">
    /// How long the contents stay fresh, or <see langword="null"/> for the provider default.
    /// </param>
    /// <returns>The cache for that partition.</returns>
    ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
        string partition,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeSpan? lifetime = null);
}
