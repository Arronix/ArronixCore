using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Throttling;

/// <summary>
/// A permit obtained from an <see cref="IRateLimiter"/>. Disposing it returns the permit.
/// </summary>
/// <remarks>
/// The lease shape is what makes throttling composable: the caller holds the permit for exactly as long
/// as the throttled work runs, instead of sleeping for an interval computed in advance and hoping the
/// work fits inside it.
/// </remarks>
[Experimental(ExperimentalContracts.Throttling, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IRateLimitLease : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the permit was granted. A lease that was not acquired must not
    /// be treated as permission to proceed.
    /// </summary>
    bool IsAcquired { get; }

    /// <summary>
    /// Gets how long the caller should wait before trying again, when the limiter knows. Only
    /// meaningful when <see cref="IsAcquired"/> is <see langword="false"/>.
    /// </summary>
    TimeSpan? RetryAfter { get; }
}

/// <summary>
/// Throttles outbound work by partition, so that everything talking to the same remote shares one
/// budget rather than each caller keeping a private one.
/// </summary>
/// <remarks>
/// <para>
/// The limiter is host-owned and singleton: two extensions hitting the same remote acquire from the
/// same partition, which is the only arrangement under which a remote's published rate limit can
/// actually be honored.
/// </para>
/// <para>
/// Acquisition never blocks a thread. Waiting is expressed by the returned task, not by sleeping.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Throttling, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IRateLimiter
{
    /// <summary>
    /// Acquires a single permit, waiting until one is available.
    /// </summary>
    /// <param name="partitionKey">
    /// Identifies the budget to draw from — normally the remote host. The host composes the caller's
    /// identity into the effective key; callers pass only the remote they are addressing.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lease, which must be disposed once the throttled work has finished.</returns>
    ValueTask<IRateLimitLease> AcquireAsync(string partitionKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires several permits at once, waiting until they are available.
    /// </summary>
    /// <param name="partitionKey">Identifies the budget to draw from.</param>
    /// <param name="permitCount">How many permits the work consumes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lease, which must be disposed once the throttled work has finished.</returns>
    ValueTask<IRateLimitLease> AcquireAsync(
        string partitionKey,
        int permitCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire a single permit without waiting.
    /// </summary>
    /// <param name="partitionKey">Identifies the budget to draw from.</param>
    /// <returns>
    /// A lease whose <see cref="IRateLimitLease.IsAcquired"/> reports the outcome. The lease must be
    /// disposed either way.
    /// </returns>
    IRateLimitLease AttemptAcquire(string partitionKey);
}
