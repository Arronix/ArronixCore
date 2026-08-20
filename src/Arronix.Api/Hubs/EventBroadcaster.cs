using Arronix.Abstractions.Events;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Telemetry;
using Arronix.Abstractions.Wire;
using Arronix.Api.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;


#pragma warning disable CA1848 // Source-generated log delegates buy an allocation on a hot path;
                               // every call site in this file is a startup event or a rare failure.

namespace Arronix.Api.Hubs;

/// <summary>
/// Turns what already happened inside the host into envelopes on the live connection.
/// </summary>
/// <remarks>
/// <para>
/// This is a translator, not a message bus, and the distinction is the whole point of the seam. The
/// platform already has exactly one way for a component to say that something happened — the domain event
/// publisher — and exactly one way for it to say something worth telling a person about — the telemetry
/// emitter. Introducing a second mechanism for the sake of the live connection would mean every future
/// subsystem had to remember to raise the same fact twice, and the two would drift. So this type
/// <em>subscribes</em>: it is an event handler and a telemetry sink, and it holds no queue of its own.
/// </para>
/// <para>
/// Delivery is narrowed by the envelope itself. An envelope that names a media kind goes only to that
/// kind's group, so a client looking at one library is not woken by another's; everything else goes to the
/// system group.
/// </para>
/// </remarks>
internal sealed class EventBroadcaster :
    IEventHandler<IDomainEvent>,
    IEventHandler<ProviderDefinitionChanged>,
    ITelemetrySink
{
    private readonly IHubContext<EventHub> _hub;
    private readonly ILogger<EventBroadcaster> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventBroadcaster"/> class.
    /// </summary>
    /// <param name="hub">The hub envelopes are sent through.</param>
    /// <param name="logger">The logger.</param>
    public EventBroadcaster(IHubContext<EventHub> hub, ILogger<EventBroadcaster> logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(logger);

        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public string SinkId => "arronix.api.events";

    /// <summary>
    /// Sends one envelope to whichever group it belongs to.
    /// </summary>
    /// <param name="envelope">The envelope to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the envelope has been handed to the transport.</returns>
    internal async Task BroadcastAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var group = envelope.MediaKind is { } kind ? EventHub.GroupFor(kind) : EventHub.SystemGroup;

        try
        {
            await _hub.Clients.Group(group)
                .SendAsync(EventHub.EventMethod, envelope, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A connected client failing to be written to must never fail the work that produced the event.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogDebug(exception, "Dropping a {Kind} envelope: the live connection could not be written to.", envelope.Kind);
        }
    }

    /// <inheritdoc />
    public Task HandleAsync(ProviderDefinitionChanged domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return BroadcastAsync(
            new EventEnvelope
            {
                Kind = EventKind.ProviderStatusChanged,
                At = domainEvent.OccurredAt,
                CorrelationId = domainEvent.CorrelationId,
                State = WireText.Name(domainEvent.Change),
                Message = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{domainEvent.Family} definition {domainEvent.DefinitionId} was {domainEvent.Change}."),
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // The typed handler above already covers this one; handling it twice would show it twice.
        if (domainEvent is ProviderDefinitionChanged)
        {
            return Task.CompletedTask;
        }

        // Anything else is reported as activity rather than dropped. A subsystem that later wants a richer
        // envelope gets it by adding a typed handler beside this one — never by raising a second event.
        return BroadcastAsync(
            new EventEnvelope
            {
                Kind = EventKind.ActivityRaised,
                At = domainEvent.OccurredAt,
                CorrelationId = domainEvent.CorrelationId,
                State = domainEvent.GetType().Name,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        // Only things a person would want to be told about. Debug and informational telemetry is a firehose
        // whose whole audience is a log reader, and forwarding it here would also risk a loop: writing to a
        // connection can itself emit telemetry.
        if (telemetryEvent.Severity < TelemetrySeverity.Warning)
        {
            return Task.CompletedTask;
        }

        return BroadcastAsync(
            new EventEnvelope
            {
                Kind = EventKind.ActivityRaised,
                At = telemetryEvent.Timestamp,
                CorrelationId = telemetryEvent.CorrelationId,
                State = WireText.Name(telemetryEvent.Severity),
                Message = telemetryEvent.Message,
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // Nothing is buffered here: an envelope is either handed to the transport or dropped, because a
        // live feed that replays a backlog on reconnect tells the user about a past that has already moved.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
