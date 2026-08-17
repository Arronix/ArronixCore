using Arronix.Abstractions.Health;
using Microsoft.Extensions.Logging;

namespace Arronix.Plugins.Loading;

/// <summary>
/// The loader's own diagnostics, generated rather than formatted.
/// </summary>
/// <remarks>
/// <para>
/// Source-generated so that the message template is compiled once rather than parsed on every call, and so
/// that a disabled level costs nothing at all. Loading runs during startup, where an operator is waiting.
/// </para>
/// <para>
/// This is the <i>host's</i> diagnostic surface, not an extension's. An extension never sees a logger: its
/// diagnostics go through the telemetry emitter, which is what keeps an extension from taking a hard
/// dependency on a logging framework the contract assembly cannot even name.
/// </para>
/// </remarks>
internal static partial class PluginLoaderLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Extension loading is disabled by configuration; nothing will be loaded.")]
    internal static partial void LoadingDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "No extension folder at {Root}; nothing to load.")]
    internal static partial void NoExtensionFolder(ILogger logger, string root);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Extension {Plugin} {Version} is active with {Count} registrations.")]
    internal static partial void Activated(ILogger logger, string plugin, string version, int count);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "Extension {Plugin} was quarantined ({ErrorCode}): {Reason}")]
    internal static partial void Quarantined(ILogger logger, string plugin, CoreErrorCode errorCode, string reason);
}
