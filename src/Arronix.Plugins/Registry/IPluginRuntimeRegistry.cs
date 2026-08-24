using Arronix.Abstractions.Wire;


namespace Arronix.Plugins.Registry;

/// <summary>
/// What the host and the interface ask about extensions.
/// </summary>
/// <remarks>
/// <para>
/// Held host-side rather than promoted. An extension never asks about another extension — direct
/// cross-extension interaction is forbidden, and the registry is precisely the thing that would enable it.
/// The promotion trigger is therefore an extension that must legitimately read another's state, which would
/// itself need review before anything was promoted for it.
/// </para>
/// <para>
/// Quarantined extensions are listed, not hidden. An operator's most common question is about the extension
/// that is <i>not</i> working, and a registry that only remembers successes cannot answer it.
/// </para>
/// </remarks>
public interface IPluginRuntimeRegistry
{
    /// <summary>
    /// Gets every extension as an immutable, lifetime-free status projection.
    /// </summary>
    /// <remarks>
    /// The public reader never exposes the registration ledger, collectible load context or teardown
    /// receipt retained by an active runtime result. Those are lifecycle authority, not status data.
    /// </remarks>
    /// <returns>The published views, ordered by identifier.</returns>
    IReadOnlyList<PluginStatusView> Snapshot();
}
