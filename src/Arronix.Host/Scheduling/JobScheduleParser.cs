using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;

namespace Arronix.Host.Scheduling;

/// <summary>
/// Reads the schedule text a job was registered with.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is four forms and nothing else:
/// </para>
/// <list type="table">
///   <item><term><c>manual</c></term><description>never runs automatically; trigger-only.</description></item>
///   <item><term><c>startup</c></term><description>once, when the host has finished admitting extensions.</description></item>
///   <item><term><c>every PT15M</c> / <c>every 00:15</c></term><description>a fixed interval.</description></item>
///   <item><term><c>daily 03:30</c></term><description>once a day at a local wall-clock time.</description></item>
/// </list>
/// <para>
/// An unparseable schedule is a load failure, never a lenient reinterpretation. A schedule guessed wrong
/// runs forever at a rate nobody chose, and only whoever wrote the extension can tell which of
/// the plausible readings was meant.
/// </para>
/// </remarks>
public static class JobScheduleParser
{
    private const string Manual = "manual";
    private const string Startup = "startup";
    private const string EveryPrefix = "every ";
    private const string DailyPrefix = "daily ";

    /// <summary>
    /// Parses a schedule.
    /// </summary>
    /// <param name="text">The text the job was registered with.</param>
    /// <param name="schedule">The parsed schedule when the text is valid.</param>
    /// <param name="error">Why the text is not valid, phrased so it can be corrected.</param>
    /// <returns><see langword="true"/> when the text is valid.</returns>
    [SuppressMessage(
        "Design",
        "CA1021:Avoid out parameters",
        Justification = "The try-and-out form returns two results: the parse and the reason a failed parse failed, which the loader reports back to the extension.")]
    public static bool TryParse(string? text, out JobSchedule schedule, out string? error)
    {
        schedule = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "A schedule is 'manual', 'startup', 'every <duration>' or 'daily <HH:mm>'; this one is empty.";
            return false;
        }

        var trimmed = text.Trim();

        if (string.Equals(trimmed, Manual, StringComparison.OrdinalIgnoreCase))
        {
            schedule = new JobSchedule(JobScheduleKind.Manual, TimeSpan.Zero, default, trimmed);
            return true;
        }

        if (string.Equals(trimmed, Startup, StringComparison.OrdinalIgnoreCase))
        {
            schedule = new JobSchedule(JobScheduleKind.Startup, TimeSpan.Zero, default, trimmed);
            return true;
        }

        if (trimmed.StartsWith(EveryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseEvery(trimmed, trimmed[EveryPrefix.Length..].Trim(), out schedule, out error);
        }

        if (trimmed.StartsWith(DailyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return TryParseDaily(trimmed, trimmed[DailyPrefix.Length..].Trim(), out schedule, out error);
        }

        error = $"'{trimmed}' is not a schedule; use 'manual', 'startup', 'every <duration>' or 'daily <HH:mm>'.";
        return false;
    }

    private static bool TryParseEvery(string raw, string argument, out JobSchedule schedule, out string? error)
    {
        schedule = default;

        if (!TryParseDuration(argument, out var interval))
        {
            error = $"'{argument}' is not a duration; write it as an ISO-8601 duration such as PT15M or as a clock span such as 00:15.";
            return false;
        }

        if (interval <= TimeSpan.Zero)
        {
            error = "An interval is greater than zero; a job that runs every no-time would never stop starting.";
            return false;
        }

        error = null;
        schedule = new JobSchedule(JobScheduleKind.Every, interval, default, raw);
        return true;
    }

    private static bool TryParseDaily(string raw, string argument, out JobSchedule schedule, out string? error)
    {
        schedule = default;

        if (!TimeOnly.TryParseExact(argument, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            error = $"'{argument}' is not a time of day; write it as HH:mm on a 24-hour clock.";
            return false;
        }

        error = null;
        schedule = new JobSchedule(JobScheduleKind.Daily, TimeSpan.Zero, time, raw);
        return true;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The ISO-8601 duration reader signals every malformed input by throwing, and the several types it throws all mean the same thing here: not a duration.")]
    private static bool TryParseDuration(string argument, out TimeSpan interval)
    {
        // ISO-8601 first, because it is the unambiguous form. A clock span is accepted as well because every
        // surveyed application schedules in minutes and "00:15" is what an operator writes.
        if (argument.StartsWith('P') || argument.StartsWith('p'))
        {
            try
            {
                interval = XmlConvert.ToTimeSpan(argument.ToUpperInvariant());
                return true;
            }
            catch (Exception)
            {
                interval = default;
                return false;
            }
        }

        return TimeSpan.TryParse(argument, CultureInfo.InvariantCulture, out interval);
    }
}
