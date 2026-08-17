using Arronix.Abstractions.Health;
using Arronix.Abstractions.Scheduling;
using Arronix.Abstractions.Wire;
using Arronix.Host.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

#pragma warning disable ARX0006 // Health contracts are experimental; error codes are quoted in problem documents.
#pragma warning disable ARX0017 // Wire contracts are experimental; this assembly publishes them.

namespace Arronix.Api.Endpoints;

/// <summary>
/// The recurring work: what is scheduled, when it last ran, and a way to make it run now.
/// </summary>
/// <remarks>
/// Triggering goes through the platform's own task registry rather than through the scheduler's internals,
/// because that registry is the contract an extension registered its job against and it already knows how
/// to refuse a job that does not exist or is already running at its concurrency limit. A trigger is
/// therefore always accepted or refused for a reason, and never runs the work inside the request.
/// </remarks>
internal static class JobEndpoints
{
    /// <summary>
    /// Maps the job routes.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapJobEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var jobs = group.MapGroup("/jobs").WithTags("Jobs");

        jobs.MapGet("/", ListJobs)
            .WithName("ListJobs")
            .WithSummary("Lists every scheduled job, with its schedule and last outcome.");

        jobs.MapPost("/{jobId}/trigger", TriggerJob)
            .WithName("TriggerJob")
            .WithSummary("Queues a scheduled job to run now.");

        return group;
    }

    // The registry rather than the scheduler: a job's schedule, its last run and its next one are facts the
    // registry holds, and the scheduler is the loop that reads them. Asking the loop would be asking a
    // consumer of the answer for the answer.
    private static Ok<IReadOnlyList<JobView>> ListJobs(BackgroundTaskRegistry jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        return TypedResults.Ok(jobs.Describe());
    }

    private static async Task<Results<Accepted<ActionResult>, ProblemHttpResult>> TriggerJob(
        string jobId,
        IBackgroundTaskRegistry tasks,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(context);

        if (tasks.GetJob(jobId) is null)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.JobSchedulingFailed,
                $"There is no job '{jobId}'.");
        }

        var correlationId = context.Request.Headers[ActionEndpoints.CorrelationHeader].ToString() is { Length: > 0 } supplied
            ? supplied
            : Guid.NewGuid().ToString("n", System.Globalization.CultureInfo.InvariantCulture);

        var queued = await tasks
            .TriggerJobAsync(jobId, parameters: null, correlationId, cancellationToken)
            .ConfigureAwait(false);

        if (!queued)
        {
            return ApiRequests.Problem(
                StatusCodes.Status409Conflict,
                CoreErrorCode.JobSchedulingFailed,
                $"'{jobId}' could not be queued; it is most likely already running at its concurrency limit.");
        }

        return TypedResults.Accepted(
            (string?)null,
            new ActionResult(true, correlationId, $"'{jobId}' was queued.", null));
    }
}
