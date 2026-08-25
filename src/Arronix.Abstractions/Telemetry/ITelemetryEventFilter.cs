
namespace Arronix.Abstractions.Telemetry;

/// <summary>
/// Decides whether a telemetry event is worth sending.
/// </summary>
/// <remarks>
/// <para>
/// Filters are contributed by whichever component understands the noise it produces — a storage provider
/// knows which of its error codes are routine, an indexer knows which upstream responses are not worth
/// reporting. That is what allows the telemetry pipeline itself to reference none of them.
/// </para>
/// <para>
/// What a filter can suppress depends on who registered it. A host-registered filter returning
/// <see langword="false"/> drops the event for every sink. One contributed by an extension requires
/// <see cref="Plugins.Capability.TelemetryProcessing"/> and is asked only about the events that extension
/// raised, and only below <see cref="TelemetrySeverity.Error"/>. It never sees the host's events or another
/// extension's, at any severity: an extension may quiet its own noise, not anyone else's, and not anything
/// serious enough that an operator would be misled by its absence.
/// </para>
/// </remarks>
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
