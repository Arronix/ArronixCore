using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Telemetry;

/// <summary>
/// Decides whether a telemetry event is worth sending.
/// </summary>
/// <remarks>
/// Filters are contributed by whichever component understands the noise it produces — a storage
/// provider knows which of its error codes are routine, an indexer knows which upstream responses are
/// not worth reporting. That is what allows the telemetry pipeline itself to reference none of them.
/// A single filter returning <see langword="false"/> drops the event for every sink.
/// </remarks>
[Experimental(ExperimentalContracts.Telemetry, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface ITelemetryEventFilter
{
    /// <summary>
    /// Determines whether the event should be forwarded to the sinks.
    /// </summary>
    /// <param name="telemetryEvent">The candidate event.</param>
    /// <returns>
    /// <see langword="true"/> to allow the event through; <see langword="false"/> to drop it.
    /// </returns>
    bool ShouldSend(TelemetryEvent telemetryEvent);
}
