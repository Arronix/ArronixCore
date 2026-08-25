using System.ComponentModel.DataAnnotations;

namespace Arronix.Common.Telemetry;

/// <summary>
/// Operator control over the telemetry pipeline's bounds.
/// </summary>
/// <remarks>
/// Every value here is a bound rather than a feature. Telemetry is what an operator reads when something
/// has already gone wrong, so the pipeline's failure modes — an unbounded queue, a pattern that never
/// finishes, a shutdown that waits forever on a sink that will not answer — matter more than its features.
/// </remarks>
public sealed class TelemetryOptions
{
    /// <summary>The configuration section this options type binds from.</summary>
    public const string SectionName = "Arronix:Telemetry";

    /// <summary>
    /// Gets or sets how many accepted events may wait for delivery. Beyond this the oldest are dropped and
    /// counted, because an emitter that blocks the caller has turned diagnostics into a liability.
    /// </summary>
    [Range(1, 1_000_000)]
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets how long shutdown waits for the queue to drain before flushing the sinks anyway.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan ShutdownDrain { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets how long one sink is waited for, per send and per flush.
    /// </summary>
    /// <remarks>
    /// A budget rather than a kill. In-process code that ignores its token cannot be stopped; what this
    /// bounds is how long the pump waits before going on to the next sink.
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:00.0010000", "00:01:00")]
    public TimeSpan SinkAttempt { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets or sets how long a single redaction pattern may run against one string.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.0010000", "00:00:05")]
    public TimeSpan RedactionTimeout { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets or sets how many redaction rules the installation may hold in total.
    /// </summary>
    [Range(1, 1_000)]
    public int MaxRedactionRules { get; set; } = 200;

    /// <summary>
    /// Gets or sets the longest redaction pattern the installation will compile.
    /// </summary>
    [Range(1, 4_000)]
    public int MaxRedactionPatternLength { get; set; } = 512;
}
