
namespace Arronix.Abstractions.Events;

/// <summary>
/// Handles events of a single type. Register one implementation per event type a component cares about.
/// </summary>
/// <typeparam name="TEvent">The event type this handler consumes.</typeparam>
/// <remarks>
/// The type parameter is contravariant, and what that buys depends on who registered the handler. A handler
/// the host registers in its own container is resolved through the published event's whole contract chain,
/// so one written against a base type does receive derived events. A handler an extension registers through
/// <c>IPluginRegistry.AddEventHandler</c> receives the exact type it named and nothing else: isolation
/// decides which events an extension may see, and a base subscription would widen that. A handler that
/// throws does not prevent the remaining handlers from running.
/// </remarks>
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
