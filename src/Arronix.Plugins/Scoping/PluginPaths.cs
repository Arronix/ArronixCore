using System.IO;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Plugins;


namespace Arronix.Plugins.Scoping;

/// <summary>
/// One extension's own three folders.
/// </summary>
/// <remarks>
/// <para>
/// The platform's own data folder is never handed to an extension, and there is no member on this contract
/// that could return it. An extension asking "where does the platform keep its files" is either lost or
/// hostile, and neither deserves an answer.
/// </para>
/// <para>
/// The folders exist before the extension is activated. A contract promising a writable folder that the
/// extension has to create itself is a contract that fails on first use, in extension code, with an error
/// the extension author has to interpret.
/// </para>
/// </remarks>
public sealed class PluginPaths : IPluginPaths
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginPaths"/> class.
    /// </summary>
    /// <param name="plugin">The extension the folders belong to.</param>
    /// <param name="dataFolder">The folder for data that survives a restart.</param>
    /// <param name="cacheFolder">The folder for data that is safe to lose.</param>
    /// <param name="tempFolder">The folder for scratch files.</param>
    /// <exception cref="ArgumentException">A folder path is blank.</exception>
    public PluginPaths(PluginId plugin, string dataFolder, string cacheFolder, string tempFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempFolder);

        Plugin = plugin;
        DataFolder = Path.GetFullPath(dataFolder);
        CacheFolder = Path.GetFullPath(cacheFolder);
        TempFolder = Path.GetFullPath(tempFolder);
    }

    /// <summary>
    /// Gets the extension the folders belong to.
    /// </summary>
    public PluginId Plugin { get; }

    /// <inheritdoc />
    public string PluginId => Plugin.ToString();

    /// <inheritdoc />
    public string DataFolder { get; }

    /// <inheritdoc />
    public string CacheFolder { get; }

    /// <inheritdoc />
    public string TempFolder { get; }

    /// <summary>
    /// Lays out one extension's folders beneath a root the operator configured.
    /// </summary>
    /// <param name="plugin">The extension.</param>
    /// <param name="root">The root holding every extension's state.</param>
    /// <returns>The paths, not yet created.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is blank.</exception>
    /// <remarks>
    /// The layout puts the extension identifier immediately beneath the root, which is why the identifier's
    /// permitted form excludes anything that could be a path separator.
    /// </remarks>
    public static PluginPaths Beneath(PluginId plugin, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var home = Path.Combine(Path.GetFullPath(root), plugin.ToString());

        return new PluginPaths(
            plugin,
            Path.Combine(home, "data"),
            Path.Combine(home, "cache"),
            Path.Combine(home, "temp"));
    }

    /// <summary>
    /// Creates the folders, emptying the scratch folder first.
    /// </summary>
    /// <remarks>
    /// The scratch folder is emptied on every start because the contract says nothing written there
    /// survives a restart, and a promise the host does not keep is worse than one it never made.
    /// </remarks>
    public void Prepare()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(CacheFolder);

        if (Directory.Exists(TempFolder))
        {
            Directory.Delete(TempFolder, recursive: true);
        }

        Directory.CreateDirectory(TempFolder);
    }
}
