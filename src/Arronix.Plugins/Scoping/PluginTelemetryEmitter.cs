using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;


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

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginTelemetryEmitter"/> class.
    /// </summary>
    /// <param name="inner">The platform's emitter.</param>
    /// <param name="plugin">The extension the events are attributed to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public PluginTelemetryEmitter(ITelemetryEmitter inner, PluginId plugin)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
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

        _inner.Emit(telemetryEvent with { Tags = tags });
    }
}
