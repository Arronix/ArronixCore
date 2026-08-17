namespace Arronix.Common.Configuration;

/// <summary>
/// The shape of a log line written to the console.
/// </summary>
/// <remarks>
/// The platform names no logging framework, so this enumeration expresses the operator's intent and the
/// logging binding decides how to honor it.
/// </remarks>
public enum ConsoleLogFormat
{
    /// <summary>
    /// Human-readable single-line text. The default, because the console is normally read by a person.
    /// </summary>
    Standard = 0,

    /// <summary>
    /// One JSON object per line, in the shape the framework's own JSON console formatter emits. Machine
    /// readable without the operator having to install anything beyond the platform itself.
    /// </summary>
    Json = 1,

    /// <summary>
    /// One compact log event per line, for collectors that consume that format directly and retain the
    /// message template alongside its rendered form.
    /// </summary>
    Clef = 2,
}
