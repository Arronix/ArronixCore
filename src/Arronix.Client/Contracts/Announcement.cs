using Arronix.Client.Diagnostics;

namespace Arronix.Client.Contracts;

/// <summary>How this client tells the subscribers of one event.</summary>
internal static class Announcement
{
    /// <summary>Tells each subscriber in turn and reports every refusal.</summary>
    /// <param name="subscribers">The event's delegate, or <see langword="null"/> when none subscribed.</param>
    /// <param name="sender">What is announcing.</param>
    /// <param name="stage">The step these refusals belong to.</param>
    /// <returns>Each refusal, in the order the subscribers were told; empty when none refused.</returns>
    /// <remarks>
    /// Raising the delegate whole stops at the first refusal and faults the raising task; returning only
    /// the last one loses the others just as completely. An unsound process is still contained nowhere.
    /// </remarks>
    internal static IReadOnlyList<ContractFailure> ToEachSubscriber(
        EventHandler? subscribers,
        object sender,
        ContractFailureStage stage)
    {
        if (subscribers is null)
        {
            return [];
        }

        var refused = new List<ContractFailure>();

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(sender, EventArgs.Empty);
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                refused.Add(new ContractFailure(stage, failure.Message));
            }
        }

        return refused.AsReadOnly();
    }
}
