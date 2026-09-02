using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arronix.Common.Installation;

namespace Arronix.Installation;

/// <summary>
/// Builds one installation out of this repository's deliverables.
/// </summary>
/// <remarks>
/// <para>
/// This is the composition boundary the repository did not have. Every deliverable was individually
/// buildable and individually publishable, and nothing owned the step that puts them together: the server,
/// the client and the packages only became an installation inside a proof script, which is why the platform
/// could be green and still not be installable, and why running it meant somebody reconstructing four
/// environment variables by hand.
/// </para>
/// <para>
/// Two rules make this a composition rather than a copy. Every payload comes from a real
/// <c>dotnet publish</c>, so what is installed is the computed runtime closure rather than a build
/// directory MSBuild never pruned; and every destination is cleared first, so an assembly no current
/// project produces cannot survive into a new installation.
/// </para>
/// <para>
/// State is the deliberate exception. The database and the per-package state folders are what an
/// installation accumulates by being used, so composing over an existing installation replaces the code and
/// leaves the data alone. Emptying it is a separate, narrower operation.
/// </para>
/// </remarks>
internal sealed class InstallationComposer(DotNetCli dotnet, string repositoryRoot, InstallationLayout layout)
{
    /// <summary>
    /// Publishes the server, the client and the selected packages into the installation.
    /// </summary>
    /// <param name="packages">The packages to install.</param>
    /// <param name="log">Where progress is reported.</param>
    /// <returns>The manifest describing what was installed.</returns>
    public InstallationManifest Install(IReadOnlyList<InstallablePackage> packages, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(log);

        Directory.CreateDirectory(layout.Root);
        Directory.CreateDirectory(layout.StateFolder);
        Directory.CreateDirectory(layout.PackageStateFolder);

        log($"Publishing the server into {Relative(layout.ServerFolder)}");
        dotnet.Publish(
            Deliverables.ProjectFile(repositoryRoot, Deliverables.ServerProject),
            layout.ServerFolder,
            repositoryRoot);
        RecordInstallationRootInServerSettings();

        log($"Publishing the client into {Relative(layout.ClientFolder)}");
        dotnet.Publish(
            Deliverables.ProjectFile(repositoryRoot, Deliverables.ClientProject),
            layout.ClientFolder,
            repositoryRoot);

        if (!File.Exists(Path.Combine(layout.ClientStaticRoot, "index.html")))
        {
            throw new InstallationException(
                $"The client published without a static root at '{layout.ClientStaticRoot}'.");
        }

        // Cleared whole rather than per package: a package dropped from the deliverable set has to leave the
        // installation, and a folder nothing installs any more is exactly what a loader would still admit.
        if (Directory.Exists(layout.PackagesFolder))
        {
            Directory.Delete(layout.PackagesFolder, recursive: true);
        }

        Directory.CreateDirectory(layout.PackagesFolder);

        var installed = new List<InstalledPackage>(packages.Count);

        foreach (var package in packages)
        {
            var destination = layout.PackageFolder(package.Id);

            log($"Installing package {package.Id}");
            dotnet.Publish(
                Deliverables.ProjectFile(repositoryRoot, package.ProjectName),
                destination,
                repositoryRoot);

            installed.Add(Describe(package, destination));
        }

        var manifest = new InstallationManifest(
            InstallationManifest.CurrentSchemaVersion,
            dotnet.Version(repositoryRoot),
            Relative(layout.ServerFolder),
            Deliverables.ServerEntryAssembly,
            Relative(layout.ClientStaticRoot),
            Relative(layout.StoreDataSource),
            Relative(layout.PackageStateFolder),
            installed);

        manifest.WriteTo(layout);

        return manifest;
    }

    private InstalledPackage Describe(InstallablePackage package, string destination)
    {
        var manifestFile = Path.Combine(destination, Deliverables.PackageManifestFileName);

        if (!File.Exists(manifestFile))
        {
            throw new InstallationException(
                $"'{package.ProjectName}' published no {Deliverables.PackageManifestFileName}, so it is not "
                + "an installable package.");
        }

        JsonNode? declared;

        try
        {
            declared = JsonNode.Parse(File.ReadAllText(manifestFile));
        }
        catch (JsonException error)
        {
            throw new InstallationException($"'{manifestFile}' is not readable JSON.", error);
        }

        var id = (string?)declared?["id"];

        // The declared set says which folder a package occupies; the package's own manifest says who it is.
        // A disagreement means the installation would admit a package under a name nothing here chose.
        return !string.Equals(id, package.Id, StringComparison.Ordinal)
            ? throw new InstallationException(
                $"'{package.ProjectName}' declares package id '{id}', not the installed '{package.Id}'.")
            : new InstalledPackage(
                package.Id,
                (string?)declared?["name"] ?? package.Id,
                (string?)declared?["version"] ?? "unknown",
                package.ProjectName,
                Relative(destination),
                package.Role);
    }

    /// <summary>
    /// Tells the installed server which installation it is part of.
    /// </summary>
    /// <remarks>
    /// Written as <c>..</c> rather than as an absolute path, because a relative root resolves against the
    /// server's content root. An installation therefore stays correct when it is moved or copied, and it can
    /// be started directly from its own folder without anything being exported into the environment first.
    /// </remarks>
    private void RecordInstallationRootInServerSettings()
    {
        var settingsFile = Path.Combine(layout.ServerFolder, "appsettings.json");

        if (!File.Exists(settingsFile))
        {
            throw new InstallationException(
                $"The published server has no appsettings.json at '{settingsFile}'.");
        }

        var settings = JsonNode.Parse(File.ReadAllText(settingsFile))?.AsObject()
            ?? throw new InstallationException($"'{settingsFile}' is not a JSON object.");

        var arronix = settings["Arronix"]?.AsObject();

        if (arronix is null)
        {
            arronix = new JsonObject();
            settings["Arronix"] = arronix;
        }

        arronix["Installation"] = new JsonObject { ["Root"] = ".." };

        File.WriteAllText(
            settingsFile,
            settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private string Relative(string path)
        => Path.GetRelativePath(layout.Root, path).Replace(Path.DirectorySeparatorChar, '/');
}
