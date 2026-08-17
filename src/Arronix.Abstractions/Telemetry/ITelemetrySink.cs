using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Telemetry;

/// <summary>
/// Receives enriched, redacted telemetry events and forwards them somewhere. Implemented by telemetry
/// extensions; never by the platform itself, which owns no remote destination.
/// </summary>
/// <remarks>
/// Registering a sink requires the <c>telemetry-sink</c> capability. Events reaching a sink have
/// already passed enrichment, redaction and de-duplication, so every sink observes the same event.
/// </remarks>
[Experimental(ExperimentalContracts.Telemetry, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface ITelemetrySink
{
    /// <summary>
    /// Gets the stable identifier of this sink, used in diagnostics and to disable it by configuration.
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
