using System.IO;
using System.Linq;
using System.Text.Json;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Shape;

/// <summary>
/// What the manifest is allowed to own, and what it must no longer restate.
/// </summary>
/// <remarks>
/// <para>
/// This fixture replaces the mirror it used to be. While the loader compared a manifest's media kinds and
/// tokens against a media shape, Movies had to carry a hand-maintained copy of both and something had to
/// check the copy — so the fixture checked it, and the copy stayed. The runtime now reads the projection
/// the host admitted, which makes the copy pure duplication: a second media definition that can only drift.
/// </para>
/// <para>
/// So the assertions are inverted. A derivable fact must be absent from <c>plugin.json</c>, and the derived
/// vocabulary is still checked — against the media type, which is where it comes from.
/// </para>
/// <para>
/// The manifest is read from the working tree rather than from the build output, because it is not copied
/// alongside a referencing test assembly.
/// </para>
/// </remarks>
[TestFixture]
public class ManifestOwnershipTests
{
    /// <summary>
    /// Media facts compiled from the media definition. None may be restated in the manifest.
    /// </summary>
    /// <remarks>
    /// <c>actions</c> has never been a manifest key and is listed so it cannot quietly become one. Platform
    /// actions are derived by the host from the compiled media definition; a manifest naming them would be
    /// an extension declaring the platform's own operation catalogue.
    /// </remarks>
    private static readonly string[] MediaDefinitionProperties =
    [
        "mediaKinds",
        "tokens",
        "policies",
        "actions"
    ];

    /// <summary>
    /// Everything the manifest genuinely owns: what the loader cannot learn from code it has not been
    /// allowed to run yet.
    /// </summary>
    private static readonly string[] ManifestOwnedProperties =
    [
        "schemaVersion",
        "id",
        "name",
        "version",
        "description",
        "contracts",
        "entryAssembly",
        "capabilities"
    ];

    private static JsonElement Manifest { get; } = ReadManifest();

    [TestCaseSource(nameof(MediaDefinitionProperties))]
    public void RestatesNoDerivedMediaFact(string property)
        => Assert.That(
            Manifest.TryGetProperty(property, out _),
            Is.False,
            $"'{property}' is derived from the media type and settled against the projection the host "
            + "admitted. Restating it in the manifest makes the manifest a second media schema.");

    [Test]
    public void ClaimsNoCatalogerOwnedIdentifierVocabulary()
        => Assert.That(
            Manifest.TryGetProperty("identifiers", out _),
            Is.False,
            "Movies owns identity roles, while installed catalogers own the external schemes and release "
            + "markers which can fill them. The media package must not bake a provider vocabulary into its manifest.");

    [Test]
    public void CarriesNothingButWhatItOwns()
        => Assert.That(
            Manifest.EnumerateObject().Select(static property => property.Name),
            Is.SubsetOf(ManifestOwnedProperties));

    [Test]
    public void DeclaresTheSameExtensionIdentifierAsTheCode()
        => Assert.That(Manifest.GetProperty("id").GetString(), Is.EqualTo(new MoviesPluginModule().Id.Value));

    [Test]
    public void DeclaresTheContractRangeTheFirstPartyLineShips()
        => Assert.That(
            Manifest.GetProperty("contracts").GetProperty("arronix").GetString(),
            Is.EqualTo(">=0.8 <0.9"));

    [Test]
    public void NamesTheAssemblyTheModuleActuallyLivesIn()
        => Assert.That(
            Manifest.GetProperty("entryAssembly").GetString(),
            Is.EqualTo(typeof(MoviesPluginModule).Assembly.GetName().Name + ".dll"));

    [Test]
    public void NamesTheExtensionAndSaysWhatItIsFor()
        => Assert.Multiple(() =>
        {
            Assert.That(Manifest.GetProperty("name").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(Manifest.GetProperty("version").GetString(), Is.EqualTo("0.1.0"));
            Assert.That(Manifest.GetProperty("description").GetString(), Is.Not.Null.And.Not.Empty);
        });

    /// <summary>
    /// The capability list stays. It is an admission check in both directions — a registration the manifest
    /// does not cover is refused, and a capability nothing uses quarantines the extension — and neither
    /// direction can be derived from code the loader has not yet been allowed to run.
    /// </summary>
    [Test]
    public void AsksForExactlyTheCapabilitiesTheModuleExercises()
        => Assert.That(
            Strings("capabilities"),
            Is.EquivalentTo(new[]
            {
                "media-kind", "parsing", "matching", "indexing",
                "quality", "renaming", "notification"
            }));

    [TestCase("network")]
    [TestCase("storage")]
    public void AsksForNoPrivilegeItDoesNotUse(string privilege)
        => Assert.That(Strings("capabilities"), Does.Not.Contain(privilege));

    /// <summary>The media type declares identity roles but owns no catalog scheme.</summary>
    [Test]
    public void NamesNoCatalogScheme()
        => Assert.Multiple(() =>
        {
            Assert.That(
                MoviesDeclaration.Level.Identity.ExternalIds,
                Is.Empty,
                "The level declares roles; which schemes fill them is a fact about the installed catalogers.");
            Assert.That(
                MoviesDeclaration.Shape.GroupingAxes.SelectMany(static axis => axis.ExternalIds),
                Is.Empty,
                "And the axis likewise, for its own key space.");
        });

    /// <summary>
    /// No token is required any more, and that is a closed defect rather than a relaxation: the rule about
    /// what a file template must contain is a disjunction with an exclusivity between its branches, which a
    /// per-token flag could express neither half of. It is a predicate on the model now.
    /// </summary>
    [Test]
    public void MarksNoTokenRequiredBecauseTheRuleIsAPredicate()
        => Assert.Multiple(() =>
        {
            Assert.That(
                MoviesDeclaration.Shape.Tokens.Where(static token => token.IsRequired),
                Is.Empty);
            Assert.That(
                MoviesDeclaration.Carried.TemplateRules.Select(static rule => rule.RuleId),
                Is.EqualTo(new[] { "names-the-movie-or-the-original-file" }));
        });

    [Test]
    public void DescribesEveryToken()
    {
        foreach (var token in MoviesDeclaration.Shape.Tokens)
        {
            Assert.That(token.Description, Is.Not.Null.And.Not.Empty, token.Name);
            Assert.That(token.Name, Does.StartWith("{").And.EndWith("}"), token.Name);
        }
    }

    /// <summary>
    /// <b>What derivation cannot produce, pinned rather than papered over.</b> A worked example is prose,
    /// and prose is not in an assembly at run time, so every example comes from an attribute on the property
    /// the token was derived from. The four host title transforms have no property to carry one, so they
    /// have no example and cannot be given one without the host owning the transform vocabulary's prose.
    /// </summary>
    [Test]
    public void GivesAnExampleToEveryTokenAPropertyCouldSupplyOneFor()
    {
        var withoutExample = MoviesDeclaration.Shape.Tokens
            .Where(static token => string.IsNullOrEmpty(token.ExampleValue))
            .Select(static token => token.Name)
            .ToArray();

        Assert.That(
            withoutExample,
            Is.EquivalentTo(new[]
            {
                // The surrogate key and the identifier set: tokens nobody would type, derived because the
                // derivation makes a token of every nameable field.
                "{Movie Key}",
                "{Movie ExternalIds}",

                // Fields with no worked example written on them, which is a judgement rather than a gap.
                "{Movie TitleLanguage}",
                "{Movie OriginalLanguage}",
                "{Movie AlternateTitles}",
                "{Movie Status}",
                "{Movie CatalogState}",
                "{Movie Overview}",
                "{Movie Genres}",
                "{Movie Keywords}",
                "{Movie Website}",
                "{Movie Preview}",
                "{Movie Popularity}",

                // The host's own title transforms, for the item and for the group. These are the ones that
                // cannot be fixed from here: the derivation hard-codes an empty example for them.
                "{Movie TitleClean}",
                "{Movie TitleThe}",
                "{Movie TitleCleanThe}",
                "{Movie TitleFirstCharacter}",
                "{Collection TitleClean}",
                "{Collection TitleThe}",
                "{Collection TitleCleanThe}",
                "{Collection TitleFirstCharacter}",
            }));
    }

    [Test]
    public void DeclaresEveryTokenExactlyOnce()
        => Assert.That(MoviesDeclaration.Shape.Tokens.Select(static token => token.Name), Is.Unique);

    /// <summary>
    /// Every token the kind publishes is derived from something the kind owns: one of its own fields, one
    /// of its group's, or the per-file facet its format family declares. The three hand-maintained
    /// partitions this fixture used to check are gone with the list they partitioned.
    /// </summary>
    [Test]
    public void PublishesOnlyTokensDerivedFromWhatTheKindOwns()
    {
        foreach (var token in MoviesDeclaration.Shape.Tokens)
        {
            Assert.That(
                token.Name.StartsWith("{Movie ", StringComparison.Ordinal)
                || token.Name.StartsWith("{Collection ", StringComparison.Ordinal)
                || token.Name.StartsWith("{Edition ", StringComparison.Ordinal),
                Is.True,
                token.Name);
        }
    }

    /// <summary>
    /// Representation facts do not belong in the item-owned naming vocabulary. A template over a typed
    /// release is a separate surface; an item template must not name a token no item can supply.
    /// </summary>
    [Test]
    public void PublishesNoRepresentationTokensInTheItemTemplate()
    {
        var derived = MoviesDeclaration.Shape.Tokens.Select(static token => token.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (var hostGlobal in new[]
            {
                "{Quality Full}", "{Quality Title}", "{Release Group}",
                "{Original Title}", "{Original Filename}", "{MediaInfo Simple}",
            })
            {
                Assert.That(derived, Does.Not.Contain(hostGlobal), hostGlobal);
            }

            Assert.That(
                MoviesDeclaration.Carried.Naming.DefaultTemplates["file"],
                Does.Not.Contain("{Quality Full}"));
        });
    }

    private static IReadOnlyList<string> Strings(string property)
        => [.. Manifest.GetProperty(property).EnumerateArray().Select(static value => value.GetString()!)];

    private static JsonElement ReadManifest()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arronix.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "The repository root was not found from the test binary.");

        var path = Path.Combine(
            directory!.FullName,
            "src",
            "Arronix.Plugin.Movies",
            "plugin.json");

        Assert.That(File.Exists(path), Is.True, path);

        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }
}
