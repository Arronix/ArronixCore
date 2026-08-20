
namespace Arronix.Abstractions.Caching;

/// <summary>
/// A keyed, optionally expiring cache of values of one type.
/// </summary>
/// <typeparam name="TValue">The cached value type.</typeparam>
public interface ICache<TValue> : ICache
{
    /// <summary>
    /// Gets a snapshot of the live values. The snapshot is disconnected from the cache: entries may
    /// expire the instant after it is taken.
    /// </summary>
    IReadOnlyCollection<TValue> Values { get; }

    /// <summary>
    /// Stores a value.
    /// </summary>
    /// <param name="key">The key to store under.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="lifetime">
    /// How long the entry stays live, or <see langword="null"/> to use the cache's default.
    /// </param>
    void Set(string key, TValue value, TimeSpan? lifetime = null);

    /// <summary>
    /// Returns the live value for a key, producing and storing it if absent.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="valueFactory">Produces the value on a miss. Invoked at most once per miss.</param>
    /// <param name="lifetime">
    /// How long a newly produced entry stays live, or <see langword="null"/> for the cache default.
    /// </param>
    /// <returns>The cached or newly produced value.</returns>
    TValue Get(string key, Func<TValue> valueFactory, TimeSpan? lifetime = null);

    /// <summary>
    /// Returns the live value for a key, producing and storing it if absent, without blocking.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="valueFactory">Produces the value on a miss. Invoked at most once per miss.</param>
    /// <param name="lifetime">
    /// How long a newly produced entry stays live, or <see langword="null"/> for the cache default.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached or newly produced value.</returns>
    /// <remarks>
    /// This overload exists because a synchronous-factory-only cache forces callers on network paths to
    /// block a pool thread inside the factory, which is exactly where a cache is most valuable.
    /// </remarks>
    Task<TValue> GetAsync(
        string key,
        Func<CancellationToken, Task<TValue>> valueFactory,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the live value for a key without producing one.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>
    /// The cached value, or <see langword="null"/> when the key is absent or its entry has expired.
    /// The nullable return is deliberate: returning <c>default</c> for a miss reports a null reference
    /// as a legitimate cached value.
    /// </returns>
    TValue? Find(string key);
}
