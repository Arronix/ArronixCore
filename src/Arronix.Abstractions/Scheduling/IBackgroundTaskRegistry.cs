namespace Arronix.Abstractions.Scheduling;

/// <summary>
/// Registry for background tasks and scheduled jobs.
/// Plugins use this to register their jobs with the host runtime.
/// </summary>
public interface IBackgroundTaskRegistry
{
    /// <summary>
    /// Registers a scheduled job.
    /// </summary>
    /// <param name="job">The job to register.</param>
    /// <param name="schedule">The cron expression or interval for scheduling.</param>
    void RegisterJob(IScheduledJob job, string schedule);

    /// <summary>
    /// Unregisters a scheduled job.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    void UnregisterJob(string jobId);

    /// <summary>
    /// Gets a registered job by its identifier.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>The job, or null if not found.</returns>
    IScheduledJob? GetJob(string jobId);

    /// <summary>
    /// Gets all registered jobs.
    /// </summary>
    /// <returns>A read-only list of all registered jobs.</returns>
    IReadOnlyList<IScheduledJob> GetAllJobs();

    /// <summary>
    /// Triggers a job to run immediately.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="parameters">Optional parameters for the job execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the job was triggered; otherwise, false.</returns>
    Task<bool> TriggerJobAsync(
        string jobId,
        IReadOnlyDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default);
}
