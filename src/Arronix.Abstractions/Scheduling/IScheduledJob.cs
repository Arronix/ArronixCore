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
    /// Executes the job.
    /// </summary>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The execution result.</returns>
    Task<JobExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Provides context information for job execution.
/// </summary>
public record JobExecutionContext(
    string JobId,
    DateTime ScheduledTime,
    DateTime StartTime,
    IReadOnlyDictionary<string, object> Parameters);

/// <summary>
/// Represents the result of a job execution.
/// </summary>
public record JobExecutionResult(
    bool Success,
    string? ErrorMessage = null,
    IReadOnlyDictionary<string, object>? ResultData = null);
