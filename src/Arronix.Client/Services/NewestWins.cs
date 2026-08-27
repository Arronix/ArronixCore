namespace Arronix.Client.Services;

/// <summary>Lets only the newest of several overlapping refreshes commit what it read.</summary>
/// <remarks>
/// An automatic contract reload notifies twice — the load, then the sweep after it — and awaits do not
/// complete in the order they started, so an older read can land last and put back what a newer one
/// corrected. Interlocked because a notification arrives on whichever context delivered it.
/// </remarks>
internal sealed class NewestWins
{
    private int _requested;

    /// <summary>Numbers a refresh about to start.</summary>
    /// <returns>The number to present when it is ready to commit.</returns>
    public int Request() => Interlocked.Increment(ref _requested);

    /// <summary>Determines whether a numbered refresh is still the newest one requested.</summary>
    /// <param name="request">The number <see cref="Request"/> returned.</param>
    /// <returns><see langword="false"/> for every request a later one has overtaken.</returns>
    /// <remarks>Equality, never an ordering: every earlier number is stale, not only the one before.</remarks>
    public bool IsCurrent(int request) => request == Volatile.Read(ref _requested);
}
