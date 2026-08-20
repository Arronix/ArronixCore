using System.IO;
using System.Linq;
using System.Text.Json;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Shape;

/// <summary>
/// The manifest and the derived model, checked against each other.
/// </summary>
/// <remarks>
/// <para>
/// <c>plugin.json</c> is read by the loader before any of this assembly's code runs, and the model is
/// derived after it. Nothing in the platform compares them, so every disagreement between the two is a
/// defect that surfaces as a missing feature at run time rather than as a build failure. This fixture is
/// that comparison.
/// </para>
/// <para>
/// The manifest is read from the working tree rather than from the build output, because it is not copied
/// alongside a referencing test assembly.
/// </para>
/// </remarks>
[TestFixture]
public class ManifestAgreementTests
{
    private static JsonElement Manifest { get; } = ReadManifest();

    [Test]
    public void DeclaresTheSameExtensionIdentifierAsTheCode()
        => Assert.That(Manifest.GetProperty("id").GetString(), Is.EqualTo(new MoviesPluginModule().Id.Value));

    [Test]
    public void DeclaresTheSameMediaKindAsTheType()
        => Assert.That(Strings("mediaKinds"), Is.EqualTo(new[] { new Movies().Kind.Value }));

    [Test]
    public void NamesTheAssemblyTheModuleActuallyLivesIn()
        => Assert.That(
            Manifest.GetProperty("entryAssembly").GetString(),
            Is.EqualTo(typeof(MoviesPluginModule).Assembly.GetName().Name + ".dll"));

    /// <summary>The media type declares identity roles but owns no catalog scheme.</summary>
    [Test]
    public void NamesNoCatalogScheme()
    {
        var advertised = Strings("identifiers").ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(advertised, Is.Empty);

            Assert.That(
                MoviesDeclaration.Level.Identity.ExternalIds,
                Is.Empty,
                "The level declares roles; which schemes fill them is a fact about the installed catalogers.");
            Assert.That(
                MoviesDeclaration.Shape.GroupingAxes.SelectMany(static axis => axis.ExternalIds),
                Is.Empty,
                "And the axis likewise, for its own key space.");

        });
    }

    /// <summary>
    /// The capability list is an admission check in both directions: a registration the manifest does not
    /// cover is refused, and a capability nothing uses quarantines the extension.
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

    /// <summary>
    /// The manifest's token list must mirror the derived vocabulary exactly. It is duplicated because the
    /// loader reads it before any of this assembly's code runs, and a duplicate that nothing compares is a
    /// duplicate that goes stale — which is precisely what the mirror is here to prevent now that the
    /// vocabulary is derived rather than hand-written.
    /// </summary>
    [Test]
    public void MirrorsTheDerivedTokenVocabulary()
    {
        var derived = MoviesDeclaration.Shape.Tokens;
        var manifest = Manifest.GetProperty("tokens").EnumerateArray().ToList();

        Assert.That(manifest, Has.Count.EqualTo(derived.Count));
        Assert.That(
            manifest.Select(static token => token.GetProperty("name").GetString()),
            Is.EqualTo(derived.Select(static token => token.Name)));
    }

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
                Manifest.GetProperty("tokens").EnumerateArray()
                    .Where(static token => token.TryGetProperty("isRequired", out var flag) && flag.GetBoolean()),
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
