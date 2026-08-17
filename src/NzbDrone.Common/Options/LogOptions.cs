namespace NzbDrone.Common.Options;

public class LogOptions
{
    public string Level { get; set; }
    public bool? FilterSentryEvents { get; set; }
    public int? Rotate { get; set; }
    public int? SizeLimit { get; set; }
    public bool? Sql { get; set; }
    public string ConsoleLevel { get; set; }
    public string ConsoleFormat { get; set; }
    public bool? AnalyticsEnabled { get; set; }

    /// <summary>
    /// Sentry DSN for crash and error reporting. Empty by default: Arronix operates no Sentry instance, and
    /// with no DSN configured the Sentry target is never registered, so nothing leaves the machine.
    /// Set this only to a DSN you control.
    /// </summary>
    public string SentryDsn { get; set; }
    public string SyslogServer { get; set; }
    public int? SyslogPort { get; set; }
    public string SyslogLevel { get; set; }
    public bool? DbEnabled { get; set; }
}
