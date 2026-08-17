using System.IO;
using Arronix.Plugins.Manifest;

namespace Arronix.Plugins.Loading;

/// <summary>
/// A declaration file found on disk, before anything has been proved about it.
/// </summary>
/// <remarks>
/// Discovery is deliberately dumb: one folder per extension, one declaration file per folder, no recursion
/// and no assembly scanning. A host that discovers extensions by scanning for types cannot tell an operator
/// what it is about to load, and cannot refuse anything before it has already loaded it.
/// </remarks>
public sealed class PluginCandidate
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginCandidate"/> class.
    /// </summary>
    /// <param name="manifestPath">The declaration file that was found.</param>
    /// <param name="manifest">The declaration as read, before semantic validation.</param>
    /// <exception cref="ArgumentException"><paramref name="manifestPath"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is <see langword="null"/>.</exception>
    public PluginCandidate(string manifestPath, PluginManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(manifest);

        ManifestPath = manifestPath;
        Manifest = manifest;
        Folder = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new ArgumentException("The declaration file has no containing folder.", nameof(manifestPath));
    }

    /// <summary>
    /// Gets the declaration file that was found.
    /// </summary>
    public string ManifestPath { get; }

    /// <summary>
    /// Gets the folder the extension owns. Nothing outside it is read.
    /// </summary>
    public string Folder { get; }

    /// <summary>
    /// Gets the declaration as read.
    /// </summary>
    public PluginManifest Manifest { get; }

    /// <summary>
    /// Gets the full path of the entry assembly, resolved inside the extension's own folder.
    /// </summary>
    /// <returns>The path.</returns>
    /// <remarks>
    /// The file name has already been proved to carry no separator and no parent-directory segment, so the
    /// combination cannot leave the folder.
    /// </remarks>
    public string ResolveEntryAssemblyPath() => Path.Combine(Folder, Manifest.EntryAssembly);
}
