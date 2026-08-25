namespace Arronix.Abstractions.Telemetry;

/// <summary>
/// A failure rendered to text, safe to hand to anything.
/// </summary>
/// <param name="TypeName">The failure's type, as it was named where it was thrown.</param>
/// <param name="Message">Its message, already redacted when the pipeline redacts.</param>
/// <param name="StackTrace">Its stack trace as text, or <see langword="null"/> when it had none.</param>
/// <remarks>
/// <see cref="TelemetryEvent.Exception"/> carries a live object, and a live exception is a handle onto the
/// assembly that defines it and the method that threw it. This is what an event carries once it can be seen
/// by anything other than the host: three strings, which reveal the failure and nothing else, and which
/// keep no assembly alive.
/// </remarks>
public sealed record ExceptionSummary(string TypeName, string Message, string? StackTrace)
{
    /// <summary>
    /// Renders the failure itself. The chain behind it is not walked: an inner exception is another
    /// object with another assembly behind it, and a summary that followed the chain would be unbounded in
    /// a place that must stay bounded. A caller that wants the chain renders it into the message.
    /// </summary>
    /// <param name="failure">The failure to render.</param>
    /// <returns>The summary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="failure"/> is <see langword="null"/>.</exception>
    public static ExceptionSummary From(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new ExceptionSummary(
            failure.GetType().FullName ?? failure.GetType().Name,
            failure.Message,
            failure.StackTrace);
    }
}
