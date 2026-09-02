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
/// directory MSBuild never pruned; and the live installation is never touched until a whole new generation
/// has been built and validated in a sibling staging directory, so a publish that fails partway leaves the
/// last good installation exactly as it was.
/// </para>
/// <para>
/// State is the deliberate exception. The database and the per-package state folders are what an
/// installation accumulates by being used, so composing over an existing installation replaces the code and
/// leaves the data alone. Emptying it is a separate, narrower operation.
/// </para>
/// </remarks>
internal sealed class InstallationComposer(IDotNetCli dotnet, string repositoryRoot, InstallationLayout layout)
{
    private const string StagingSuffix = ".staging";
    private const string PreviousSuffix = ".previous";
    private const string FailedSuffix = ".failed";

    /// <summary>
    /// Publishes the server, the client and the selected packages into the installation.
    /// </summary>
    /// <param name="packages">The packages to install.</param>
    /// <param name="log">Where progress is reported.</param>
    /// <returns>The manifest describing what was installed.</returns>
    /// <exception cref="InstallationException">
    /// The new generation could not be built or did not validate. The installation this call started from,
    /// if any, is unchanged.
    /// </exception>
    public InstallationManifest Install(IReadOnlyList<PackageSource> packages, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(log);

        var stagingRoot = layout.Root + StagingSuffix;

        if (Directory.Exists(stagingRoot))
        {
            // Tool-owned scratch space from an interrupted prior compose. Safe to discard: nothing here was
            // ever promoted, or this run would not still find it under the staging suffix.
            Directory.Delete(stagingRoot, recursive: true);
        }

        var staging = InstallationLayout.At(stagingRoot);
        InstallationManifest manifest;

        try
        {
            manifest = Stage(staging, packages, log);
        }
        catch
        {
            SafeDelete(stagingRoot);
            throw;
        }

        Commit(staging, log);

        return manifest;
    }

    private InstallationManifest Stage(
        InstallationLayout staging,
        IReadOnlyList<PackageSource> packages,
        Action<string> log)
    {
        Directory.CreateDirectory(staging.Root);

        log($"Publishing the server into {Relative(staging, staging.ServerFolder)}");
        dotnet.Publish(Deliverables.ProjectFile(repositoryRoot, Deliverables.ServerProject), staging.ServerFolder, repositoryRoot);
        RecordInstallationRootInServerSettings(staging);

        log($"Publishing the client into {Relative(staging, staging.ClientFolder)}");
        dotnet.Publish(Deliverables.ProjectFile(repositoryRoot, Deliverables.ClientProject), staging.ClientFolder, repositoryRoot);

        Directory.CreateDirectory(staging.PackagesFolder);

        var installed = new List<InstalledPackage>(packages.Count);

        foreach (var package in packages)
        {
            var destination = staging.PackageFolder(package.Id);

            log($"Installing package {package.Id}");
            dotnet.Publish(package.ProjectFile, destination, repositoryRoot);

            installed.Add(Describe(package, destination, staging));
        }

        var manifest = new InstallationManifest(
            InstallationManifest.CurrentSchemaVersion,
            dotnet.Version(repositoryRoot),
            Relative(staging, staging.ServerFolder),
            Deliverables.ServerEntryAssembly,
            Relative(staging, staging.ClientStaticRoot),
            Relative(staging, staging.StoreDataSource),
            Relative(staging, staging.PackageStateFolder),
            installed);

        // Validated against the staging layout before a single live file is touched. This is what "never
        // clear the live payload before the replacement is built and validated" actually means in code: the
        // entry assembly, the client index, every package manifest and the whole dependency graph are all
        // checked here, against the exact bytes about to be promoted.
        manifest.Validate(staging);
        manifest.WriteTo(staging);

        return manifest;
    }

    private void Commit(InstallationLayout staging, Action<string> log)
    {
        Directory.CreateDirectory(layout.Root);
        Directory.CreateDirectory(layout.StateFolder);
        Directory.CreateDirectory(layout.PackageStateFolder);

        var entries = new (string Staged, string Live)[]
        {
            (staging.ServerFolder, layout.ServerFolder),
            (staging.ClientFolder, layout.ClientFolder),
            (staging.PackagesFolder, layout.PackagesFolder),
            (staging.ManifestFile, layout.ManifestFile),
        };

        var promoted = new List<string>(entries.Length);

        try
        {
            foreach (var (staged, live) in entries)
            {
                PromoteEntry(staged, live);
                promoted.Add(live);
            }
        }
        catch (Exception failure)
        {
            var rollback = RollBack(promoted);

            throw new InstallationException(
                "Composing a new installation generation succeeded, but replacing the previous one with it "
                + $"did not. {rollback} The staged generation was left at '{staging.Root}' for inspection; "
                + "it is not part of the live installation.",
                failure);
        }

        foreach (var (_, live) in entries)
        {
            SafeDelete(live + PreviousSuffix);
        }

        SafeDelete(staging.Root);
        log($"Installed into {layout.Root}");
    }

    /// <summary>
    /// Moves one staged entry into place, keeping whatever it replaces as a bounded, tool-owned backup.
    /// </summary>
    /// <remarks>
    /// This call is itself all-or-nothing: either it ends with the staged content live and the old content
    /// backed up beside it, or it ends with <paramref name="live"/> exactly as it was before the call, never
    /// with <paramref name="live"/> missing or half-written. That is what lets <see cref="Commit"/> reason
    /// about a batch of these calls in terms of "how many fully succeeded" — a failure never leaves the one
    /// entry it happened on in an in-between state for <see cref="RollBack"/> to also have to know about.
    /// Exposed at internal visibility so this behaviour can be proved directly against plain temporary
    /// directories, without paying for a real publish.
    /// </remarks>
    internal static void PromoteEntry(string staged, string live)
    {
        if (!Directory.Exists(staged) && !File.Exists(staged))
        {
            throw new InstallationException($"The staged generation has nothing at '{staged}' to promote.");
        }

        var backup = live + PreviousSuffix;
        var hadLive = Directory.Exists(live) || File.Exists(live);

        SafeDelete(backup);

        if (Directory.Exists(live))
        {
            Directory.Move(live, backup);
        }
        else if (File.Exists(live))
        {
            File.Move(live, backup);
        }

        try
        {
            if (Directory.Exists(staged))
            {
                Directory.Move(staged, live);
            }
            else
            {
                File.Move(staged, live);
            }
        }
        catch
        {
            if (hadLive)
            {
                if (Directory.Exists(backup))
                {
                    Directory.Move(backup, live);
                }
                else
                {
                    File.Move(backup, live);
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Best-effort restoration of every live entry a failed commit had already replaced. Internal for the
    /// same reason as <see cref="PromoteEntry"/>.
    /// </summary>
    /// <returns>One sentence describing whether the rollback itself succeeded.</returns>
    internal static string RollBack(IReadOnlyList<string> promotedLivePaths)
    {
        var failures = new List<string>();

        foreach (var live in promotedLivePaths)
        {
            var backup = live + PreviousSuffix;

            if (!Directory.Exists(backup) && !File.Exists(backup))
            {
                // Nothing was backed up because nothing lived here before this compose; there is nothing to
                // roll back to. Leaving the newly promoted entry in place is the least-wrong outcome, since
                // it was fully validated staged content.
                continue;
            }

            try
            {
                var failedAside = live + FailedSuffix;
                SafeDelete(failedAside);

                if (Directory.Exists(live))
                {
                    Directory.Move(live, failedAside);
                }
                else if (File.Exists(live))
                {
                    File.Move(live, failedAside);
                }

                if (Directory.Exists(backup))
                {
                    Directory.Move(backup, live);
                }
                else
                {
                    File.Move(backup, live);
                }
            }
            catch (Exception rollbackFailure)
            {
                failures.Add($"'{live}' ({rollbackFailure.Message})");
            }
        }

        return failures.Count == 0
            ? "The previous installation was restored."
            : $"The previous installation could not be fully restored: {string.Join("; ", failures)}.";
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of tool-owned staging or backup space. Leaving a stray directory behind is
            // not a reason to fail a compose that otherwise succeeded.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }

    private InstalledPackage Describe(PackageSource package, string destination, InstallationLayout staging)
    {
        var manifestFile = Path.Combine(destination, Deliverables.PackageManifestFileName);

        if (!File.Exists(manifestFile))
        {
            throw new InstallationException(
                $"'{package.ProjectFile}' published no {Deliverables.PackageManifestFileName}, so it is not "
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

        // The requested identifier says which folder a package occupies; the package's own manifest says who
        // it is. A disagreement means the installation would admit a package under a name nothing here chose.
        return !string.Equals(id, package.Id, StringComparison.Ordinal)
            ? throw new InstallationException(
                $"'{package.ProjectFile}' declares package id '{id}', not the installed '{package.Id}'.")
            : new InstalledPackage(
                package.Id,
                (string?)declared?["name"] ?? package.Id,
                (string?)declared?["version"] ?? "unknown",
                Path.GetFileNameWithoutExtension(package.ProjectFile),
                Relative(staging, destination),
                package.Role);
    }

    /// <summary>
    /// Tells the installed server which installation it is part of.
    /// </summary>
    /// <remarks>
    /// Written as <c>..</c> rather than as an absolute path, because a relative root resolves against the
    /// server's content root. An installation therefore stays correct when it is moved or copied, and it can
    /// be started directly from its own folder without anything being exported into the environment first.
    /// This is written into the staged server before it is ever promoted, so it is true from the moment the
    /// server folder becomes live.
    /// </remarks>
    private static void RecordInstallationRootInServerSettings(InstallationLayout staging)
    {
        var settingsFile = Path.Combine(staging.ServerFolder, "appsettings.json");

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

    private static string Relative(InstallationLayout staging, string path)
        => Path.GetRelativePath(staging.Root, path).Replace(Path.DirectorySeparatorChar, '/');
}
