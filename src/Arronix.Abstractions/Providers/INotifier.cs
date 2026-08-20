
namespace Arronix.Abstractions.Providers;

/// <summary>
/// A destination for outbound notifications.
/// </summary>
/// <remarks>
/// <para>
/// The surveyed applications express "which events does this destination care about" as roughly fifty
/// hand-written boolean properties across four codebases — independently invented, four times, and
/// impossible to extend without editing the interface. One declared set replaces all of them.
/// </para>
/// <para>
/// A destination never interprets media context: what to say about an item is rendered by the extension
/// that owns the item, and arrives already rendered.
/// </para>
/// </remarks>
public interface INotifier : IProvider
{
    /// <summary>
    /// Gets the events this destination accepts. Cross-checked at registration; an event outside the set
    /// is never dispatched.
    /// </summary>
    IReadOnlyList<NotificationEvent> SupportedEvents { get; }

    /// <summary>
    /// Delivers one notification.
    /// </summary>
    /// <param name="invocation">The definition being notified through, and its session.</param>
    /// <param name="message">What happened.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once delivery has been attempted.</returns>
    Task NotifyAsync(
        ProviderInvocation invocation,
        NotificationMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The occasions the platform notifies about.
/// </summary>
/// <remarks>
/// Closed, because the host dispatches on it and a destination declares which members it accepts. An open
/// vocabulary would be silently ignored by every destination that had not heard of the new value, which is
/// indistinguishable from the notification never being sent.
/// </remarks>
public enum NotificationEvent
{
    /// <summary>A release was selected and handed to a transfer client.</summary>
    Grabbed = 0,

    /// <summary>Files were taken into the library.</summary>
    Imported = 1,

    /// <summary>Files replaced existing ones of lower quality.</summary>
    Upgraded = 2,

    /// <summary>Files were renamed.</summary>
    Renamed = 3,

    /// <summary>Files had their embedded metadata rewritten.</summary>
    Retagged = 4,

    /// <summary>An item was added to the library.</summary>
    ItemAdded = 5,

    /// <summary>An item was removed from the library.</summary>
    ItemDeleted = 6,

    /// <summary>A file was removed.</summary>
    FileDeleted = 7,

    /// <summary>A release could not be handed to a transfer client.</summary>
    GrabFailed = 8,

    /// <summary>Files could not be taken into the library.</summary>
    ImportFailed = 9,

    /// <summary>Something needs a person to decide.</summary>
    ManualInteractionRequired = 10,

    /// <summary>Everything wanted for an acquisition has arrived.</summary>
    AcquisitionComplete = 11,

    /// <summary>A health check started reporting a problem.</summary>
    HealthDegraded = 12,

    /// <summary>A health check stopped reporting a problem.</summary>
    HealthRestored = 13,

    /// <summary>The platform updated itself.</summary>
    ApplicationUpdated = 14
}
