namespace Arronix.Common.Telemetry;

/// <summary>
/// The handshake a host performs with the telemetry pipeline before it takes its extensions away.
/// </summary>
/// <remarks>
/// Extension-contributed sinks can only be found and leased while the extensions that contributed them are
/// still published, and events accepted before that point are owed to them. Ordering two hosted services is
/// not enough to guarantee that: the generic host may stop them concurrently. So the pipeline is told
/// explicitly, once, that dynamic delivery is closing, and the caller waits for that to complete before it
/// unpublishes anything.
/// </remarks>
public interface ITelemetryShutdown
{
    /// <summary>
    /// Stops directing new events at extension-contributed sinks, delivers everything already owed to them,
    /// and flushes them — all while they are still leasable.
    /// </summary>
    /// <param name="cancellationToken">Bounds the wait.</param>
    /// <returns>A task that completes when no further event will be handed to a contributed sink.</returns>
    /// <remarks>
    /// Accepting events goes on working across this call, and so does delivery to the host's own sinks. An
    /// extension's cleanup telemetry is raised after this point by definition, and reaching the host's sinks
    /// is what makes it worth raising.
    /// </remarks>
    Task CloseDynamicDeliveryAsync(CancellationToken cancellationToken = default);
}
