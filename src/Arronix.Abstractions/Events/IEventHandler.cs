using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Events;

/// <summary>
/// Handles events of a single type. Register one implementation per event type a component cares about.
/// </summary>
/// <typeparam name="TEvent">The event type this handler consumes.</typeparam>
/// <remarks>
/// The type parameter is contravariant, so a handler written against a base event type also receives
/// derived events. A handler that throws does not prevent the remaining handlers from running.
/// </remarks>
[Experimental(ExperimentalContracts.Events, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles a published event.
    /// </summary>
    /// <param name="domainEvent">The event that was published.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the event has been handled.</returns>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
