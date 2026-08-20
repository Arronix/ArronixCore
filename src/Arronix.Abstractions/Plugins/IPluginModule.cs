
namespace Arronix.Abstractions.Plugins;

/// <summary>
/// The single entry point of an extension assembly.
/// </summary>
/// <remarks>
/// <para>
/// An extension assembly must expose exactly one public, parameterless-constructible implementation.
/// Neither zero nor more than one is a load failure: ambiguity about what an extension <i>is</i> is a
/// defect, not a feature, and the host never scans for candidates beyond the one entry assembly the
/// manifest names.
/// </para>
/// <para>
/// There is deliberately no stop method. The host owns everything registered through the registry and
/// disposes it; a module that itself needs teardown implements asynchronous disposal and is awaited.
/// </para>
/// </remarks>
public interface IPluginModule
{
    /// <summary>
    /// Gets the extension's identifier. Cross-checked against the manifest at load; a mismatch is a load
    /// failure.
    /// </summary>
    PluginId Id { get; }

    /// <summary>
    /// Registers everything the extension contributes.
    /// </summary>
    /// <param name="context">Everything the extension may reach.</param>
    /// <remarks>
    /// Called exactly once, after validation and before activation. Registration only: no I/O, no network
    /// calls, no long work. Throwing quarantines the extension; it never brings down the host.
    /// </remarks>
    void Configure(IPluginContext context);
}
