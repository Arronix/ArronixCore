using System.Linq;
using Arronix.Abstractions.Wire;
using Arronix.Host.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;


namespace Arronix.Api.Endpoints;

/// <summary>
/// What is waiting to happen.
/// </summary>
/// <remarks>
/// <para>
/// A queue entry is published without the parameters it carries. Those parameters are a bag of arbitrary
/// objects supplied by whichever component enqueued the work — they can hold a progress reporter, a
/// resolved item, a credential a job was handed — and serializing them would turn an activity view into an
/// unbounded leak of internal state. The entry says what is queued, for whom, on whose behalf, how many
/// times it has been attempted and when it may next run, which is everything a person watching a queue
/// actually needs.
/// </para>
/// <para>
/// The item references it does carry are abstract on purpose: the queue holds identifiers and the owning
/// extension rehydrates them, so a queue entry never pins an object graph in memory and never outlives the
/// shape of the thing it points at.
/// </para>
/// </remarks>
internal static class QueueEndpoints
{
    /// <summary>
    /// Maps the queue route.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapQueueEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/queue", ListQueue)
            .WithTags("Jobs")
            .WithName("ListQueue")
            .WithSummary("Lists the work waiting to run, in the order it will be taken.");

        return group;
    }

    private static Ok<IReadOnlyList<QueueEntryView>> ListQueue(JobQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        IReadOnlyList<QueueEntryView> views = [.. queue.Snapshot().Select(ToView)];
        return TypedResults.Ok(views);
    }

    private static QueueEntryView ToView(JobQueueEntry entry) => new(
        entry.EntryId,
        entry.JobId,
        entry.Owner.Value,
        entry.CorrelationId,
        entry.Attempt,
        entry.EnqueuedAt,
        entry.NotBefore,
        entry.MediaKind?.Value);
}
