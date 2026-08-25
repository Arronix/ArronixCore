using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Plugins;
using Arronix.Common.Caching;

namespace Arronix.Plugins.Scoping;

/// <summary>
/// Namespaces one extension's cache partitions under its identifier, in a group the host can take back.
/// </summary>
/// <remarks>
/// <para>
/// The caching contract states that a resolved partition is namespaced by the extension identifier, so one
/// extension can neither read nor evict another's entries. Prefixing here is what makes that true, because
/// a guarantee an extension has to cooperate with is not a guarantee.
/// </para>
/// <para>
/// The releasable namespace is the other half. Cached values, factory delegates and the constructed generic
/// cache instances all come from the extension's collectible context, so teardown has to take the whole
/// group back before it can unload one.
/// </para>
/// </remarks>
public sealed class PartitionedCacheProvider : ICacheProvider
{
    private readonly ICacheNamespace _namespace;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedCacheProvider"/> class.
    /// </summary>
    /// <param name="namespaces">The host's namespace-capable provider.</param>
    /// <param name="plugin">The extension whose partitions are namespaced.</param>
    /// <param name="attemptId">
    /// This load attempt's host-minted identity. Part of the namespace name because an identifier and a
    /// folder are exactly what two attempts at one package have in common, and each attempt needs a group
    /// of its own so a reload reaches its admission conflict rather than colliding here.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="namespaces"/> is <see langword="null"/>.</exception>
    public PartitionedCacheProvider(ICacheNamespaceProvider namespaces, PluginId plugin, long attemptId)
    {
        ArgumentNullException.ThrowIfNull(namespaces);

        Plugin = plugin;
        _namespace = namespaces.CreateNamespace(
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"plugin:{plugin}#{attemptId}"));
    }

    /// <summary>Gets the extension whose partitions are namespaced.</summary>
    public PluginId Plugin { get; }

    /// <summary>Gets the releasable group this extension's caches belong to.</summary>
    public ICacheNamespace Namespace => _namespace;

    /// <summary>Gets the partition name an extension's partition resolves to.</summary>
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
        => _namespace.GetCache<TOwner, TValue>(ResolvePartition(partition));

    /// <inheritdoc />
    public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
        => _namespace.GetRollingCache<TOwner, TValue>(ResolvePartition(partition), defaultLifetime);

    /// <inheritdoc />
    public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
        string partition,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeSpan? lifetime = null)
        => _namespace.GetSelfRefreshingCache<TOwner, TValue>(ResolvePartition(partition), fetch, lifetime);
}
