
namespace Arronix.Abstractions.Wire;

/// <summary>
/// What the platform will say about one queued unit of work.
/// </summary>
/// <param name="EntryId">The entry's identifier.</param>
/// <param name="JobId">The job that will run it.</param>
/// <param name="Owner">The extension it belongs to, or the platform itself.</param>
/// <param name="CorrelationId">The wider operation it belongs to.</param>
/// <param name="Attempt">Which attempt this is, counting from one.</param>
/// <param name="EnqueuedAt">When it was queued.</param>
/// <param name="NotBefore">The earliest it may run, which a failed attempt pushes out.</param>
/// <param name="MediaKind">The media kind it concerns, when it concerns one.</param>
/// <remarks>
/// The attempt count and the earliest-run time are both published because between them they are the whole
/// story an operator needs about work that is not progressing: how many times it has failed and when it
/// will next be tried.
/// </remarks>
public sealed record QueueEntryView(
    Guid EntryId,
    string JobId,
    string Owner,
    string CorrelationId,
    int Attempt,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset NotBefore,
    string? MediaKind);
