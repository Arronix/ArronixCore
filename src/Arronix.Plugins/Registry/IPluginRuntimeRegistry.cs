using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Plugins.Loading;


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
    /// Gets every extension the host tried to load, successful or not.
    /// </summary>
    IReadOnlyList<PluginLoadResult> All { get; }

    /// <summary>
    /// Gets only the extensions that are serving.
    /// </summary>
    IReadOnlyList<PluginLoadResult> Active { get; }

    /// <summary>
    /// Finds one extension.
    /// </summary>
    /// <param name="plugin">The identifier.</param>
    /// <param name="result">What became of it, when the host tried to load it.</param>
    /// <returns><see langword="true"/> when the host tried to load it.</returns>
    bool TryGet(PluginId plugin, out PluginLoadResult? result);

    /// <summary>
    /// Projects every extension onto the shape the interface reads.
    /// </summary>
    /// <returns>The published views, ordered by identifier.</returns>
    IReadOnlyList<PluginStatusView> Snapshot();
}
