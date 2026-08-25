using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;

namespace Arronix.Common.Telemetry;

/// <summary>
/// An emitter that can be told which extension an event came from.
/// </summary>
/// <remarks>
/// Origin decides what an extension's enrichers and filters may see, so it is carried as a typed fact
/// rather than read back out of a tag dictionary. The attribution tag is still written, because an
/// operator reads tags; nothing in the pipeline decides anything from it.
/// </remarks>
internal interface IOriginatedTelemetryEmitter : ITelemetryEmitter
{
    /// <summary>Raises an event on behalf of one extension.</summary>
    /// <param name="telemetryEvent">The event.</param>
    /// <param name="origin">The extension that raised it.</param>
    void Emit(TelemetryEvent telemetryEvent, PluginId origin);
}
