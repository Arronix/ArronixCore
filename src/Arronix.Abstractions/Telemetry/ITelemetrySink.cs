
namespace Arronix.Abstractions.Telemetry;

/// <summary>
/// Receives enriched, redacted telemetry events and forwards them somewhere.
/// </summary>
/// <remarks>
/// Usually implemented by a telemetry extension, since the platform owns no remote destination; a host may
/// also register one of its own in its composition, and the two are delivered to alike. An extension
/// registering a sink requires the <c>telemetry-sink</c> capability, which is the privilege of reading the
/// whole process's diagnostic stream. Events reaching a sink have already passed redaction, enrichment,
/// filtering and de-duplication, so every sink observes the same event and none of them observes a live
/// exception.
/// </remarks>
public interface ITelemetrySink
{
    /// <summary>
    /// Gets the stable identifier of this sink. It names the sink in the platform's own diagnostics when
    /// one fails or has to be waited past.
    /// </summary>
    string SinkId { get; }

    /// <summary>
    /// Forwards one event.
    /// </summary>
    /// <param name="telemetryEvent">The event to forward.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the sink has accepted the event.</returns>
    Task SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes anything the sink has buffered.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the buffer has been drained or the token fired.</returns>
    /// <remarks>
    /// The host calls this during shutdown. It is a first-class member rather than an implicit part of
    /// disposal because a buffered sink that is only disposed silently drops the events that mattered
    /// most — the ones raised just before the process stopped.
    /// </remarks>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
