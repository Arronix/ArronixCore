
namespace Arronix.Abstractions.Events;

/// <summary>
/// Publishes domain events to the host's internal event bus.
/// </summary>
/// <remarks>
/// <para>
/// Delivery is fan-out to every registered <see cref="IEventHandler{TEvent}"/> that the subscriber filter
/// allows. A plugin sees platform events plus events raised from its own namespace, and never another
/// plugin's — direct plugin-to-plugin calls are not a supported topology.
/// </para>
/// <para>
/// The published object's runtime type selects the handlers, not the static type it was passed as.
/// Publishing a concrete event through a base-typed variable reaches the same subscribers as publishing it
/// through its own type; a caller cannot narrow or widen delivery by choosing what to declare.
/// </para>
/// </remarks>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an event.
    /// </summary>
    /// <typeparam name="TEvent">
    /// The static type of <paramref name="domainEvent"/>. Handlers are selected from the object's runtime
    /// type, so this only has to be an event type, not the exact one.
    /// </typeparam>
    /// <param name="domainEvent">The event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A task that completes once every eligible handler has finished. A handler that fails does not fail
    /// the publication; a handler that is slow delays it.
    /// </returns>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;
}
