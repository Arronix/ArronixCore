using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Shape;


namespace Arronix.Host.Scheduling;

/// <summary>
/// One piece of work waiting to run.
/// </summary>
/// <remarks>
/// The entry references media by abstract identifier and never by a rehydrated object. An entry that carried
/// an extension's own entity would tie the queue's lifetime to that extension's, would have to be
/// serializable by the host to survive a restart, and would make the queue's memory footprint a function of
/// what the extensions chose to attach. Identifiers are resolved back to items by their owning extension, at
/// the moment the work runs.
/// </remarks>
public sealed record JobQueueEntry
{
    /// <summary>
    /// Gets the exact admission receipt which produced this envelope.
    /// </summary>
    /// <remarks>
    /// Internal because consumers identify work by <see cref="JobId"/>. The scheduler uses reference
    /// identity to reject an envelope captured before a registration with the same textual id was replaced.
    /// </remarks>
    internal RegisteredJob? Registration { get; init; }

    /// <summary>Gets this entry's identifier.</summary>
    public required Guid EntryId { get; init; }

    /// <summary>Gets the job that will run it.</summary>
    public required string JobId { get; init; }

    /// <summary>Gets the extension it belongs to.</summary>
    public required PluginId Owner { get; init; }

    /// <summary>Gets the wider operation it belongs to.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>Gets the parameters the job will be given.</summary>
    public required IReadOnlyDictionary<string, object> Parameters { get; init; }

    /// <summary>
    /// Gets the concurrency keys that must all be free before it runs.
    /// </summary>
    /// <remarks>
    /// Synthesized by the host from the owning extension's capabilities and the media kind the work concerns,
    /// never declared by the extension. An extension that could name its own throttle key could name one no
    /// other work uses and escape the ceiling entirely.
    /// </remarks>
    public required IReadOnlyList<string> ThrottleKeys { get; init; }

    /// <summary>Gets when it was queued.</summary>
    public required DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>Gets the earliest it may run. A failed attempt pushes this out along the back-off ladder.</summary>
    public DateTimeOffset NotBefore { get; init; }

    /// <summary>Gets which attempt this is, counting from one.</summary>
    public int Attempt { get; init; } = 1;

    /// <summary>Gets its rank among entries competing for the same capacity. Higher runs first.</summary>
    public int Priority { get; init; }

    /// <summary>Gets the media kind it concerns, when it concerns one.</summary>
    public MediaKindId? MediaKind { get; init; }

    /// <summary>Gets the items it concerns.</summary>
    public IReadOnlyList<MediaItemRef> Items { get; init; } = [];
}
