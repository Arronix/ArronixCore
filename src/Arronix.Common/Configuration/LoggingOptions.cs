using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace Arronix.Common.Configuration;

/// <summary>
/// Operator control over how much the platform logs, where it rotates and how the console renders.
/// </summary>
/// <remarks>
/// Only settings that are true of any logging binding appear here. Anything that configures one concrete
/// sink — a crash-reporting endpoint, a syslog server, a database target — belongs to that sink's own
/// options section, not to the platform's.
/// </remarks>
public sealed class LoggingOptions
{
    /// <summary>
    /// The configuration section this options type binds from.
    /// </summary>
    public const string SectionName = "Arronix:Logging";

    /// <summary>
    /// Gets or sets the lowest severity written to the log files.
    /// </summary>
    [EnumDataType(typeof(LogLevel))]
    public LogLevel Level { get; set; } = LogLevel.Information;

    /// <summary>
    /// Gets or sets the lowest severity written to the console, which is frequently coarser than the file
    /// level because the console is watched live.
    /// </summary>
    [EnumDataType(typeof(LogLevel))]
    public LogLevel ConsoleLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Gets or sets the shape of a console log line.
    /// </summary>
    [EnumDataType(typeof(ConsoleLogFormat))]
    public ConsoleLogFormat ConsoleFormat { get; set; } = ConsoleLogFormat.Standard;

    /// <summary>
    /// Gets or sets the size a log file reaches before it is rotated, in bytes.
    /// </summary>
    [Range(64L * 1024L, 1024L * 1024L * 1024L)]
    public long RotateAboveBytes { get; set; } = 1024L * 1024L;

    /// <summary>
    /// Gets or sets how many rotated files are kept before the oldest is deleted.
    /// </summary>
    [Range(1, 1000)]
    public int RotatedFileCount { get; set; } = 5;

    /// <summary>
    /// Gets or sets a value indicating whether the platform writes log files at all. Turning it off leaves
    /// the console and any contributed sink as the only destinations, which is the normal arrangement for a
    /// containerized deployment where the orchestrator collects standard output.
    /// </summary>
    public bool WriteToFile { get; set; } = true;
}
