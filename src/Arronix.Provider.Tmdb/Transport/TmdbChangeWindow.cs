using System;
using System.Collections.Generic;

namespace Arronix.Provider.Tmdb.Transport;

/// <summary>Splits a date range into the windows TMDb's change-list endpoint accepts.</summary>
/// <remarks>
/// TMDb rejects a <c>movie/changes</c> query whose <c>start_date</c>/<c>end_date</c> span exceeds 14 days.
/// A cataloger asked for everything changed since an arbitrary date — which may be months or years ago —
/// cannot send one query; it must walk the range in bounded windows instead. Partitioning is local,
/// deterministic, and independent of any single HTTP call, so it is proved once here rather than by
/// reasoning about a loop buried in the cataloger.
/// </remarks>
internal static class TmdbChangeWindow
{
    private const int MaxSpanDays = 14;

    /// <summary>
    /// Partitions <paramref name="since"/> through <paramref name="until"/> into consecutive, non-overlapping,
    /// inclusive windows of at most 14 calendar days each.
    /// </summary>
    /// <param name="since">The inclusive start of the whole range.</param>
    /// <param name="until">The inclusive end of the whole range.</param>
    /// <returns>
    /// The windows in chronological order. Empty when <paramref name="since"/> is after
    /// <paramref name="until"/> — there is nothing to check.
    /// </returns>
    public static IReadOnlyList<(DateOnly Start, DateOnly End)> Partition(DateOnly since, DateOnly until)
    {
        if (since > until)
        {
            return [];
        }

        var windows = new List<(DateOnly Start, DateOnly End)>();
        var start = since;

        while (start <= until)
        {
            var windowEnd = start.AddDays(MaxSpanDays - 1);
            var end = windowEnd < until ? windowEnd : until;

            windows.Add((start, end));
            start = end.AddDays(1);
        }

        return windows;
    }
}
