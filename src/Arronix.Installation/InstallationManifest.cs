using System.IO;
using System.Text.Json;
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
/// folders it names are the ones <see cref="InstallationLayout"/> computes.
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
    /// <exception cref="InstallationException">There is no readable installation at that root.</exception>
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

        return manifest is null || manifest.SchemaVersion != CurrentSchemaVersion
            ? throw new InstallationException(
                $"The installation at '{layout.Root}' was written by a different version of this tool. "
                + "Compose it again.")
            : manifest;
    }
}
