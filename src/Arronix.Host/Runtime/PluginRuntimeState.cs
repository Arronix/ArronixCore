using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Loading;


namespace Arronix.Host.Runtime;

/// <summary>
/// Where one extension got to, and why it got no further.
/// </summary>
/// <param name="Id">The extension's identifier.</param>
/// <param name="State">The furthest state it reached.</param>
/// <param name="Name">Its display name, when its declaration could be read.</param>
/// <param name="Version">Its version, when its declaration could be read.</param>
/// <param name="Granted">The capabilities granted to it.</param>
/// <param name="ErrorCode">The machine-readable code it failed with, when it failed.</param>
/// <param name="Message">Why it failed, in a sentence an operator can act on.</param>
/// <param name="Defects">Every individual fault found, not merely the first.</param>
/// <param name="ChangedAt">When it last changed state.</param>
/// <remarks>
/// A failed extension is quarantined and reported here. It is never fatal to the host and its stored
/// configuration is never deleted — the deliberate inversion of a surveyed behavior that removes stored
/// definitions whose implementation has gone missing, which under an extension model means uninstalling an
/// extension destroys the operator's configuration.
/// </remarks>
public sealed record PluginRuntimeState(
    PluginId Id,
    PluginState State,
    string? Name,
    string? Version,
    CapabilitySet Granted,
    CoreErrorCode? ErrorCode,
    string? Message,
    IReadOnlyList<string> Defects,
    DateTimeOffset ChangedAt);
