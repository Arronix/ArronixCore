
namespace Arronix.Abstractions.Hosting;

/// <summary>
/// The directories an extension may use, already rooted inside that extension's own storage.
/// </summary>
/// <remarks>
/// <para>
/// One instance exists per loaded extension and every path it returns is inside that extension's own
/// tree. This contract deliberately does not expose the host's application-data directory: handing that
/// out would let any extension read the host configuration and the storage layer's files, which is a
/// least-privilege violation whatever the extension intended to do with it.
/// </para>
/// <para>
/// The directories are created before the extension is activated, so implementations of this contract
/// never need to create them and consumers never need to check.
/// </para>
/// </remarks>
public interface IPluginPaths
{
    /// <summary>
    /// Gets the identifier of the extension these paths belong to.
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// Gets the directory for data that must survive a restart and an upgrade.
    /// </summary>
    string DataFolder { get; }

    /// <summary>
    /// Gets the directory for data that is expensive to recompute but safe to lose. The host may empty
    /// it between runs.
    /// </summary>
    string CacheFolder { get; }

    /// <summary>
    /// Gets the directory for scratch files. The host empties it on startup, so nothing written here
    /// survives a restart.
    /// </summary>
    string TempFolder { get; }
}
