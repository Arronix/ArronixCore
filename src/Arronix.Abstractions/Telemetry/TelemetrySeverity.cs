
namespace Arronix.Abstractions.Telemetry;

/// <summary>
/// How serious a telemetry event is. Deliberately distinct from any logging framework's level type so
/// that neither the contract nor its callers name a concrete framework.
/// </summary>
/// <remarks>
/// The numbers are the meaning: a verbosity filter is a <c>&gt;=</c> comparison, so the members are
/// ordered least serious first and nothing may be appended out of order. They are never persisted or put
/// on a wire as integers — anything that stores a severity stores the member name — so the ordering is
/// free to be corrected by renumbering whenever a member is added in the middle.
/// </remarks>
public enum TelemetrySeverity
{
    /// <summary>Step-by-step detail, off in normal operation.</summary>
    Trace = 0,

    /// <summary>Diagnostic detail, normally sampled away.</summary>
    Debug = 1,

    /// <summary>A noteworthy but expected occurrence.</summary>
    Info = 2,

    /// <summary>Something unexpected that the platform recovered from.</summary>
    Warning = 3,

    /// <summary>An operation failed.</summary>
    Error = 4,

    /// <summary>The process cannot continue.</summary>
    Fatal = 5
}
