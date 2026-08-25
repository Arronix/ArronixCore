using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Common.Lifetimes;
using Arronix.Common.Telemetry;
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
    public const string PluginTag = HostTelemetryEmitter.OriginTag;

    private readonly ITelemetryEmitter _inner;
    private readonly IOriginatedTelemetryEmitter? _originated;
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

        // The platform's own pipeline takes the origin as a typed fact, which is what decides whose
        // enrichers and filters may see the event. The tag below is for whoever reads the tags.
        _originated = inner as IOriginatedTelemetryEmitter;
        _invocation = invocation;
        Plugin = plugin;
    }

    /// <summary>
    /// Gets the extension the events are attributed to.
    /// </summary>
    public PluginId Plugin { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The invocation ticket is taken before any member of the event is read. Every one of them — the tags,
    /// the fingerprint, the message, the exception — may be this extension's own object or its own
    /// overridden getter, so reading one is calling into the extension and needs the ticket that keeps its
    /// code loaded.
    /// </remarks>
    public void Emit(TelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);

        if (_invocation is null)
        {
            Forward(telemetryEvent);
            return;
        }

        if (_invocation.TryEnter(out var lease))
        {
            using (lease)
            {
                Forward(telemetryEvent);
            }

            return;
        }

        // Closed: this extension is being torn down, and this is its cleanup telemetry — the events an
        // operator most wants. It is still emitted and still attributed. What it cannot carry across the
        // close is a live exception, whose type may be the extension's own, so it is rendered to text here,
        // before the pipeline reads anything else.
        Forward(Rendered(telemetryEvent));
    }

    /// <summary>
    /// Hands the event over, saying who raised it when the pipeline can be told and stamping the tag when
    /// it cannot.
    /// </summary>
    /// <remarks>
    /// The platform's own pipeline takes the origin as a typed fact and writes the tag from it, so nothing
    /// is copied twice. Another host's emitter gets the tag, which is all a foreign implementation has.
    /// </remarks>
    private void Forward(TelemetryEvent telemetryEvent)
    {
        if (_originated is not null)
        {
            _originated.Emit(telemetryEvent, Plugin);
            return;
        }

        var tags = new Dictionary<string, string>(telemetryEvent.Tags.Count + 1, StringComparer.Ordinal);

        foreach (var tag in telemetryEvent.Tags)
        {
            tags[tag.Key] = tag.Value;
        }

        tags[PluginTag] = Plugin.ToString();

        // Host-owned collections, never the extension's own: a dictionary or list type defined in the
        // extension's assembly would keep its collectible context alive for as long as the event lives.
        _inner.Emit(telemetryEvent with
        {
            Tags = tags,
            Fingerprint = [.. telemetryEvent.Fingerprint],
        });
    }

    /// <summary>
    /// Replaces a possibly extension-owned failure with three host-owned strings.
    /// </summary>
    /// <remarks>
    /// Reading the failure is calling into the extension: its message and its stack trace are properties it
    /// may have overridden. This runs on the teardown path, where an escape would turn cleanup telemetry
    /// into a failed release and retain the package, so a failure that will not describe itself is
    /// described instead.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A failure's own members are third-party code when the failure is an extension's; a getter that throws must not escape an emit.")]
    private static TelemetryEvent Rendered(TelemetryEvent telemetryEvent)
    {
        if (telemetryEvent.Exception is not { } failure)
        {
            return telemetryEvent;
        }

        ExceptionSummary summary;

        try
        {
            summary = telemetryEvent.ExceptionSummary ?? ExceptionSummary.From(failure);
        }
        catch (Exception unreadable) when (!ProcessFailure.IsFatal(unreadable))
        {
            summary = new ExceptionSummary(
                failure.GetType().FullName ?? failure.GetType().Name,
                $"the failure would not describe itself ({unreadable.GetType().Name})",
                StackTrace: null);
        }

        return telemetryEvent with { Exception = null, ExceptionSummary = summary };
    }
}
