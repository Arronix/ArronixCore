using System.Globalization;

namespace Arronix.Host.Scheduling;

/// <summary>
/// The identifier that ties everything one operation produced back together.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is a typed member: <c>JobExecutionContext.CorrelationId</c> for a run, and
/// <c>JobQueueEntry.CorrelationId</c> for the queued work it came from. One mechanism, checked by the
/// compiler — two mechanisms for one identifier is how half a system's logs end up uncorrelated.
/// </para>
/// <para>
/// An ambient asynchronous slot exists alongside it so that host code deep in a call chain can read the
/// identifier without every intermediate signature carrying it. It is never the source of truth: the queue
/// entry is, and the ambient value is pushed from it when work starts.
/// </para>
/// </remarks>
public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> Ambient = new();

    /// <summary>
    /// Gets the correlation identifier of the work currently running on this asynchronous flow.
    /// </summary>
    public static string? Current => Ambient.Value;

    /// <summary>
    /// Mints an identifier.
    /// </summary>
    /// <returns>A new identifier.</returns>
    /// <remarks>
    /// Compact rather than a dashed form, because it is written into every telemetry event and every queue
    /// row the operation touches.
    /// </remarks>
    public static string New() => Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    /// <summary>
    /// Sets the identifier for the current asynchronous flow, restoring the previous one when disposed.
    /// </summary>
    /// <param name="correlationId">The identifier.</param>
    /// <returns>A scope that restores the previous identifier.</returns>
    public static IDisposable Push(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var previous = Ambient.Value;
        Ambient.Value = correlationId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
