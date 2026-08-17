using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Plugins;

#pragma warning disable ARX0001 // Caching contracts are experimental; this assembly decorates them.
#pragma warning disable ARX0014 // The extension model is experimental; this assembly implements it.

namespace Arronix.Plugins.Scoping;

/// <summary>
/// Namespaces one extension's cache partitions under its identifier.
/// </summary>
/// <remarks>
/// The caching contract states that a resolved partition is namespaced by the extension identifier, so that
/// one extension can neither read nor evict another's entries. Prefixing here is what makes that true, and
/// it is done at the provider rather than left to the caller because a guarantee an extension has to
/// cooperate with is not a guarantee.
/// </remarks>
public sealed class PartitionedCacheProvider : ICacheProvider
{
    private readonly ICacheProvider _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCacheProvider"/> class.
    /// </summary>
    /// <param name="inner">The shared provider.</param>
    /// <param name="plugin">The extension whose partitions are namespaced.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public PartitionedCacheProvider(ICacheProvider inner, PluginId plugin)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        Plugin = plugin;
    }

    /// <summary>
    /// Gets the extension whose partitions are namespaced.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Gets the partition name an extension's partition resolves to.
    /// </summary>
    /// <param name="partition">The name the extension asked for.</param>
    /// <returns>The namespaced name.</returns>
    /// <exception cref="ArgumentException"><paramref name="partition"/> is blank.</exception>
    public string ResolvePartition(string partition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partition);
        return $"plugin:{Plugin}:{partition}";
    }

    /// <inheritdoc />
    public ICache<TValue> GetCache<TOwner, TValue>(string partition)
        => _inner.GetCache<TOwner, TValue>(ResolvePartition(partition));

    /// <inheritdoc />
    public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
        => _inner.GetRollingCache<TOwner, TValue>(ResolvePartition(partition), defaultLifetime);

    /// <inheritdoc />
    public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
        string partition,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeSpan? lifetime = null)
        => _inner.GetSelfRefreshingCache<TOwner, TValue>(ResolvePartition(partition), fetch, lifetime);
}
