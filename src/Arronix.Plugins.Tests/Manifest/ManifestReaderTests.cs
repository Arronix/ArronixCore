using System.IO;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Plugins.Manifest;


namespace Arronix.Plugins.Tests.Manifest;

/// <summary>
/// The reader is the schema validation.
/// </summary>
/// <remarks>
/// These cases are the ones a separate schema document would have covered. Asserting them against the
/// reader rather than against a schema is the point: there is one definition of the shape, so the check
/// cannot drift from the types the loader actually acts on.
/// </remarks>
[TestFixture]
public sealed class ManifestReaderTests
{
    private const string Complete =
        """
        {
          "schemaVersion": 1,
          "id": "example",
          "name": "Example",
          "version": "0.1.0",
          "contracts": { "arronix": ">=0.3 <0.4" },
          "entryAssembly": "Arronix.Plugin.Example.dll",
          "contractAssemblies": ["Example.Contracts.dll"],
          "dependencies": [{ "package": "example.contracts", "range": ">=0.1 <0.2" }],
          "mediaKinds": ["example"],
          "identifiers": ["exdb"],
          "capabilities": ["media-kind", "parsing"],
          "tokens": [
            { "name": "{Title}", "description": "The title", "exampleValue": "A Title", "isRequired": true }
          ],
          "policies": {
            "parsing": ["Alpha", "Beta"],
            "naming": ["Standard"]
          }
        }
        """;

    private const string Minimal =
        """
        {
          "schemaVersion": 1,
          "id": "example",
          "name": "Example",
          "version": "0.1.0",
          "contracts": { "arronix": ">=0.3 <0.4" },
          "entryAssembly": "Arronix.Plugin.Example.dll",
          "capabilities": ["parsing"]
        }
        """;

    [Test]
    public void ACompleteDeclarationIsReadWhole()
    {
        var manifest = PluginManifestReader.Read(Complete, "test");

        manifest.SchemaVersion.Should().Be(1);
        manifest.Id.Should().Be("example");
        manifest.Name.Should().Be("Example");
        manifest.Version.Should().Be("0.1.0");
        manifest.Contracts.Arronix.Should().Be(">=0.3 <0.4");
        manifest.EntryAssembly.Should().Be("Arronix.Plugin.Example.dll");
        manifest.ContractAssemblies.Should().Equal("Example.Contracts.dll");
        manifest.Dependencies.Should().ContainSingle();
        manifest.Dependencies[0].Package.Should().Be("example.contracts");
        manifest.Dependencies[0].Range.Should().Be(">=0.1 <0.2");
        manifest.MediaKinds.Should().Equal("example");
        manifest.Identifiers.Should().Equal("exdb");
        manifest.Capabilities.Should().Equal("media-kind", "parsing");
        manifest.Tokens.Should().ContainSingle();
        manifest.Tokens[0].Name.Should().Be("{Title}");
        manifest.Tokens[0].Description.Should().Be("The title");
        manifest.Tokens[0].IsRequired.Should().BeTrue();
        manifest.Policies!.Parsing.Should().Equal("Alpha", "Beta");
        manifest.Policies.Naming.Should().Equal("Standard");
        manifest.Policies.Matching.Should().BeEmpty();
    }

    [Test]
    public void TheOptionalMembersDefaultToEmptyRatherThanNull()
    {
        var manifest = PluginManifestReader.Read(Minimal, "test");

        manifest.MediaKinds.Should().BeEmpty();
        manifest.Identifiers.Should().BeEmpty();
        manifest.Tokens.Should().BeEmpty();
        manifest.ContractAssemblies.Should().BeEmpty();
        manifest.Dependencies.Should().BeEmpty();
        manifest.Policies.Should().BeNull();
    }

    [Test]
    public void ABareTokenStringIsWidenedSoTheDocumentedExampleStillParses()
    {
        var json = Minimal.Replace(
            "\"capabilities\": [\"parsing\"]",
            "\"capabilities\": [\"parsing\"],\n  \"tokens\": [\"{Title}\"]",
            StringComparison.Ordinal);

        var manifest = PluginManifestReader.Read(json, "test");

        manifest.Tokens.Should().ContainSingle();
        manifest.Tokens[0].Name.Should().Be("{Title}");
        manifest.Tokens[0].Description.Should().BeEmpty();
        manifest.Tokens[0].IsRequired.Should().BeFalse();
    }

    [Test]
    public void CommentsAreAllowedBecauseOperatorsEditTheseFilesByHand()
    {
        var json = Minimal.Insert(1, "\n  // why this extension exists\n");

        var read = () => PluginManifestReader.Read(json, "test");

        read.Should().NotThrow();
    }

    [TestCase("\"capabilities\"", "\"capability\"", TestName = "A misspelled member is refused rather than ignored")]
    public void AnUnmappedMemberIsRefused(string original, string replacement)
    {
        var json = Minimal.Replace(original, replacement, StringComparison.Ordinal);

        ShouldBeManifestInvalid(() => PluginManifestReader.Read(json, "test"));
    }

    [TestCase("\"schemaVersion\": 1,")]
    [TestCase("\"id\": \"example\",")]
    [TestCase("\"name\": \"Example\",")]
    [TestCase("\"version\": \"0.1.0\",")]
    [TestCase("\"contracts\": { \"arronix\": \">=0.3 <0.4\" },")]
    public void AnOmittedRequiredMemberIsRefused(string fragment)
    {
        var json = Minimal.Replace(fragment, string.Empty, StringComparison.Ordinal);

        ShouldBeManifestInvalid(() => PluginManifestReader.Read(json, "test"));
    }

    /// <summary>
    /// A package with no executable behavior omits the entry assembly, and one with no privileges omits the
    /// capability list. Both are package shapes rather than omissions, so the reader accepts them and the
    /// validator — which can name the member at fault — decides whether the combination makes sense.
    /// </summary>
    [Test]
    public void ThePackageShapeMembersAreReadRatherThanRequired()
    {
        var json = Minimal
            .Replace("  \"entryAssembly\": \"Arronix.Plugin.Example.dll\",\n", string.Empty, StringComparison.Ordinal)
            .Replace(
                "\"capabilities\": [\"parsing\"]",
                "\"contractAssemblies\": [\"Example.Contracts.dll\"]",
                StringComparison.Ordinal);

        var manifest = PluginManifestReader.Read(json, "test");

        manifest.EntryAssembly.Should().BeNull();
        manifest.Capabilities.Should().BeEmpty();
        manifest.ContractAssemblies.Should().Equal("Example.Contracts.dll");
    }

    [Test]
    public void ADependencyIsReadAsAnExactPackageAndOneRange()
    {
        var json = Minimal.Replace(
            "\"capabilities\": [\"parsing\"]",
            "\"capabilities\": [\"parsing\"],\n  \"dependencies\": [{ \"package\": \"example.contracts\", \"range\": \">=0.1 <0.2\" }]",
            StringComparison.Ordinal);

        var manifest = PluginManifestReader.Read(json, "test");

        manifest.Dependencies.Should().ContainSingle();
        manifest.Dependencies[0].Package.Should().Be("example.contracts");
        manifest.Dependencies[0].Range.Should().Be(">=0.1 <0.2");
    }

    [TestCase("{ \"package\": \"example.contracts\" }", TestName = "A dependency with no range is refused")]
    [TestCase("{ \"range\": \">=0.1 <0.2\" }", TestName = "A dependency naming no package is refused")]
    [TestCase("{ \"package\": \"example.contracts\", \"range\": \">=0.1\", \"facet\": \"contract\" }", TestName = "A dependency member the format does not define is refused")]
    public void AnIncompleteDependencyIsRefused(string entry)
    {
        var json = Minimal.Replace(
            "\"capabilities\": [\"parsing\"]",
            $"\"capabilities\": [\"parsing\"],\n  \"dependencies\": [{entry}]",
            StringComparison.Ordinal);

        ShouldBeManifestInvalid(() => PluginManifestReader.Read(json, "test"));
    }

    [Test]
    public void AnUnknownPolicyCategoryIsRefusedByTheTypeItself()
    {
        var json = Minimal.Replace(
            "\"capabilities\": [\"parsing\"]",
            "\"capabilities\": [\"parsing\"],\n  \"policies\": { \"invented\": [\"Alpha\"] }",
            StringComparison.Ordinal);

        ShouldBeManifestInvalid(() => PluginManifestReader.Read(json, "test"));
    }

    [Test]
    public void MalformedJsonIsRefusedWithTheOriginNamed()
    {
        var failure = ShouldBeManifestInvalid(() => PluginManifestReader.Read("{ not json", "/somewhere/plugin.json"));

        failure.Message.Should().Contain("/somewhere/plugin.json");
    }

    [Test]
    public void AMissingFileIsRefusedRatherThanIgnored()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arronix-absent-{Guid.NewGuid():N}", "plugin.json");

        ShouldBeManifestInvalid(() => PluginManifestReader.ReadFile(path));
    }

    [Test]
    public void AnEmptyFileIsRefused()
    {
        var folder = Directory.CreateTempSubdirectory("arronix-manifest").FullName;
        var path = Path.Combine(folder, "plugin.json");
        File.WriteAllText(path, "   ");

        try
        {
            ShouldBeManifestInvalid(() => PluginManifestReader.ReadFile(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static ArronixException ShouldBeManifestInvalid(Func<PluginManifest> read)
    {
        var failure = read.Should().Throw<ArronixException>().Which;

        failure.ErrorCode.Should().Be(CoreErrorCode.PluginManifestInvalid);
        return failure;
    }
}
