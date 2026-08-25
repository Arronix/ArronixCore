namespace Arronix.Common.Telemetry;

/// <summary>
/// The handshake a host performs with the telemetry pipeline before it takes its extensions away.
/// </summary>
/// <remarks>
/// The enrichers, filters and sinks extensions contributed can only be found and leased while those
/// extensions are still published. Ordering two hosted services is not enough to guarantee that: the
/// generic host may stop them concurrently. So the pipeline is told explicitly, once, that extension
/// participation is closing, and the caller waits for that before it unpublishes anything.
/// </remarks>
public interface ITelemetryShutdown
{
    /// <summary>
    /// Closes extension participation: no contributed enricher, filter or sink is entered for an event
    /// after the cutoff, the pipeline stops awaiting the ones it had started, and the contributed sinks are
    /// then given one final flush — the last call they get, and one they can still be leased for.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cuts short the wait on each contributed sink's final flush, alongside that attempt's own bound. It
    /// does not cut short the wait for callbacks already running, and a flush abandoned this way is still
    /// running: its extension's lease is held until it returns.
    /// </param>
    /// <returns>
    /// A task that completes when no further event can be handed to an extension. A call the pipeline
    /// already abandoned may still be running: it is held by its own extension's lease until it returns,
    /// which is what keeps that extension loaded underneath it, and the final flush can overlap it.
    /// </returns>
    /// <remarks>
    /// An event accepted before the cutoff but not yet begun is written off rather than held for: it is
    /// counted, and it still goes on to the host's own enrichers, filters and sinks. Accepting events goes
    /// on working across this call, and so does that host delivery — an extension's cleanup telemetry is
    /// raised after this point by definition, and reaching the host's sinks is what makes it worth raising.
    /// </remarks>
    Task CloseDynamicDeliveryAsync(CancellationToken cancellationToken = default);
}
