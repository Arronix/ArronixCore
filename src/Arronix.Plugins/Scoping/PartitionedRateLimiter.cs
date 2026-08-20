using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Throttling;


namespace Arronix.Plugins.Scoping;

/// <summary>
/// Composes an extension's identity into the throttling partition it asked for.
/// </summary>
/// <remarks>
/// <para>
/// The throttling contract tells a caller to pass only the remote it is addressing and says the host
/// composes the caller's identity into the effective partition. This is that composition.
/// </para>
/// <para>
/// The trade it encodes is deliberate and worth stating. A budget shared across every extension addressing
/// the same remote is what politeness to that remote requires; a budget an extension cannot exhaust on
/// another extension's behalf is what fairness inside the platform requires. Composing rather than
/// replacing keeps the remote's name in the key, so the shared budget survives, while the extension's name
/// bounds what any one extension can consume of it.
/// </para>
/// </remarks>
public sealed class PartitionedRateLimiter : IRateLimiter
{
    private readonly IRateLimiter _inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartitionedRateLimiter"/> class.
    /// </summary>
    /// <param name="inner">The shared limiter.</param>
    /// <param name="plugin">The extension whose partitions are composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public PartitionedRateLimiter(IRateLimiter inner, PluginId plugin)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        Plugin = plugin;
    }

    /// <summary>
    /// Gets the extension whose partitions are composed.
    /// </summary>
    public PluginId Plugin { get; }

    /// <summary>
    /// Gets the effective partition an extension's key resolves to.
    /// </summary>
    /// <param name="partitionKey">The key the extension asked for.</param>
    /// <returns>The composed key.</returns>
    /// <exception cref="ArgumentException"><paramref name="partitionKey"/> is blank.</exception>
    public string ResolvePartition(string partitionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        return $"{Plugin}|{partitionKey}";
    }

    /// <inheritdoc />
    public ValueTask<IRateLimitLease> AcquireAsync(string partitionKey, CancellationToken cancellationToken = default)
        => _inner.AcquireAsync(ResolvePartition(partitionKey), cancellationToken);

    /// <inheritdoc />
    public ValueTask<IRateLimitLease> AcquireAsync(
        string partitionKey,
        int permitCount,
        CancellationToken cancellationToken = default)
        => _inner.AcquireAsync(ResolvePartition(partitionKey), permitCount, cancellationToken);

    /// <inheritdoc />
    public IRateLimitLease AttemptAcquire(string partitionKey)
        => _inner.AttemptAcquire(ResolvePartition(partitionKey));
}
