using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Arronix.Common.Installation;

namespace Arronix.Installation;

/// <summary>One package an installation holds.</summary>
/// <param name="Id">The identifier its own manifest declares.</param>
/// <param name="Name">Its operator-facing name.</param>
/// <param name="Version">Its version.</param>
/// <param name="Project">The project that published it.</param>
/// <param name="Folder">Its folder, relative to the installation root.</param>
/// <param name="Role">What an evaluator should understand it to be.</param>
internal sealed record InstalledPackage(
    string Id,
    string Name,
    string Version,
    string Project,
    string Folder,
    PackageRole Role);

/// <summary>
/// What an installation currently holds, written by the composer and read by everything else.
/// </summary>
/// <remarks>
/// Every path is relative to the installation root, so an installation can be moved or copied and still
/// describe itself. It is a record of one composition, not a second source of truth about the layout: the
/// folders it names are the ones <see cref="InstallationLayout"/> computes, and <see cref="Validate"/> holds
/// it to exactly that computation rather than trusting whatever the file says.
/// </remarks>
/// <param name="SchemaVersion">The manifest schema version.</param>
/// <param name="Sdk">The .NET SDK version the deliverables were published with.</param>
/// <param name="ServerFolder">The published server's folder, relative to the root.</param>
/// <param name="ServerEntryAssembly">The server assembly to run.</param>
/// <param name="ClientStaticRoot">The client's static root, relative to the root.</param>
/// <param name="StoreDataSource">The database file, relative to the root.</param>
/// <param name="PackageStateFolder">The per-package state folder, relative to the root.</param>
/// <param name="Packages">The installed packages, in installation order.</param>
internal sealed record InstallationManifest(
    int SchemaVersion,
    string Sdk,
    string ServerFolder,
    string ServerEntryAssembly,
    string ClientStaticRoot,
    string StoreDataSource,
    string PackageStateFolder,
    IReadOnlyList<InstalledPackage> Packages)
{
    /// <summary>The schema version this build writes and accepts.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Writes the manifest into an installation.</summary>
    /// <param name="layout">The installation.</param>
    public void WriteTo(InstallationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        File.WriteAllText(layout.ManifestFile, JsonSerializer.Serialize(this, Format) + Environment.NewLine);
    }

    /// <summary>Reads the manifest an installation already has.</summary>
    /// <param name="layout">The installation.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="InstallationException">
    /// There is no readable installation at that root, or what is on disk does not match what the manifest
    /// declares.
    /// </exception>
    public static InstallationManifest ReadFrom(InstallationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (!File.Exists(layout.ManifestFile))
        {
            throw new InstallationException(
                $"There is no installation at '{layout.Root}'. Run it without --no-build to compose one.");
        }

        InstallationManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<InstallationManifest>(
                File.ReadAllText(layout.ManifestFile),
                Format);
        }
        catch (JsonException error)
        {
            throw new InstallationException(
                $"The installation manifest at '{layout.ManifestFile}' could not be read.",
                error);
        }

        if (manifest is null)
        {
            throw new InstallationException(
                $"The installation manifest at '{layout.ManifestFile}' is empty or not an object.");
        }

        manifest.Validate(layout);

        return manifest;
    }

    /// <summary>
    /// Proves this manifest actually describes what is on disk at <paramref name="layout"/>, so that a
    /// reader never trusts a schema version, a path or a package list the manifest merely states.
    /// </summary>
    /// <param name="layout">The installation, or a staged candidate laid out the same way.</param>
    /// <exception cref="InstallationException">The manifest and the disk disagree, or the layout is unsafe.</exception>
    /// <remarks>
    /// This is the one place identity, schema and path truth are enforced, so that the same rule protects a
    /// normal <c>run --no-build</c>, a destructive <c>reset</c>, and a freshly staged compose before it is
    /// ever promoted into the live installation.
    /// </remarks>
    public void Validate(InstallationLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InstallationException(
                $"The installation at '{layout.Root}' was written by a different version of this tool "
                + $"(schema {SchemaVersion}, expected {CurrentSchemaVersion}). Compose it again.");
        }

        RequireCanonicalPath(layout, "server folder", ServerFolder, layout.ServerFolder);
        RequireCanonicalPath(layout, "client static root", ClientStaticRoot, layout.ClientStaticRoot);
        RequireCanonicalPath(layout, "store data source", StoreDataSource, layout.StoreDataSource);
        RequireCanonicalPath(layout, "package state folder", PackageStateFolder, layout.PackageStateFolder);

        if (string.IsNullOrWhiteSpace(ServerEntryAssembly) || ServerEntryAssembly.Contains('/')
            || ServerEntryAssembly.Contains('\\'))
        {
            throw new InstallationException(
                $"The installation at '{layout.Root}' declares an invalid server entry assembly "
                + $"'{ServerEntryAssembly}'.");
        }

        var entryAssembly = Path.Combine(layout.ServerFolder, ServerEntryAssembly);

        if (!File.Exists(entryAssembly))
        {
            throw new InstallationException(
                $"The installation at '{layout.Root}' declares server entry assembly '{ServerEntryAssembly}', "
                + $"but there is no '{entryAssembly}'.");
        }

        var clientIndex = Path.Combine(layout.ClientStaticRoot, "index.html");

        if (!File.Exists(clientIndex))
        {
            throw new InstallationException(
                $"The installation at '{layout.Root}' has no published client at '{clientIndex}'.");
        }

        ValidatePackages(layout);
    }

    private void ValidatePackages(InstallationLayout layout)
    {
        var declaredIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var package in Packages)
        {
            if (string.IsNullOrWhiteSpace(package.Id) || !declaredIds.Add(package.Id))
            {
                throw new InstallationException(
                    $"The installation at '{layout.Root}' declares an invalid or duplicate package "
                    + $"identifier '{package.Id}'.");
            }

            string expectedFolder;

            try
            {
                expectedFolder = layout.PackageFolder(package.Id);
            }
            catch (ArgumentException error)
            {
                throw new InstallationException(
                    $"The installation at '{layout.Root}' declares invalid package identifier "
                    + $"'{package.Id}'.",
                    error);
            }

            var declaredFolder = Path.GetFullPath(Path.Combine(layout.Root, package.Folder));

            if (Path.IsPathRooted(package.Folder) || !layout.Contains(declaredFolder)
                || !string.Equals(declaredFolder, expectedFolder, StringComparison.Ordinal))
            {
                throw new InstallationException(
                    $"The installation at '{layout.Root}' declares package '{package.Id}' at "
                    + $"'{package.Folder}', which is not the one folder that identifier owns.");
            }

            var manifestFile = Path.Combine(expectedFolder, Deliverables.PackageManifestFileName);

            if (!File.Exists(manifestFile))
            {
                throw new InstallationException(
                    $"The installation at '{layout.Root}' declares package '{package.Id}', but "
                    + $"'{manifestFile}' does not exist.");
            }

            var declaredManifest = ReadPackageManifest(manifestFile);
            var actualId = (string?)declaredManifest?["id"];

            if (!string.Equals(actualId, package.Id, StringComparison.Ordinal))
            {
                throw new InstallationException(
                    $"'{manifestFile}' declares package id '{actualId}', not the installed '{package.Id}'. "
                    + "The installation manifest does not describe what would actually run.");
            }

            foreach (var dependency in DeclaredDependencyIds(declaredManifest))
            {
                if (!declaredIds.Contains(dependency) && Packages.All(p => p.Id != dependency))
                {
                    throw new InstallationException(
                        $"Package '{package.Id}' declares a dependency on '{dependency}', which the "
                        + $"installation at '{layout.Root}' does not install. Its dependency graph is "
                        + "incomplete.");
                }
            }
        }

        if (!Directory.Exists(layout.PackagesFolder))
        {
            if (declaredIds.Count > 0)
            {
                throw new InstallationException(
                    $"The installation at '{layout.Root}' declares packages, but '{layout.PackagesFolder}' "
                    + "does not exist.");
            }

            return;
        }

        var actualFolders = Directory.EnumerateDirectories(layout.PackagesFolder)
            .Select(Path.GetFileName)
            .Where(static name => name is { Length: > 0 })
            .Select(static name => name!)
            .ToHashSet(StringComparer.Ordinal);

        var undeclared = actualFolders.Except(declaredIds).Order(StringComparer.Ordinal).ToArray();

        if (undeclared.Length > 0)
        {
            throw new InstallationException(
                $"'{layout.PackagesFolder}' contains {string.Join(", ", undeclared)}, which the "
                + $"installation manifest at '{layout.ManifestFile}' does not declare. The manifest must "
                + "describe exactly what would run, so it cannot omit an installed package.");
        }
    }

    private static void RequireCanonicalPath(
        InstallationLayout layout,
        string what,
        string declared,
        string canonical)
    {
        var expected = Path.GetRelativePath(layout.Root, canonical).Replace(Path.DirectorySeparatorChar, '/');

        if (Path.IsPathRooted(declared) || !string.Equals(declared, expected, StringComparison.Ordinal))
        {
            throw new InstallationException(
                $"The installation at '{layout.Root}' declares an unexpected {what} '{declared}'; this "
                + $"tool's layout computes '{expected}'.");
        }
    }

    private static JsonNode? ReadPackageManifest(string manifestFile)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(manifestFile));
        }
        catch (JsonException error)
        {
            throw new InstallationException($"'{manifestFile}' is not readable JSON.", error);
        }
    }

    private static IEnumerable<string> DeclaredDependencyIds(JsonNode? manifest)
    {
        if (manifest?["dependencies"] is not JsonArray dependencies)
        {
            yield break;
        }

        foreach (var dependency in dependencies)
        {
            if ((string?)dependency?["package"] is { Length: > 0 } id)
            {
                yield return id;
            }
        }
    }
}
