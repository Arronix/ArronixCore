
namespace Arronix.Abstractions.Events;

/// <summary>
/// Publishes domain events to the host's internal event bus.
/// </summary>
/// <remarks>
/// Delivery is fan-out to every registered <see cref="IEventHandler{TEvent}"/> that the subscriber
/// filter allows. A plugin sees platform events plus events raised from its own namespace, and never
/// another plugin's — direct plugin-to-plugin calls are not a supported topology.
/// </remarks>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event.
    /// </summary>
    /// <typeparam name="TEvent">The concrete event type, used to select handlers.</typeparam>
    /// <param name="domainEvent">The event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the event has been accepted for delivery.</returns>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
}
