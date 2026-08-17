namespace Arronix.Host.Scheduling;

/// <summary>
/// What kind of failure a piece of work suffered, and therefore what the scheduler does next.
/// </summary>
/// <remarks>
/// Held host-side, not promoted to the contract assembly. It is a five-value enumeration inferred from two
/// data points and it has zero extension implementers: promoting it now would freeze a guess. The way an
/// extension classifies its own failure is a published key in the job result's existing bag, which costs no
/// contract surface at all, and promoting a classifier interface later is purely additive. That is recorded
/// as a known wart rather than presented as a preference — it is the price of two contract records that were
/// made stable before a scheduler existed to use them.
/// </remarks>
public enum FailureClass
{
    /// <summary>Retrying will not help. Bad input, a rejected credential, a missing file.</summary>
    Permanent = 0,

    /// <summary>Retrying will probably help. A timeout, a connection reset, a server error.</summary>
    Transient = 1,

    /// <summary>The remote asked for less traffic, and may have said how long to wait.</summary>
    RateLimited = 2,

    /// <summary>The deployment is misconfigured. Retrying is pointless until an operator acts.</summary>
    Configuration = 3,

    /// <summary>The work was canceled. Not a failure of the work, and never counted as an attempt.</summary>
    Canceled = 4,
}

/// <summary>
/// A classification, and the wait the failure itself asked for when it asked for one.
/// </summary>
/// <param name="Class">What kind of failure it was.</param>
/// <param name="RetryAfter">
/// How long the failure said to wait, when it said. A remote's own answer beats the back-off ladder, because
/// the ladder is a guess and this is not.
/// </param>
public readonly record struct FailureOutcome(FailureClass Class, TimeSpan? RetryAfter);
