using Arronix.Abstractions.Diagnostics;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

// The progress reporter and the media-shape reference are experimental contracts. The job context names
// both directly rather than smuggling them through its untyped bag: a published string key over an
// object dictionary is not a weaker version of a typed member, it is an unchecked one.
#pragma warning disable ARX0002
#pragma warning disable ARX0013

namespace Arronix.Abstractions.Scheduling;

/// <summary>
/// Represents a scheduled background job that can be executed by the host runtime.
/// Plugins register jobs to perform periodic tasks like indexer updates, imports, etc.
/// </summary>
public interface IScheduledJob
{
    /// <summary>
    /// Gets the unique identifier for this job.
    /// </summary>
    string JobId { get; }

    /// <summary>
    /// Gets the human-readable name of this job.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the description of what this job does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the execution priority (higher values execute first).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Gets the maximum number of concurrent executions allowed.
    /// </summary>
    int MaxConcurrency { get; }

    /// <summary>
    /// Gets the longest the job may still be running after the cancellation token handed to
    /// <see cref="ExecuteAsync"/> has been signaled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the job's own promise, and it is the only cooperative-shutdown guarantee the platform has:
    /// the runtime cannot abort a thread, so a job that ignores its token cannot be stopped at all. The
    /// host waits this long for the run to end, then stops waiting and records that the job overran. A run
    /// that overran is still in the process, which is why an extension owning one cannot be unloaded.
    /// </para>
    /// <para>
    /// A job whose work is a loop of short steps should declare seconds. A job that makes one long remote
    /// call should declare enough for that call's own timeout to fire, and should pass the token to it.
    /// The host clamps the declared value to its own ceiling, so a job cannot hold shutdown open
    /// indefinitely by declaring a large number.
    /// </para>
    /// </remarks>
    TimeSpan ShutdownDeadline { get; }

    /// <summary>
    /// Executes the job.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">
    /// Signaled when the platform is stopping or the work has been cancelled. Observing it is not optional:
    /// <see cref="ShutdownDeadline"/> is a promise about how quickly this job reacts to it.
    /// </param>
    /// <returns>The execution result.</returns>
    Task<JobExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Provides context information for job execution.
/// </summary>
/// <param name="JobId">The identifier of the job being run.</param>
/// <param name="ScheduledTime">When the run was queued.</param>
/// <param name="StartTime">When the run started.</param>
/// <param name="CorrelationId">
/// The wider operation this run belongs to. Every log line, telemetry event and progress report the run
/// produces carries it, which is what lets an operator follow one user action across several jobs.
/// </param>
/// <param name="Parameters">
/// Arguments specific to this job, supplied by whoever triggered it. Nothing the platform itself provides
/// belongs here — platform facts are the typed members above and below.
/// </param>
/// <param name="Progress">
/// Where the run reports how far it has got, or <see langword="null"/> when nothing is listening.
/// </param>
/// <param name="MediaKind">The media kind the run concerns, when it concerns one.</param>
/// <param name="Items">
/// The items the run concerns, or <see langword="null"/> when the run is not scoped to particular items.
/// </param>
public record JobExecutionContext(
    string JobId,
    DateTime ScheduledTime,
    DateTime StartTime,
    string CorrelationId,
    IReadOnlyDictionary<string, object> Parameters,
    IProgressReporter? Progress = null,
    MediaKindId? MediaKind = null,
    IReadOnlyList<MediaItemRef>? Items = null);

/// <summary>
/// Represents the result of a job execution.
/// </summary>
/// <param name="Success">Whether the work completed.</param>
/// <param name="ErrorMessage">What went wrong, when something did.</param>
/// <param name="ResultData">
/// What the job wants to say about the run, including how it classifies its own failure. See
/// <see cref="Arronix.Abstractions.Plugins.WellKnownJobResults"/>.
/// </param>
public record JobExecutionResult(
    bool Success,
    string? ErrorMessage = null,
    IReadOnlyDictionary<string, object>? ResultData = null);
