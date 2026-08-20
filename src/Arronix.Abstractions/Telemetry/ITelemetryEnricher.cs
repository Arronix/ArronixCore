
namespace Arronix.Abstractions.Telemetry;

/// <summary>
/// Adds context to every telemetry event before it is handed to the sinks.
/// </summary>
/// <remarks>
/// Enrichment is contributed by whoever owns the subsystem being described, because the platform cannot
/// know what facts a given deployment wants attached. Enrichers run in registration order, each
/// receiving the output of the previous one.
/// </remarks>
public interface ITelemetryEnricher
{
    /// <summary>
    /// Returns the event with this enricher's context applied.
    /// </summary>
    /// <param name="telemetryEvent">The event to enrich.</param>
    /// <returns>
    /// The enriched event, or the argument unchanged when this enricher has nothing to add. Events are
    /// immutable, so implementations return a copy rather than mutating the argument.
    /// </returns>
    TelemetryEvent Enrich(TelemetryEvent telemetryEvent);
}
