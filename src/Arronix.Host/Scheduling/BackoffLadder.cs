namespace Arronix.Host.Scheduling;

/// <summary>
/// How long to wait before trying a failed piece of work again.
/// </summary>
/// <remarks>
/// <para>
/// The ten rungs are the ladder all four surveyed applications use, byte-identical in each of them. It was
/// adopted rather than invented because ten years of operating against the same remote services is better
/// evidence than any reasoning available here.
/// </para>
/// <para>
/// The jitter is full rather than the more common "base plus a random fraction of base". The scheduler's
/// characteristic failure is several extensions retrying one dead remote in lockstep after a shared outage,
/// and full jitter is the only variant that decorrelates them completely: a retry lands uniformly anywhere
/// in the window instead of clustering at its end. The cost is that an early retry may be very early, which
/// is exactly what a queue with a global concurrency ceiling can absorb.
/// </para>
/// <para>
/// The jitter sample is a parameter rather than read from a random source here, so that the ladder is a pure
/// function and every rung of it can be asserted exactly.
/// </para>
/// </remarks>
public static class BackoffLadder
{
    private static readonly TimeSpan[] LadderPeriods =
    [
        TimeSpan.Zero,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24),
    ];

    /// <summary>
    /// Gets the rungs, shortest first.
    /// </summary>
    public static IReadOnlyList<TimeSpan> Periods => LadderPeriods;

    /// <summary>
    /// Gets the un-jittered wait for an attempt.
    /// </summary>
    /// <param name="attempt">Which attempt has just failed, counting from one.</param>
    /// <returns>The rung, clamped to the top of the ladder.</returns>
    /// <remarks>
    /// The first failure waits nothing. A transient fault that clears immediately — a connection reset, a
    /// leader election — should not cost a minute of latency to recover from.
    /// </remarks>
    public static TimeSpan PeriodFor(int attempt)
    {
        var rung = attempt < 1 ? 0 : attempt - 1;

        return rung >= LadderPeriods.Length
            ? LadderPeriods[^1]
            : LadderPeriods[rung];
    }

    /// <summary>
    /// Gets the jittered wait for an attempt.
    /// </summary>
    /// <param name="attempt">Which attempt has just failed, counting from one.</param>
    /// <param name="jitterSample">A sample in the half-open range zero to one.</param>
    /// <returns>The wait.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="jitterSample"/> is outside the half-open range zero to one.
    /// </exception>
    public static TimeSpan Delay(int attempt, double jitterSample)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(jitterSample, 0d);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(jitterSample, 1d);

        return PeriodFor(attempt) * jitterSample;
    }
}
