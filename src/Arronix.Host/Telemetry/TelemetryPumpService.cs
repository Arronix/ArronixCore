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

    private Task? _pump;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pump = _emitter.RunAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Waits for the extensions to be gone, closes the queue, waits for the pump itself to finish — not
    /// merely for the last item to be dequeued — and only then flushes the host's own sinks, because a
    /// flush that overtakes the last send is a flush of something that had not arrived. The awaited pump
    /// task is also where a process-fatal failure inside delivery surfaces.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // The extensions go first, and this waits for them to say so. Deliberately not on the caller's
        // token: extension withdrawal itself ignores it, so abandoning the wait would leave the queue open
        // and the host's sinks unflushed while the withdrawal it was waiting for went on regardless.
        await _extensions.ExtensionsTornDown.ConfigureAwait(false);
        await _emitter.DrainAsync(CancellationToken.None).ConfigureAwait(false);

        if (_pump is { } pump)
        {
            await pump.ConfigureAwait(false);
        }

        await _emitter.FlushHostAsync(CancellationToken.None).ConfigureAwait(false);

        await _stopping.CancelAsync().ConfigureAwait(false);
        _stopping.Dispose();
    }
}
