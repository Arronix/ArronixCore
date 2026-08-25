using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Providers;
using Microsoft.Extensions.Logging;


namespace Arronix.Host.Providers;

/// <summary>
/// Sends what happened to the destinations that asked to hear about it.
/// </summary>
/// <remarks>
/// <para>
/// Which destinations hear about an event is decided by the destination's own declared set, not by a
/// property per event. The surveyed applications carry roughly fifty hand-rolled "supports this event"
/// booleans between them, which is the mechanism the capability model formalizes, invented independently
/// four times. One declared list replaces all of them, an unknown event is not expressible, and an
/// unsupported one is simply not dispatched.
/// </para>
/// <para>
/// The message carries a summary rendered by the media extension that originated it. The destination never
/// interprets media context — it receives a title, a subtitle, artwork and a link, which is exactly what a
/// chat embed, a push payload and an activity feed each need. That is the whole solution to a contract whose
/// shape is generic but whose payload was per-media-kind.
/// </para>
/// </remarks>
/// <param name="providers">Where implementations are looked up.</param>
/// <param name="definitions">Where configured definitions are read from.</param>
/// <param name="status">Where failures are recorded.</param>
/// <param name="tests">Where invocations are built.</param>
/// <param name="log">Where dispatch failures are reported.</param>
public sealed partial class NotificationDispatcher(
    ProviderRegistry providers,
    ProviderDefinitionStore definitions,
    ProviderStatusStore status,
    ProviderTestService tests,
    ILogger<NotificationDispatcher> log)
{
    private readonly ProviderRegistry _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    private readonly ProviderDefinitionStore _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
    private readonly ProviderStatusStore _status = status ?? throw new ArgumentNullException(nameof(status));
    private readonly ProviderTestService _tests = tests ?? throw new ArgumentNullException(nameof(tests));
    private readonly ILogger<NotificationDispatcher> _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>
    /// Sends a message to every destination that declared an interest in its event.
    /// </summary>
    /// <param name="message">What happened.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many destinations were sent to.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "One destination failing must not stop the others, and a notification failure never fails the work that caused it; the failure is recorded against the destination.")]
    public async Task<int> DispatchAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sent = 0;

        foreach (var definition in _definitions.Query(ProviderFamily.Notifier, message.MediaKind, enabledOnly: true))
        {
            if (!_status.IsAvailable(definition.Id)
                || !_providers.TryLease<INotifier>(definition.Provider, out var leased))
            {
                continue;
            }

            // Held for the whole call: teardown waits for it rather than disposing the notifier while it
            // is still sending.
            using var held = leased;
            var notifier = held.Value;

            if (!notifier.SupportedEvents.Contains(message.Event))
            {
                continue;
            }

            try
            {
                await notifier
                    .NotifyAsync(_tests.Invocation(definition, message.CorrelationId), message, cancellationToken)
                    .ConfigureAwait(false);

                _status.RecordSuccess(definition.Id);
                sent++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception failure)
            {
                _status.RecordFailure(definition.Id);
                NotificationFailed(_log, definition.Name, message.Event.ToString(), failure);
            }
        }

        return sent;
    }

    [LoggerMessage(
        EventId = 9100,
        Level = LogLevel.Warning,
        Message = "Notification destination '{Destination}' did not accept a {Event} message.")]
    private static partial void NotificationFailed(
        ILogger logger,
        string destination,
        string @event,
        Exception exception);
}
