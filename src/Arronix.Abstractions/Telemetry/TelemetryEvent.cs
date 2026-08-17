using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Telemetry;

/// <summary>
/// A vendor-neutral telemetry event. Call sites describe what happened; they never name the sink that
/// will receive it.
/// </summary>
/// <param name="EventId">Identifier of this occurrence, used to detect duplicate delivery.</param>
/// <param name="Timestamp">When the event occurred, from the emitter's injected clock.</param>
/// <param name="Severity">How serious the event is.</param>
/// <param name="Message">The human-readable description.</param>
/// <remarks>
/// <see cref="Fingerprint"/> replaces the untyped, sink-named property key the legacy code used to
/// smuggle grouping information through a logging framework. It is a first-class field here precisely
/// so that grouping survives without the call site knowing which sink groups by it.
/// </remarks>
[Experimental(ExperimentalContracts.Telemetry, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record TelemetryEvent(
    Guid EventId,
    DateTimeOffset Timestamp,
    TelemetrySeverity Severity,
    string Message)
{
    /// <summary>
    /// Gets the ordered tokens sinks use to group related occurrences. An empty fingerprint means the
    /// sink groups by its own default, which is normally the message and stack trace.
    /// </summary>
    public IReadOnlyList<string> Fingerprint { get; init; } = [];

    /// <summary>
    /// Gets the enrichment attached to the event, contributed by the registered enrichers. Values are
    /// redacted before fan-out, so a sink never sees an unredacted tag.
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// Gets the failure that produced this event, when there was one.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets the identifier of the wider operation this event belongs to, when it was raised inside one.
    /// </summary>
    public string? CorrelationId { get; init; }
}
