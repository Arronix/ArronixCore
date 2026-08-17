using System;
using System.Diagnostics;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Sentry;

namespace NzbDrone.Common.Instrumentation;

public static class NzbDroneLogger
{
    private const string FileLogLayout = @"${date:format=yyyy-MM-dd HH\:mm\:ss.f}|${level}|${logger}|${message}${onexception:inner=${newline}${newline}[v${assembly-version}] ${exception:format=ToString}${newline}}";
    private const string ConsoleFormat = "[${level}] ${logger}: ${message} ${onexception:inner=${newline}${newline}[v${assembly-version}] ${exception:format=ToString}${newline}}";

    private static readonly CleansingConsoleLogLayout CleansingConsoleLayout  = new(ConsoleFormat);
    private static readonly CleansingClefLogLayout ClefLogLayout = new();

    private static bool _isConfigured;

    static NzbDroneLogger()
    {
        LogManager.Configuration = new LoggingConfiguration();
    }

    public static void Register(IStartupContext startupContext, bool updateApp, bool inConsole)
    {
        if (_isConfigured)
        {
            throw new InvalidOperationException("Loggers have already been registered.");
        }

        _isConfigured = true;

        GlobalExceptionHandlers.Register();

        var appFolderInfo = new AppFolderInfo(startupContext);

        RegisterGlobalFilters();

        if (Debugger.IsAttached)
        {
            RegisterDebugger();
        }

        RegisterSentry(updateApp, appFolderInfo);

        if (updateApp)
        {
            RegisterUpdateFile(appFolderInfo);
        }
        else
        {
            if (inConsole && (OsInfo.IsNotWindows || RuntimeInfo.IsUserInteractive))
            {
                RegisterConsole();
            }

            RegisterAppFile(appFolderInfo);
        }

        RegisterAuthLogger();

        LogManager.ReconfigExistingLoggers();
    }

    /// <summary>
    /// Environment variable holding the Sentry DSN, matching the <c>Sonarr:Log</c> configuration section
    /// bound in <c>Bootstrap</c>. Read directly from the environment because logging is registered before
    /// configuration binding has run.
    /// </summary>
    private const string SentryDsnEnvironmentVariable = "SONARR__LOG__SENTRYDSN";

    private static void RegisterSentry(bool updateClient, IAppFolderInfo appFolderInfo)
    {
        // Crash/error reporting is OPT-IN and defaults to sending nothing anywhere.
        //
        // Upstream Sonarr hardcoded four of its own sentry.sonarr.tv DSNs here. Inherited unchanged by a fork,
        // that means this application's crash reports, stack traces and warning-level log events are delivered
        // to the Sonarr project's Sentry — a third party with no relationship to this deployment's operator or
        // users. Arronix runs no Sentry instance of its own, so the DSN is empty unless an operator explicitly
        // supplies one they control, and with no DSN no SentryTarget is constructed at all.
        //
        // The logging rules below are still registered against a NullTarget so that rule ordering and the
        // "Sentry" logger's Final rule behave identically whether or not reporting is configured.
        var dsn = Environment.GetEnvironmentVariable(SentryDsnEnvironmentVariable);

        Target target;

        if (dsn.IsNullOrWhiteSpace())
        {
            target = new NullTarget();
        }
        else
        {
            try
            {
                target = new SentryTarget(dsn, appFolderInfo)
                {
                    Name = "sentryTarget",
                    Layout = "${message}"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load dependency, may need an OS update: " + ex.ToString());
                LogManager.GetLogger(nameof(NzbDroneLogger)).Debug(ex, "Failed to load dependency, may need an OS update");

                // We still need the logging rules, so use a null target.
                target = new NullTarget();
            }
        }

        var loggingRule = new LoggingRule("*", updateClient ? LogLevel.Trace : LogLevel.Warn, target);
        LogManager.Configuration.AddTarget("sentryTarget", target);
        LogManager.Configuration.LoggingRules.Add(loggingRule);

        // Events logged to Sentry go only to Sentry.
        var loggingRuleSentry = new LoggingRule("Sentry", LogLevel.Debug, target) { Final = true };
        LogManager.Configuration.LoggingRules.Insert(0, loggingRuleSentry);
    }

    private static void RegisterDebugger()
    {
        var target = new DebuggerTarget();
        target.Name = "debuggerLogger";
        target.Layout = "[${level}] [${threadid}] ${logger}: ${message} ${onexception:inner=${newline}${newline}[v${assembly-version}] ${exception:format=ToString}${newline}}";

        var loggingRule = new LoggingRule("*", LogLevel.Trace, target);
        LogManager.Configuration.AddTarget("debugger", target);
        LogManager.Configuration.LoggingRules.Add(loggingRule);
    }

    private static void RegisterConsole()
    {
        var level = LogLevel.Trace;

        var coloredConsoleTarget = new ColoredConsoleTarget();

        coloredConsoleTarget.Name = "consoleLogger";

        var logFormat = Enum.TryParse<ConsoleLogFormat>(Environment.GetEnvironmentVariable("SONARR__LOG__CONSOLEFORMAT"), out var formatEnumValue)
            ? formatEnumValue
            : ConsoleLogFormat.Standard;

        ConfigureConsoleLayout(coloredConsoleTarget, logFormat);

        var loggingRule = new LoggingRule("*", level, coloredConsoleTarget);

        LogManager.Configuration.AddTarget("console", coloredConsoleTarget);
        LogManager.Configuration.LoggingRules.Add(loggingRule);
    }

    private static void RegisterAppFile(IAppFolderInfo appFolderInfo)
    {
        RegisterAppFile(appFolderInfo, "appFileInfo", "sonarr.txt", 5, LogLevel.Info);
        RegisterAppFile(appFolderInfo, "appFileDebug", "sonarr.debug.txt", 50, LogLevel.Off);
        RegisterAppFile(appFolderInfo, "appFileTrace", "sonarr.trace.txt", 50, LogLevel.Off);
    }

    private static void RegisterAppFile(IAppFolderInfo appFolderInfo, string name, string fileName, int maxArchiveFiles, LogLevel minLogLevel)
    {
        var fileTarget = new CleansingFileTarget();

        fileTarget.Name = name;
        fileTarget.FileName = Path.Combine(appFolderInfo.GetLogFolder(), fileName);
        fileTarget.AutoFlush = true;
        fileTarget.KeepFileOpen = false;

        // NLog 6 removed explicit concurrent write tuning APIs; rely on internal file locking.
        fileTarget.ArchiveAboveSize = 1.Megabytes();
        fileTarget.MaxArchiveFiles = maxArchiveFiles;
        fileTarget.EnableFileDelete = true;

        // Use suffix format to approximate previous rolling numbering behavior.
        fileTarget.ArchiveSuffixFormat = "{#}";
        fileTarget.Layout = FileLogLayout;

        var loggingRule = new LoggingRule("*", minLogLevel, fileTarget);

        LogManager.Configuration.AddTarget(name, fileTarget);
        LogManager.Configuration.LoggingRules.Add(loggingRule);
    }

    private static void RegisterUpdateFile(IAppFolderInfo appFolderInfo)
    {
        var fileTarget = new FileTarget();

        fileTarget.Name = "updateFileLogger";
        fileTarget.FileName = Path.Combine(appFolderInfo.GetUpdateLogFolder(), DateTime.Now.ToString("yyyy.MM.dd-HH.mm") + ".txt");
        fileTarget.AutoFlush = true;
        fileTarget.KeepFileOpen = false;

        // Removed concurrent write settings (NLog 6 handles internally)
        fileTarget.Layout = FileLogLayout;

        var loggingRule = new LoggingRule("*", LogLevel.Trace, fileTarget);

        LogManager.Configuration.AddTarget("updateFile", fileTarget);
        LogManager.Configuration.LoggingRules.Add(loggingRule);
    }

    private static void RegisterAuthLogger()
    {
        var consoleTarget = LogManager.Configuration.FindTargetByName("console");
        var fileTarget = LogManager.Configuration.FindTargetByName("appFileInfo");

        var target = consoleTarget ?? fileTarget ?? new NullTarget();

        // Send Auth to Console and info app file, but not the log database
        var rule = new LoggingRule("Auth", LogLevel.Info, target) { Final = true };
        if (consoleTarget != null && fileTarget != null)
        {
            rule.Targets.Add(fileTarget);
        }

        LogManager.Configuration.LoggingRules.Insert(0, rule);
    }

    private static void RegisterGlobalFilters()
    {
        LogManager.Setup().LoadConfiguration(c =>
        {
            c.ForLogger("System.*").WriteToNil(LogLevel.Warn);
            c.ForLogger("Microsoft.*").WriteToNil(LogLevel.Warn);
            c.ForLogger("Microsoft.Hosting.Lifetime*").WriteToNil(LogLevel.Info);
            c.ForLogger("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware").WriteToNil(LogLevel.Fatal);
            c.ForLogger("Sonarr.Http.Authentication.ApiKeyAuthenticationHandler").WriteToNil(LogLevel.Info);
        });
    }

    public static Logger GetLogger(Type obj)
    {
        return LogManager.GetLogger(obj.Name.Replace("NzbDrone.", ""));
    }

    public static Logger GetLogger(object obj)
    {
        return GetLogger(obj.GetType());
    }

    public static void ConfigureConsoleLayout(ColoredConsoleTarget target, ConsoleLogFormat format)
    {
        target.Layout = format switch
        {
            ConsoleLogFormat.Clef => NzbDroneLogger.ClefLogLayout,
            _ => NzbDroneLogger.CleansingConsoleLayout
        };
    }
}

public enum ConsoleLogFormat
{
    Standard,
    Clef
}
