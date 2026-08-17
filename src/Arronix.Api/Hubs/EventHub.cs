using Arronix.Abstractions.Identity;
using Microsoft.AspNetCore.SignalR;

namespace Arronix.Api.Hubs;

/// <summary>
/// The live half of the API: one connection per client, carrying every event envelope the client is
/// subscribed to.
/// </summary>
/// <remarks>
/// <para>
/// There is one hub rather than one per subsystem because a client's interest is not organized by
/// subsystem — it is organized by what the person is looking at. Delivery is therefore narrowed by group
/// membership: a client that has opened one media kind joins that kind's group and receives none of the
/// churn from the others, while everything that is about the platform rather than a kind goes to a single
/// system group every connection is in.
/// </para>
/// <para>
/// The hub carries no domain logic and knows no media concept. It joins and leaves groups whose names are
/// built from identifiers, and everything it sends is an envelope somebody else built.
/// </para>
/// </remarks>
internal sealed class EventHub : Hub
{
    /// <summary>The route the hub is mapped at.</summary>
    internal const string Path = "/hub/events";

    /// <summary>The client-side method every envelope is delivered to.</summary>
    internal const string EventMethod = "event";

    /// <summary>The group every connection belongs to, carrying everything that is not about one kind.</summary>
    internal const string SystemGroup = "system";

    /// <summary>
    /// Builds the delivery group for one media kind.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <returns>The group name.</returns>
    internal static string GroupFor(MediaKindId kind) => "kind:" + kind.Value;

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, SystemGroup, Context.ConnectionAborted).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Starts delivering the events of one media kind to this connection.
    /// </summary>
    /// <param name="mediaKind">The media kind identifier to follow.</param>
    /// <returns>A task that completes once the subscription is in place.</returns>
    public Task SubscribeAsync(string mediaKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaKind);
        return Groups.AddToGroupAsync(
            Context.ConnectionId,
            GroupFor(MediaKindId.FromString(mediaKind)),
            Context.ConnectionAborted);
    }

    /// <summary>
    /// Stops delivering the events of one media kind to this connection.
    /// </summary>
    /// <param name="mediaKind">The media kind identifier to stop following.</param>
    /// <returns>A task that completes once the subscription is gone.</returns>
    public Task UnsubscribeAsync(string mediaKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaKind);
        return Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            GroupFor(MediaKindId.FromString(mediaKind)),
            Context.ConnectionAborted);
    }
}
