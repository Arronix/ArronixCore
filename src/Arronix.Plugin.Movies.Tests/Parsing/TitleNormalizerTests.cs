using System.Globalization;
using System.Linq;
using Arronix.Plugin.Movies.Tests.Fixtures;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Parsing;

/// <summary>
/// The comparison key, ported from Radarr's <c>ParserTests/NormalizeTitleFixture</c>
/// (<c>CleanMovieTitle</c>).
/// </summary>
/// <remarks>
/// <para>
/// This function is the entire identity story for a media kind with no coordinates. It decides that
/// <c>Lord of the Rings</c> and <c>Lord of Rings</c> are one film and that <c>Tokyo Ghoul A</c> and
/// <c>Tokyo Ghoul</c> are two, and it is the index key the catalog is built on.
/// </para>
/// <para>
/// The stop-word rule is the subtle half and the reason these cases are worth their length: an article is
/// removed from the <i>middle</i> of a title and kept at either end. Removing it everywhere collapses
/// <c>The Thing</c> onto <c>Thing</c>; removing it nowhere splits one film into two keys.
/// </para>
/// </remarks>
[TestFixture]
public class TitleNormalizerTests
{
    /// <summary>
    /// The host expander a typed layer asks for by name of behaviour rather than by identifier. The kind
    /// says "roman numerals"; the derivation resolves the host's own identifier for it, which is one fewer
    /// string that has to agree with something across an assembly boundary.
    /// </summary>
    private const string RomanNumeralExpander = "roman-numeral-variants";

    private static readonly string[] MiddleLayouts = ["word.{0}.word", "word {0} word", "word-{0}-word"];

    private static readonly string[] TrailingLayouts = ["word.word.{0}", "word-word-{0}", "word-word {0}"];

    private static readonly string[] LeadingLayouts = ["{0}.word.word", "{0}-word-word", "{0} word word"];

    private static readonly string[] JoinedLayouts =
    [
        "word.{0}word", "word {0}word", "word-{0}word", "word{0}.word", "word{0}-word"
    ];

    [TestCase("Conan", "conan")]
    [TestCase("Castle (2009)", "castle2009")]
    [TestCase("Parenthood.2010", "parenthood2010")]
    [TestCase("Law_and_Order_SVU", "lawordersvu")]
    public void ProducesAComparisonKey(string title, string expected)
        => Assert.That(DeclaredTitleKey.Of(title), Is.EqualTo(expected));

    [TestCase("CaPitAl", "capital")]
    [TestCase("peri.od", "period")]
    [TestCase("this.^&%^**$%@#$!That", "thisthat")]
    [TestCase("test/test", "testtest")]
    [TestCase("90210", "90210")]
    [TestCase("24", "24")]
    [TestCase("I'm a cyborg, but that's OK", "imcyborgbutthatsok")]
    [TestCase("Im a cyborg, but thats ok", "imcyborgbutthatsok")]
    [TestCase("Test: Something à Deux", "testsomethingdeux")]
    [TestCase("Parler à", "parlera")]
    public void RemovesPunctuationAndCasing(string dirty, string expected)
        => Assert.That(DeclaredTitleKey.Of(dirty), Is.EqualTo(expected));

    [Test]
    public void RemovesAccents()
        => Assert.That(DeclaredTitleKey.Of("Carnivàle"), Is.EqualTo("carnivale"));

    [TestCase("the")]
    [TestCase("and")]
    [TestCase("or")]
    [TestCase("a")]
    [TestCase("an")]
    [TestCase("of")]
    public void RemovesAStopWordFromTheMiddle(string word)
    {
        foreach (var layout in MiddleLayouts)
        {
            Assert.That(
                DeclaredTitleKey.Of(string.Format(CultureInfo.InvariantCulture, layout, word)),
                Is.EqualTo("wordword"),
                layout);
        }
    }

    [TestCase("the")]
    [TestCase("and")]
    [TestCase("or")]
    [TestCase("an")]
    [TestCase("of")]
    public void KeepsAStopWordAtTheEnd(string word)
    {
        foreach (var layout in TrailingLayouts)
        {
            Assert.That(
                DeclaredTitleKey.Of(string.Format(CultureInfo.InvariantCulture, layout, word)),
                Is.EqualTo("wordword" + word),
                layout);
        }
    }

    [TestCase("the")]
    [TestCase("and")]
    [TestCase("or")]
    [TestCase("a")]
    [TestCase("an")]
    [TestCase("of")]
    public void KeepsAStopWordAtTheStart(string word)
    {
        foreach (var layout in LeadingLayouts)
        {
            Assert.That(
                DeclaredTitleKey.Of(string.Format(CultureInfo.InvariantCulture, layout, word)),
                Is.EqualTo(word + "wordword"),
                layout);
        }
    }

    [TestCase("the")]
    [TestCase("and")]
    [TestCase("or")]
    [TestCase("a")]
    [TestCase("an")]
    [TestCase("of")]
    public void KeepsAStopWordThatIsPartOfAWord(string word)
    {
        foreach (var layout in JoinedLayouts)
        {
            Assert.That(
                DeclaredTitleKey.Of(string.Format(CultureInfo.InvariantCulture, layout, word)),
                Is.EqualTo("word" + word + "word"),
                layout);
        }
    }

    [Test]
    public void KeepsAnArticleThatIsALetterInAnAcronym()
        => Assert.Multiple(() =>
        {
            Assert.That(DeclaredTitleKey.Of("word.a.N.K.L.E.word"), Is.EqualTo("wordankleword"));
            Assert.That(DeclaredTitleKey.Of("word.N.K.L.E.a.word"), Is.EqualTo("wordnkleaword"));
        });

    [Test]
    public void KeepsATrailingSingleLetterTitleWord()
        => Assert.That(DeclaredTitleKey.Of("Tokyo Ghoul A"), Is.EqualTo("tokyoghoula"));

    [TestCase("The Office", "theoffice")]
    [TestCase("The Tonight Show With Jay Leno", "thetonightshowwithjayleno")]
    [TestCase("The.Daily.Show", "thedailyshow")]
    public void KeepsALeadingArticle(string title, string expected)
        => Assert.That(DeclaredTitleKey.Of(title), Is.EqualTo(expected));

    /// <summary>
    /// The two titles the stop-word rule exists for. If it removed articles everywhere these would be one
    /// key; if it removed none they would be two.
    /// </summary>
    [Test]
    public void CollapsesTheSameFilmSpelledWithAndWithoutItsArticles()
        => Assert.That(
            DeclaredTitleKey.Of("The Lord of the Rings"),
            Is.EqualTo(DeclaredTitleKey.Of("The Lord of Rings")));

    /// <summary>
    /// A roman numeral is rewritten so that <c>Part II</c> and <c>Part 2</c> reach the same catalog row.
    /// The twenty-row table is host data now — it means the same thing to every kind — and what this kind
    /// declares is which lookup layers accept both spellings.
    /// </summary>
    /// <remarks>
    /// The display-title layer deliberately does not: it is tried first and unexpanded, so an exact title
    /// wins before any rewrite is considered. Expanding the original title as well would be a rewrite of a
    /// rewrite.
    /// </remarks>
    [Test]
    public void AcceptsBothNumeralSpellingsOnEveryFallbackLayer()
    {
        var layers = MoviesDeclaration.Carried.Matching.Entry.Layers;

        Assert.Multiple(() =>
        {
            Assert.That(layers[0].LayerId, Is.EqualTo("own-title"));
            Assert.That(layers[0].ExpanderIds, Is.Empty, "The exact title is tried unexpanded, first.");

            Assert.That(
                layers.Skip(1).All(static layer =>
                    layer.ExpanderIds.Contains(RomanNumeralExpander)),
                Is.True);
        });
    }

    /// <summary>
    /// The query spelling is a different function from the comparison key: a source is asked for words,
    /// not for a key with the spaces removed.
    /// </summary>
    [Test]
    public void KeepsWordBoundariesInTheQuerySpelling()
        => Assert.Multiple(() =>
        {
            Assert.That(
                DeclaredTitleKey.Options.QueryRewrites.Any(static rule => rule.Replacement == " "),
                Is.True,
                "The query spelling keeps words apart; the comparison key does not.");
            Assert.That(DeclaredTitleKey.Of("The Lord of the Rings"), Does.Not.Contain(' '));
        });
}
