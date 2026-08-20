
namespace Arronix.Abstractions.Diagnostics;

/// <summary>
/// The verbosity at which a progress message is surfaced.
/// </summary>
public enum ProgressLevel
{
    /// <summary>Fine-grained step detail, off in normal operation.</summary>
    Trace = 0,

    /// <summary>Detail useful when diagnosing a job.</summary>
    Debug = 1,

    /// <summary>Milestones an operator would expect to see.</summary>
    Info = 2
}

/// <summary>
/// Reports the progress of a long-running operation without the caller naming a logging framework.
/// </summary>
/// <remarks>
/// <para>
/// The reporter is resolved per operation and already carries the scheduler's correlation identifier,
/// so callers never thread it through by hand and progress lines cannot be attributed to the wrong job.
/// </para>
/// <para>
/// Its existence is what keeps a hard dependency on a concrete logging framework out of every component
/// that only wants to say how far along it is.
/// </para>
/// </remarks>
public interface IProgressReporter
{
    /// <summary>
    /// Gets the correlation identifier of the operation this reporter is scoped to, or
    /// <see langword="null"/> when it was created outside one.
    /// </summary>
    string? CorrelationId { get; }

    /// <summary>
    /// Reports a progress message at <see cref="ProgressLevel.Info"/>.
    /// </summary>
    /// <param name="message">The message describing what is happening now.</param>
    void Report(string message);

    /// <summary>
    /// Reports a progress message.
    /// </summary>
    /// <param name="level">The verbosity at which to surface the message.</param>
    /// <param name="message">The message describing what is happening now.</param>
    void Report(ProgressLevel level, string message);

    /// <summary>
    /// Reports a progress message together with how far the operation has advanced.
    /// </summary>
    /// <param name="level">The verbosity at which to surface the message.</param>
    /// <param name="message">The message describing what is happening now.</param>
    /// <param name="completedFraction">
    /// Completion in the range 0 to 1 inclusive. Implementations clamp values outside that range
    /// rather than throwing, because a progress report must never fail the operation it describes.
    /// </param>
    void Report(ProgressLevel level, string message, double completedFraction);
}
