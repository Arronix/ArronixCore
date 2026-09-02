using System;
using System.Collections.Generic;
using System.IO;

namespace Arronix.Installation.Tests;

/// <summary>
/// A fast, deterministic stand-in for the real SDK, used to prove the composer's staging, validation and
/// commit behaviour without paying for a real <c>dotnet publish</c> on every test.
/// </summary>
/// <remarks>
/// Each project file is a plain lookup key; it never has to name a real project on disk, because this type
/// never shells out. A test wires each key to the exact bytes a real publish would have produced for the
/// scenario it is proving — an entry assembly, a client index, a package manifest — or to a failure.
/// </remarks>
internal sealed class FakeDotNetCli : IDotNetCli
{
    private readonly Dictionary<string, Action<string>> _publishers = new(StringComparer.Ordinal);

    /// <summary>Gets every project file this fake was asked to publish, in call order.</summary>
    public List<string> PublishCalls { get; } = [];

    /// <summary>Registers what publishing <paramref name="projectFile"/> writes into its destination.</summary>
    /// <param name="projectFile">The lookup key a <see cref="PackageSource"/> or deliverable names.</param>
    /// <param name="write">Writes the published payload into the destination it is given.</param>
    public FakeDotNetCli Writing(string projectFile, Action<string> write)
    {
        _publishers[projectFile] = write;
        return this;
    }

    /// <summary>Registers <paramref name="projectFile"/> as one whose publish fails.</summary>
    /// <param name="projectFile">The lookup key.</param>
    /// <param name="message">The refusal message.</param>
    public FakeDotNetCli Failing(string projectFile, string message)
        => Writing(projectFile, _ => throw new InstallationException(message));

    /// <summary>Registers the standard server payload: one entry assembly and an appsettings.json.</summary>
    public FakeDotNetCli WithServer(string projectFile, string entryAssemblyFileName) => Writing(
        projectFile,
        destination =>
        {
            File.WriteAllBytes(Path.Combine(destination, entryAssemblyFileName), []);
            File.WriteAllText(Path.Combine(destination, "appsettings.json"), "{}");
        });

    /// <summary>Registers the standard client payload: a static root with an index page.</summary>
    public FakeDotNetCli WithClient(string projectFile) => Writing(
        projectFile,
        destination =>
        {
            var staticRoot = Path.Combine(destination, "wwwroot");
            Directory.CreateDirectory(staticRoot);
            File.WriteAllText(Path.Combine(staticRoot, "index.html"), "<html></html>");
        });

    /// <summary>Registers an ordinary package payload with a valid <c>plugin.json</c>.</summary>
    /// <param name="projectFile">The lookup key.</param>
    /// <param name="id">The package identifier its manifest declares.</param>
    /// <param name="dependencies">The package identifiers it declares as dependencies.</param>
    public FakeDotNetCli WithPackage(string projectFile, string id, params string[] dependencies) => Writing(
        projectFile,
        destination =>
        {
            var dependencyJson = string.Join(
                ",",
                Array.ConvertAll(dependencies, d => $$"""{"package":"{{d}}","range":">=0.1 <0.2"}"""));

            File.WriteAllText(
                Path.Combine(destination, "plugin.json"),
                $$"""
                {
                  "schemaVersion": 1,
                  "id": "{{id}}",
                  "name": "{{id}}",
                  "version": "1.0.0",
                  "dependencies": [{{dependencyJson}}]
                }
                """);
        });

    /// <inheritdoc />
    public string Version(string workingDirectory) => "fake-sdk";

    /// <inheritdoc />
    public void Publish(string projectFile, string destination, string workingDirectory)
    {
        PublishCalls.Add(projectFile);

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        Directory.CreateDirectory(destination);

        if (!_publishers.TryGetValue(projectFile, out var write))
        {
            throw new InstallationException($"The fake SDK was not told what publishing '{projectFile}' produces.");
        }

        write(destination);
    }
}
