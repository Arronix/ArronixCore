using Arronix.Common.Telemetry;
using Arronix.Host.Runtime;
using Microsoft.Extensions.Hosting;

namespace Arronix.Host.Telemetry;

/// <summary>
/// Owns the telemetry pump's lifetime, so a failure in it reaches the host rather than nobody.
/// </summary>
/// <remarks>
/// <para>
/// Stopping waits for the extension bootstrapper to say it has finished, rather than relying on hosted
/// services stopping in reverse registration order — the generic host may stop them concurrently, so the
/// order is a convention and the signal is a fact. Until that signal arrives the pipeline goes on accepting
/// and delivering, which is what lets an extension's cleanup telemetry reach the host's sinks.
/// </para>
/// <para>
/// The sinks extensions contributed are dealt with earlier still, by the bootstrapper's own handshake with
/// the pipeline, while those extensions can still be found and leased.
/// </para>
/// </remarks>
internal sealed class TelemetryPumpService(HostTelemetryEmitter emitter, PluginBootstrapper extensions) : IHostedService
{
    private readonly HostTelemetryEmitter _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));

    private readonly PluginBootstrapper _extensions =
        extensions ?? throw new ArgumentNullException(nameof(extensions));

    private readonly CancellationTokenSource _stopping = new();

    private readonly object _stopGate = new();

    private Task? _pump;
    private Task? _stop;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pump = _emitter.RunAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Waits for the extensions to be gone, closes the queue, waits for the pump itself to finish — not
    /// merely for the last item to be dequeued — and only then flushes the host's own sinks, so the flush
    /// does not overtake a send the pump was still awaiting. A host send the pipeline already abandoned at
    /// its attempt bound may still be running, and the flush can overlap that one. The awaited pump task is
    /// also where a process-fatal failure inside delivery surfaces.
    /// </remarks>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Stopping is one-way, so a second caller - a host stopping its services, then a fixture disposing
        // that host - is handed the same operation to await rather than a second one to race with. A caller
        // returns when the stop is actually finished, not when it finds one already running.
        lock (_stopGate)
        {
            return _stop ??= StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        // The extensions go first, and this waits for them to say so. Deliberately not on the caller's
        // token: extension withdrawal itself ignores it, so abandoning the wait would leave the queue open
        // and the host's sinks unflushed while the withdrawal it was waiting for went on regardless.
        await _extensions.ExtensionsTornDown.ConfigureAwait(false);

        if (!await _emitter.DrainAsync(CancellationToken.None).ConfigureAwait(false))
        {
            // The budget is the budget. A backlog the pump could not finish inside it is abandoned rather
            // than allowed to make a bounded drain unbounded.
            await _stopping.CancelAsync().ConfigureAwait(false);
        }

        if (_pump is { } pump)
        {
            await pump.ConfigureAwait(false);
        }

        await _emitter.FlushHostAsync(CancellationToken.None).ConfigureAwait(false);

        await _stopping.CancelAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }
}
