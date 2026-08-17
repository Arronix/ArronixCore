using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Telemetry;

/// <summary>
/// Ergonomic entry points onto <see cref="ITelemetryEmitter"/> for the common case of "raise one event
/// with a severity and a message".
/// </summary>
/// <remarks>
/// <para>
/// These exist so that no second diagnostic vocabulary has to be invented for extensions. A dedicated
/// extension-facing logger interface would duplicate this pipeline, would need its own severity type and
/// its own correlation rules, and would immediately disagree with this one. The contract layer takes no
/// package references, so a general-purpose logging abstraction is unreachable from here by
/// construction — which makes one emitter with helpers the only honest answer rather than a compromise.
/// </para>
/// <para>
/// Extension methods add no members to the interface, so an emitter implementation is unaffected and the
/// contract's shape is unchanged. The clock is a parameter rather than an ambient read of the system
/// clock so that a caller which already has a <see cref="TimeProvider"/> — every extension does, through
/// its context — keeps one source of time, and so these helpers stay testable.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Telemetry, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class TelemetryEmitterExtensions
{
    /// <summary>
    /// Raises a <see cref="TelemetrySeverity.Debug"/> event.
    /// </summary>
    /// <param name="emitter">The emitter to raise the event on.</param>
    /// <param name="message">The human-readable description of what happened.</param>
    /// <param name="correlationId">The wider operation this event belongs to, when there is one.</param>
    /// <param name="clock">The clock to timestamp the event with. Defaults to the system clock.</param>
    public static void Debug(
        this ITelemetryEmitter emitter,
        string message,
        string? correlationId = null,
        TimeProvider? clock = null)
        => Raise(emitter, TelemetrySeverity.Debug, message, null, correlationId, clock);

    /// <summary>
    /// Raises an <see cref="TelemetrySeverity.Info"/> event.
    /// </summary>
    /// <param name="emitter">The emitter to raise the event on.</param>
    /// <param name="message">The human-readable description of what happened.</param>
    /// <param name="correlationId">The wider operation this event belongs to, when there is one.</param>
    /// <param name="clock">The clock to timestamp the event with. Defaults to the system clock.</param>
    public static void Info(
        this ITelemetryEmitter emitter,
        string message,
        string? correlationId = null,
        TimeProvider? clock = null)
        => Raise(emitter, TelemetrySeverity.Info, message, null, correlationId, clock);

    /// <summary>
    /// Raises a <see cref="TelemetrySeverity.Warning"/> event.
    /// </summary>
    /// <param name="emitter">The emitter to raise the event on.</param>
    /// <param name="message">The human-readable description of what happened.</param>
    /// <param name="exception">The failure that prompted the warning, when there was one.</param>
    /// <param name="correlationId">The wider operation this event belongs to, when there is one.</param>
    /// <param name="clock">The clock to timestamp the event with. Defaults to the system clock.</param>
    public static void Warn(
        this ITelemetryEmitter emitter,
        string message,
        Exception? exception = null,
        string? correlationId = null,
        TimeProvider? clock = null)
        => Raise(emitter, TelemetrySeverity.Warning, message, exception, correlationId, clock);

    /// <summary>
    /// Raises an <see cref="TelemetrySeverity.Error"/> event.
    /// </summary>
    /// <param name="emitter">The emitter to raise the event on.</param>
    /// <param name="message">The human-readable description of what failed.</param>
    /// <param name="exception">The failure, when one is available.</param>
    /// <param name="correlationId">The wider operation this event belongs to, when there is one.</param>
    /// <param name="clock">The clock to timestamp the event with. Defaults to the system clock.</param>
    public static void Error(
        this ITelemetryEmitter emitter,
        string message,
        Exception? exception = null,
        string? correlationId = null,
        TimeProvider? clock = null)
        => Raise(emitter, TelemetrySeverity.Error, message, exception, correlationId, clock);

    private static void Raise(
        ITelemetryEmitter emitter,
        TelemetrySeverity severity,
        string message,
        Exception? exception,
        string? correlationId,
        TimeProvider? clock)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        emitter.Emit(
            new TelemetryEvent(Guid.NewGuid(), (clock ?? TimeProvider.System).GetUtcNow(), severity, message)
            {
                Exception = exception,
                CorrelationId = correlationId
            });
    }
}
