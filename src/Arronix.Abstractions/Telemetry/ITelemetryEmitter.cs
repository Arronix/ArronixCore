
namespace Arronix.Abstractions.Telemetry;

/// <summary>
/// The single entry point for raising telemetry. Everything that reports to an operator or to a remote
/// service goes through here, so no call site anywhere names a telemetry vendor.
/// </summary>
/// <remarks>
/// The emitter enriches, redacts and de-duplicates the event before handing it to the registered
/// <see cref="ITelemetrySink"/> instances. It is deliberately one method: five severity-specific
/// overloads existed in the code this replaces and only one of them was ever called.
/// </remarks>
public interface ITelemetryEmitter
{
    /// <summary>
    /// Raises a telemetry event. Returns as soon as the event is accepted; delivery to sinks is the
    /// emitter's problem, and a failing sink never propagates back to the caller.
    /// </summary>
    /// <param name="telemetryEvent">The event to raise.</param>
    void Emit(TelemetryEvent telemetryEvent);
}
