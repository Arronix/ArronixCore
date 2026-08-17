using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Wire;

/// <summary>
/// One thing that happened, pushed to a connected consumer.
/// </summary>
/// <remarks>
/// One tagged record for every kind of event rather than a hierarchy of them. The alternative would need
/// a serializer that resolves a subtype by name across two independently versioned assemblies, which is
/// the versioning hazard this contract layer refuses everywhere else; here it would also mean a consumer
/// crashing on an event kind it has never heard of, on a live connection.
/// </remarks>
[Experimental(ExperimentalContracts.Wire, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record EventEnvelope
{
    /// <summary>
    /// Gets what kind of event this is, which says which of the optional slots are populated.
    /// </summary>
    public required EventKind Kind { get; init; }

    /// <summary>
    /// Gets when it happened.
    /// </summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>
    /// Gets the wider operation it belongs to, when there was one.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the media kind involved, which is also the delivery group the event was published to.
    /// </summary>
    public MediaKindId? MediaKind { get; init; }

    /// <summary>
    /// Gets the item involved, when one was.
    /// </summary>
    public MediaItemRef? Item { get; init; }

    /// <summary>
    /// Gets the extension involved, when one was.
    /// </summary>
    public string? PluginId { get; init; }

    /// <summary>
    /// Gets the job involved, when one was.
    /// </summary>
    public string? JobId { get; init; }

    /// <summary>
    /// Gets the new state of whatever changed state.
    /// </summary>
    public string? State { get; init; }

    /// <summary>
    /// Gets how far along the work is, between zero and one, when it can be measured.
    /// </summary>
    public double? CompletedFraction { get; init; }

    /// <summary>
    /// Gets what to tell the user, when there is something to tell them.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the extension-rendered summary of the subject, for events an activity feed presents.
    /// </summary>
    public RenderedSummary? Summary { get; init; }

    /// <summary>
    /// Gets the platform's health, for events about health.
    /// </summary>
    public HealthSnapshotView? Health { get; init; }
}

/// <summary>
/// What kind of event an envelope carries.
/// </summary>
[Experimental(ExperimentalContracts.Wire, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum EventKind
{
    /// <summary>An item's state or contents changed.</summary>
    ItemChanged = 0,

    /// <summary>A running job reported progress.</summary>
    JobProgress = 1,

    /// <summary>A job finished, successfully or otherwise.</summary>
    JobCompleted = 2,

    /// <summary>The platform's health changed.</summary>
    HealthChanged = 3,

    /// <summary>The work queue changed.</summary>
    QueueChanged = 4,

    /// <summary>Something happened that belongs in the activity feed.</summary>
    ActivityRaised = 5,

    /// <summary>An extension changed state in the load pipeline.</summary>
    PluginStateChanged = 6,

    /// <summary>A provider definition's health changed.</summary>
    ProviderStatusChanged = 7
}
