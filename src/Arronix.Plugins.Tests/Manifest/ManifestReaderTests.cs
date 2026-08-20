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
          "schemaVersion": 0,
          "id": "example",
          "name": "Example",
          "version": "0.1.0",
          "contracts": { "arronix": ">=0.3 <0.4" },
          "entryAssembly": "Arronix.Plugin.Example.dll",
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
          "schemaVersion": 0,
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

        manifest.SchemaVersion.Should().Be(0);
        manifest.Id.Should().Be("example");
        manifest.Name.Should().Be("Example");
        manifest.Version.Should().Be("0.1.0");
        manifest.Contracts.Arronix.Should().Be(">=0.3 <0.4");
        manifest.EntryAssembly.Should().Be("Arronix.Plugin.Example.dll");
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

    [TestCase("\"schemaVersion\": 0,")]
    [TestCase("\"id\": \"example\",")]
    [TestCase("\"name\": \"Example\",")]
    [TestCase("\"version\": \"0.1.0\",")]
    [TestCase("\"contracts\": { \"arronix\": \">=0.3 <0.4\" },")]
    [TestCase("\"entryAssembly\": \"Arronix.Plugin.Example.dll\",")]
    public void AnOmittedRequiredMemberIsRefused(string fragment)
    {
        var json = Minimal.Replace(fragment, string.Empty, StringComparison.Ordinal);

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
