namespace Arronix.Client.Contracts;

/// <summary>A committed transaction, and what refused the signal that announced it.</summary>
/// <param name="Result">What that transaction sealed.</param>
/// <param name="Refused">Every subscriber that refused its announcement, in the order they were told.</param>
internal sealed record ContractCommit(
    ContractReloadResult Result,
    IReadOnlyList<ContractFailure> Refused)
{
    /// <summary>Gets what stands before any transaction has committed.</summary>
    internal static ContractCommit None { get; } = new(ContractReloadResult.None, []);
}

/// <summary>Holds the newest committed transaction, and only ever moves forward.</summary>
/// <remarks>
/// Transactions are numbered under the lease that runs them, but their callers resume in whatever order the
/// scheduler chooses, so an older result can arrive last. Deciding and publishing are one compare-and-swap
/// here rather than two steps in a caller: an older result that wins a separate right-to-commit and is then
/// paused would publish over the newer one that ran while it waited, and a guard that had already advanced
/// would not stop it.
/// </remarks>
internal sealed class NewestWins
{
    private ContractCommit _commit = ContractCommit.None;

    /// <summary>Gets what stands now.</summary>
    public ContractCommit Current => Volatile.Read(ref _commit);

    /// <summary>Publishes a result, unless one at least as new already stands.</summary>
    /// <param name="result">The sealed transaction to publish.</param>
    /// <returns>The commit published, or <see langword="null"/> when it was overtaken.</returns>
    /// <remarks>Deciding and publishing are the same step, so there is no moment between them.</remarks>
    public ContractCommit? Publish(ContractReloadResult result)
    {
        var candidate = new ContractCommit(result, []);

        while (true)
        {
            var current = Volatile.Read(ref _commit);

            if (result.Sequence <= current.Result.Sequence)
            {
                return null;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _commit, candidate, current), current))
            {
                return candidate;
            }
        }
    }

    /// <summary>Attaches an announcement's refusals to the commit that raised it.</summary>
    /// <param name="published">The commit whose announcement they came from.</param>
    /// <param name="refused">What refused it.</param>
    /// <returns><see langword="false"/> once a newer commit stands, so nothing was attached.</returns>
    /// <remarks>
    /// Matched by identity, not by number: a refusal belongs to the announcement it came from, and the
    /// installation this leaves standing is the same object either way.
    /// </remarks>
    public bool Attach(ContractCommit published, IReadOnlyList<ContractFailure> refused)
        => ReferenceEquals(
            Interlocked.CompareExchange(ref _commit, published with { Refused = refused }, published),
            published);
}
