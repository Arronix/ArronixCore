namespace Arronix.Host.Scheduling;

/// <summary>
/// The four ways a job can be scheduled.
/// </summary>
/// <remarks>
/// Closed, and deliberately without cron. No surveyed application uses a cron expression — they all schedule
/// in minutes — and adding one would bring a parser, a time-zone question and a daylight-saving question for
/// no demonstrated demand. Adding <c>cron</c> later is purely additive to the grammar, which makes it the
/// cheapest reversal available.
/// </remarks>
public enum JobScheduleKind
{
    /// <summary>Never runs on its own; it runs when something triggers it.</summary>
    Manual = 0,

    /// <summary>Runs once, when the host has finished admitting extensions.</summary>
    Startup = 1,

    /// <summary>Runs at a fixed interval.</summary>
    Every = 2,

    /// <summary>Runs once a day, at a local wall-clock time.</summary>
    Daily = 3,
}

/// <summary>
/// When a job runs, parsed from the text it was registered with.
/// </summary>
/// <param name="Kind">Which of the four forms this is.</param>
/// <param name="Interval">The interval, for <see cref="JobScheduleKind.Every"/>.</param>
/// <param name="TimeOfDay">The local time of day, for <see cref="JobScheduleKind.Daily"/>.</param>
/// <param name="Raw">The text it was registered with, kept verbatim for reporting.</param>
/// <remarks>
/// <para>
/// The registration contract takes a string, so the host parses it. The parsed form is held here rather
/// than published as a contract of its own: a typed schedule API alongside a string one would be a second
/// source of truth about what a job's schedule is, and the two would disagree the first time the grammar
/// grew. That is the whole reason — the string form is not preserved because the contract is frozen, it is
/// preserved because one spelling of a schedule is better than two.
/// </para>
/// <para>
/// The raw text travels with the parse for exactly that reason — everything the platform reports about a
/// schedule reports what was registered, not the host's interpretation of it.
/// </para>
/// </remarks>
public readonly record struct JobSchedule(
    JobScheduleKind Kind,
    TimeSpan Interval,
    TimeOnly TimeOfDay,
    string Raw)
{
    /// <summary>
    /// Works out when this schedule next comes due.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="lastRun">When the job last ran, or <see langword="null"/> when it never has.</param>
    /// <returns>
    /// The next due time, or <see langword="null"/> when the schedule never comes due on its own.
    /// </returns>
    /// <remarks>
    /// A startup schedule is due exactly once, so it reports a due time only while the job has never run. An
    /// interval schedule that has never run is due immediately, which is what makes a freshly installed
    /// extension's first refresh happen without the operator waiting a full period for it.
    /// </remarks>
    public DateTimeOffset? NextRun(DateTimeOffset now, DateTimeOffset? lastRun)
    {
        switch (Kind)
        {
            case JobScheduleKind.Manual:
                return null;

            case JobScheduleKind.Startup:
                return lastRun is null ? now : null;

            case JobScheduleKind.Every:
                return lastRun is { } previous ? previous + Interval : now;

            case JobScheduleKind.Daily:
                var today = new DateTimeOffset(
                    now.Year,
                    now.Month,
                    now.Day,
                    TimeOfDay.Hour,
                    TimeOfDay.Minute,
                    0,
                    now.Offset);

                return today > now ? today : today.AddDays(1);

            default:
                return null;
        }
    }
}
