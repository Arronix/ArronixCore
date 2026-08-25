using System.IO;
using System.Linq;
using System.Text.Json;
using Arronix.Plugins.Manifest;


namespace Arronix.Plugins.Tests.Manifest;

/// <summary>
/// Keeps the editor schema and the reader in step.
/// </summary>
/// <remarks>
/// <para>
/// <c>plugin.schema.json</c> says it is an editor aid and that the reader is authoritative, and both are
/// true — but it also sets <c>additionalProperties: false</c>, which means a member added to the reader and
/// not to the schema turns every shipped manifest red in an editor while the runtime happily loads it. That
/// is the worst of both: the aid is wrong exactly when somebody is relying on it. It happened, so it is
/// checked here rather than remembered.
/// </para>
/// <para>
/// The fixture does not implement JSON Schema. It checks the two properties that actually drift — the
/// member set the reader accepts, and the member set every first-party manifest uses — against the member
/// set the schema declares.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ManifestSchemaTests
{
    private static readonly JsonElement Schema = ReadJson(Path.Combine("src", "Arronix.Plugins", "Manifest", "plugin.schema.json"));

    /// <summary>Gets every first-party manifest in the working tree, for the parameterized case below.</summary>
    public static IEnumerable<string> FirstPartyManifests => Directory
        .EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "plugin.json", SearchOption.AllDirectories)
        .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .Select(path => Path.GetRelativePath(RepositoryRoot(), path))
        .Order(StringComparer.Ordinal)
        .ToArray();

    [Test]
    public void TheWorkingTreeContainsTheManifestsTheRulesAreAbout()
        => Assert.That(FirstPartyManifests, Is.Not.Empty);

    /// <summary>
    /// Every member the reader binds is offered by the schema.
    /// </summary>
    /// <remarks>
    /// Compared by the camel-cased spelling the reader uses on the wire, which is the spelling an author
    /// writes. A member the reader accepts and the schema does not is a member an editor will underline.
    /// </remarks>
    [Test]
    public void TheSchemaOffersEveryMemberTheReaderBinds()
    {
        var declared = Schema.GetProperty("properties").EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var bound = typeof(PluginManifest).GetProperties()
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .ToArray();

        Assert.That(
            bound.Where(member => !declared.Contains(member)).Order(StringComparer.Ordinal),
            Is.Empty,
            "plugin.schema.json sets additionalProperties:false, so a member the reader binds and the "
            + "schema does not know makes a valid manifest look invalid in an editor.");
    }

    /// <summary>
    /// Every member a first-party manifest actually writes is offered by the schema.
    /// </summary>
    /// <param name="manifestPath">The manifest, relative to the repository root.</param>
    [Test]
    [TestCaseSource(nameof(FirstPartyManifests))]
    public void TheSchemaAcceptsEveryMemberAFirstPartyManifestWrites(string manifestPath)
    {
        var declared = Schema.GetProperty("properties").EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var written = ReadJson(manifestPath).EnumerateObject().Select(property => property.Name).ToArray();

        Assert.That(
            written.Where(member => !declared.Contains(member)).Order(StringComparer.Ordinal),
            Is.Empty,
            $"'{manifestPath}' writes a member plugin.schema.json rejects.");
    }

    private static JsonElement ReadJson(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, path);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arronix.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "The repository root was not found from the test binary.");
        return directory!.FullName;
    }
}
