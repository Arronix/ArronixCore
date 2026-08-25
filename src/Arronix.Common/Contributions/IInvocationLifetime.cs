namespace Arronix.Common.Contributions;

/// <summary>
/// A contributor's licence to be called, and the thing its teardown waits on.
/// </summary>
/// <remarks>
/// Every runtime call into an object a plugin contributed must hold one of these for the whole call,
/// including across every <c>await</c> in it. Withdrawal closes the lifetime and removes the published
/// contribution in one transition, so a caller either holds a ticket and is waited for, or cannot get one.
/// </remarks>
internal interface IInvocationLifetime
{
    /// <summary>Gets a value indicating whether the contributor has been closed to new invocations.</summary>
    bool IsClosed { get; }

    /// <summary>Takes a ticket, or reports that the contributor is being withdrawn.</summary>
    /// <param name="ticket">The ticket, released when disposed.</param>
    /// <returns><see langword="false"/> when no further invocation may start.</returns>
    bool TryEnter(out IDisposable? ticket);
}
