using Arronix.Client.Diagnostics;

namespace Arronix.Client.Contracts;

/// <summary>How this client tells the subscribers of one event.</summary>
internal static class Announcement
{
    /// <summary>Tells each subscriber in turn and reports the last refusal.</summary>
    /// <param name="subscribers">The event's delegate, or <see langword="null"/> when nothing subscribed.</param>
    /// <param name="sender">What is announcing.</param>
    /// <returns>Why a subscriber refused, or <see langword="null"/> when none did.</returns>
    /// <remarks>
    /// Raising the delegate whole stops at the first refusal, denying later subscribers the state announced
    /// and faulting the raising task. An unsound process is still contained nowhere.
    /// </remarks>
    internal static string? ToEachSubscriber(EventHandler? subscribers, object sender)
    {
        if (subscribers is null)
        {
            return null;
        }

        string? refused = null;

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(sender, EventArgs.Empty);
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                refused = failure.Message;
            }
        }

        return refused;
    }
}
