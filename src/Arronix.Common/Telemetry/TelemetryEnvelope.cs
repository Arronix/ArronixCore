using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;

namespace Arronix.Common.Telemetry;

/// <summary>
/// One accepted event, together with the facts about it that nothing downstream may change.
/// </summary>
/// <param name="EventId">The identifier the pipeline assigned or accepted. De-duplication reads this.</param>
/// <param name="Timestamp">When it happened.</param>
/// <param name="Severity">How serious it was when it was raised.</param>
/// <param name="Origin">The extension that raised it, or <see langword="null"/> for the host's own.</param>
/// <param name="CorrelationId">The wider operation it belongs to, as it was when the event was accepted.</param>
/// <param name="DynamicDelivery">
/// Whether this event was accepted while extension-contributed sinks were still being delivered to. Fixed
/// at acceptance: an event accepted before the cutoff is owed to those sinks, and one accepted after it is
/// not, whatever happens to the pump in between.
/// </param>
/// <param name="Event">The event as it currently stands, host-owned and redacted.</param>
/// <remarks>
/// An enricher returns a whole event, so it can return one with a different identifier, a different time, a
/// different severity, a different attribution tag or a live exception put back. None of those are its to
/// decide, and none of them are read back out of <see cref="Event"/>: they are held here and restored after
/// every contribution.
/// </remarks>
internal sealed record TelemetryEnvelope(
    Guid EventId,
    DateTimeOffset Timestamp,
    TelemetrySeverity Severity,
    PluginId? Origin,
    string? CorrelationId,
    bool DynamicDelivery,
    TelemetryEvent Event);
