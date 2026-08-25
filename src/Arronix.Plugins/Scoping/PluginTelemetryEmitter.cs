using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Plugins.Registry;


namespace Arronix.Plugins.Scoping;

/// <summary>
/// Attributes one extension's telemetry to it.
/// </summary>
/// <remarks>
/// <para>
/// This is also the extension's whole diagnostic surface. There is deliberately no separate logging
/// contract: the contract assembly takes no package references and so cannot name a logging framework, and
/// inventing a parallel logging vocabulary to work around that would be worse than the problem it solved.
/// One pipeline, one correlation identifier, and no extension takes a hard dependency on a logging library.
/// </para>
/// <para>
/// The attribution tag is written last and cannot be overwritten by the extension. An extension that could
/// relabel its own telemetry as another's would make a support bundle actively misleading, which is worse
/// than one that is merely incomplete.
/// </para>
/// </remarks>
public sealed class PluginTelemetryEmitter : ITelemetryEmitter
{
    /// <summary>
    /// The tag every event from an extension carries.
    /// </summary>
    public const string PluginTag = "plugin";

    private readonly ITelemetryEmitter _inner;
    private readonly PluginInvocationLifetime? _invocation;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginTelemetryEmitter"/> class.
    /// </summary>
    /// <param name="inner">The platform's emitter.</param>
    /// <param name="plugin">The extension the events are attributed to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public PluginTelemetryEmitter(ITelemetryEmitter inner, PluginId plugin)
        : this(inner, plugin, invocation: null)
    {
    }

    internal PluginTelemetryEmitter(
        ITelemetryEmitter inner,
        PluginId plugin,
        PluginInvocationLifetime? invocation)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
        _invocation = invocation;
        Plugin = plugin;
    }

    /// <summary>
    /// Gets the extension the events are attributed to.
    /// </summary>
    public PluginId Plugin { get; }

    /// <inheritdoc />
    public void Emit(TelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        var tags = new Dictionary<string, string>(telemetryEvent.Tags.Count + 1, StringComparer.Ordinal);

        foreach (var tag in telemetryEvent.Tags)
        {
            tags[tag.Key] = tag.Value;
        }

        tags[PluginTag] = Plugin.ToString();

        // Host-owned collections, never the extension's own. A dictionary or list type defined in the
        // extension's assembly would keep its collectible context alive for as long as the event lives,
        // which for a queued event is past the point the host declared the extension gone.
        var attributed = telemetryEvent with
        {
            Tags = tags,
            Fingerprint = [.. telemetryEvent.Fingerprint],
        };

        if (_invocation is null)
        {
            _inner.Emit(attributed);
            return;
        }

        // A lease for the duration of the call, so teardown cannot dispose the pipeline's contributions
        // while this extension is inside it.
        if (_invocation.TryEnter(out var lease))
        {
            using (lease)
            {
                _inner.Emit(attributed);
            }

            return;
        }

        // Closed: this extension is being torn down, and this is its cleanup telemetry — the events an
        // operator most wants. It is still emitted and still attributed. What it cannot carry across the
        // close is a live exception, whose type may be the extension's own, so it is rendered to text.
        _inner.Emit(Rendered(attributed));
    }

    /// <summary>Replaces a possibly extension-owned failure with three host-owned strings.</summary>
    private static TelemetryEvent Rendered(TelemetryEvent telemetryEvent)
        => telemetryEvent.Exception is not { } failure
            ? telemetryEvent
            : telemetryEvent with
            {
                Exception = null,
                ExceptionSummary = telemetryEvent.ExceptionSummary ?? ExceptionSummary.From(failure),
            };
}
