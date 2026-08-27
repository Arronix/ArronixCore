namespace Arronix.Client.Contracts;

/// <summary>Lets only the newest of several completed transactions commit what it produced.</summary>
/// <remarks>
/// Transactions are numbered under the lease that runs them, but their callers resume in whatever order the
/// scheduler chooses, so an older result can arrive last. Numbering by completion is what makes "newest"
/// mean the newest installation rather than the newest await.
/// </remarks>
internal sealed class NewestWins
{
    private long _committed;

    /// <summary>Takes the right to commit a numbered result, if nothing newer already has.</summary>
    /// <param name="sequence">The transaction's number.</param>
    /// <returns><see langword="false"/> for a result a newer one has already overtaken.</returns>
    public bool Accepts(long sequence)
    {
        while (true)
        {
            var committed = Volatile.Read(ref _committed);

            if (sequence <= committed)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _committed, sequence, committed) == committed)
            {
                return true;
            }
        }
    }

    /// <summary>Determines whether a committed result is still the newest one committed.</summary>
    /// <param name="sequence">The transaction's number.</param>
    /// <returns><see langword="false"/> once a newer result has committed over it.</returns>
    public bool IsNewest(long sequence) => sequence == Volatile.Read(ref _committed);
}
