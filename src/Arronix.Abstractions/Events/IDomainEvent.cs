using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Events;

/// <summary>
/// A fact that has already happened somewhere in the platform and that other components may react to.
/// </summary>
/// <remarks>
/// The contract carries identity, timing and correlation rather than being a bare marker interface:
/// an event that cannot be correlated back to the operation that produced it is not observable, and
/// an event without a timestamp cannot be ordered once it has crossed a queue.
/// </remarks>
[Experimental(ExperimentalContracts.Events, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IDomainEvent
{
    /// <summary>
    /// Gets the identifier of this individual occurrence, used to detect redelivery.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Gets the instant the fact occurred, as observed by the publisher's clock.
    /// </summary>
    DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Gets the identifier of the wider operation this event belongs to — typically the scheduler's
    /// job correlation identifier — or <see langword="null"/> when the event was not raised inside one.
    /// </summary>
    string? CorrelationId { get; }
}
