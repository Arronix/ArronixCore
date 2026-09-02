using System.IO;

namespace Arronix.Common.Installation;

/// <summary>
/// The one shape an installed Arronix has on disk.
/// </summary>
/// <remarks>
/// <para>
/// An installation is a directory holding the server, the published client, the installed packages, and the
/// durable state those two produce. Until this type existed, that arrangement was not owned anywhere: the
/// server read four unrelated settings — a package folder, a package state folder, a database file and a
/// client root — and only something outside the product could make them describe the same installation.
/// That is why the platform could be built and tested without ever being installable, and why running it
/// meant reconstructing an environment by hand.
/// </para>
/// <para>
/// The layout is a value, not a service. It computes paths and creates nothing, so the composer that builds
/// an installation, the server that runs inside one, and the reset that empties one all agree by
/// construction rather than by convention.
/// </para>
/// <para>
/// This is the on-disk installation. It is unrelated to the client's contract <i>installation</i>, which is
/// the set of shared contract assemblies a browser page has admitted; the two words are kept apart by
/// living in different assemblies with no reference between them.
/// </para>
/// </remarks>
public sealed class InstallationLayout
{
    /// <summary>The folder holding the published server.</summary>
    public const string ServerFolderName = "server";

    /// <summary>The folder holding the published client.</summary>
    public const string ClientFolderName = "client";

    /// <summary>The folder holding the client's static files inside its published output.</summary>
    public const string ClientStaticFolderName = "wwwroot";

    /// <summary>The folder holding one subfolder per installed package.</summary>
    public const string PackagesFolderName = "packages";

    /// <summary>The folder each installed package's own data, cache and scratch folders are laid out under.</summary>
    public const string PackageStateFolderName = "package-state";

    /// <summary>The folder holding the host's own durable state.</summary>
    public const string StateFolderName = "state";

    /// <summary>The local database file the durable seams read and write.</summary>
    public const string StoreFileName = "arronix.db";

    /// <summary>The file describing what an installation currently holds.</summary>
    public const string ManifestFileName = "installation.json";

    private InstallationLayout(string root)
    {
        Root = root;
        ServerFolder = Path.Combine(root, ServerFolderName);
        ClientFolder = Path.Combine(root, ClientFolderName);
        ClientStaticRoot = Path.Combine(ClientFolder, ClientStaticFolderName);
        PackagesFolder = Path.Combine(root, PackagesFolderName);
        PackageStateFolder = Path.Combine(root, PackageStateFolderName);
        StateFolder = Path.Combine(root, StateFolderName);
        StoreDataSource = Path.Combine(StateFolder, StoreFileName);
        ManifestFile = Path.Combine(root, ManifestFileName);
    }

    /// <summary>Gets the installation's root directory, fully qualified.</summary>
    public string Root { get; }

    /// <summary>Gets the folder the published server lives in.</summary>
    public string ServerFolder { get; }

    /// <summary>Gets the folder the published client lives in.</summary>
    public string ClientFolder { get; }

    /// <summary>Gets the client's static file root, which is what the server serves.</summary>
    public string ClientStaticRoot { get; }

    /// <summary>Gets the folder holding one subfolder per installed package.</summary>
    public string PackagesFolder { get; }

    /// <summary>Gets the folder each installed package's own folders are laid out under.</summary>
    public string PackageStateFolder { get; }

    /// <summary>Gets the folder holding the host's own durable state.</summary>
    public string StateFolder { get; }

    /// <summary>Gets the local database file.</summary>
    public string StoreDataSource { get; }

    /// <summary>Gets the file describing what this installation holds.</summary>
    public string ManifestFile { get; }

    /// <summary>
    /// Reads the layout of the installation rooted at a path.
    /// </summary>
    /// <param name="root">
    /// The installation root. A relative path is resolved against <paramref name="basePath"/> when one is
    /// supplied, and against the process working directory otherwise.
    /// </param>
    /// <param name="basePath">The directory a relative root is resolved against.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is <see langword="null"/>, empty or white space.</exception>
    public static InstallationLayout At(string root, string? basePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var resolved = string.IsNullOrWhiteSpace(basePath) || Path.IsPathRooted(root)
            ? Path.GetFullPath(root)
            : Path.GetFullPath(Path.Combine(basePath, root));

        return new InstallationLayout(resolved);
    }

    /// <summary>
    /// Gets the folder one installed package occupies.
    /// </summary>
    /// <param name="packageId">The package identifier, as its manifest declares it.</param>
    /// <returns>The folder path.</returns>
    /// <exception cref="ArgumentException"><paramref name="packageId"/> is <see langword="null"/>, empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="packageId"/> is not one path segment, so it could name a directory outside the
    /// installation.
    /// </exception>
    public string PackageFolder(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        var combined = Path.GetFullPath(Path.Combine(PackagesFolder, packageId));
        var expected = Path.Combine(PackagesFolder, packageId);

        if (!string.Equals(combined, Path.GetFullPath(expected), StringComparison.Ordinal)
            || Path.GetDirectoryName(combined) != Path.GetFullPath(PackagesFolder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(packageId),
                packageId,
                "A package identifier must name exactly one folder directly inside the packages folder.");
        }

        return combined;
    }

    /// <summary>
    /// Determines whether a path is inside this installation, so that a destructive operation can refuse
    /// anything it does not own.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the path is the root or beneath it.</returns>
    public bool Contains(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Root);

        return string.Equals(candidate, root, StringComparison.Ordinal)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
