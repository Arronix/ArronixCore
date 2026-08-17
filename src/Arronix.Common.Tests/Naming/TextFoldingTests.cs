using System;
using System.Collections.Generic;
using System.Linq;
using Arronix.Abstractions.Naming;
using Arronix.Common.Naming;

// The naming contract is experimental; these tests exercise the platform's implementation of it.
#pragma warning disable ARX0009

namespace Arronix.Common.Tests.Naming;

/// <summary>
/// Covers folding: what Unicode decomposition can do on its own, what only a contributed table can do, and
/// what happens when the text is not well-formed.
/// </summary>
[TestFixture]
public class TextFoldingTests
{
    private static IReadOnlyDictionary<char, string> PlatformFolds =>
        TextFolding.BuildFoldTable([new DefaultDiacriticFoldingProvider()]);

    [TestCase("Café", "Cafe")]
    [TestCase("résumé", "resume")]
    [TestCase("Ångström", "Angstrom")]
    [TestCase("Zoë", "Zoe")]
    [TestCase("naïve", "naive")]
    public void Fold_RemovesCombiningMarksWithoutAnyTable(string input, string expected)
    {
        Assert.That(TextFolding.Fold(input), Is.EqualTo(expected));
    }

    [Test]
    public void Fold_LeavesTextThatHasNothingToFold()
    {
        Assert.That(TextFolding.Fold("Plain Title 4"), Is.EqualTo("Plain Title 4"));
    }

    [Test]
    public void Fold_LeavesAnEmptyStringAlone()
    {
        Assert.That(TextFolding.Fold(string.Empty), Is.Empty);
    }

    [Test]
    public void Fold_TreatsDecomposedTextTheSameAsItsComposedForm()
    {
        // "e" followed by COMBINING ACUTE ACCENT is the same text as the precomposed "\u00e9". Both must
        // fold to the same thing, or two spellings of one title stop comparing equal.
        const string Decomposed = "cafe\u0301";
        const string Composed = "caf\u00e9";

        Assert.That(TextFolding.Fold(Decomposed), Is.EqualTo("cafe").And.EqualTo(TextFolding.Fold(Composed)));
    }

    [TestCase("Þór", "Thor")]
    [TestCase("þór", "thor")]
    [TestCase("Ðe", "De")]
    [TestCase("guðmundur", "gudmundur")]
    public void Fold_FoldsLettersUnicodeCannotDecompose(string input, string expected)
    {
        Assert.That(TextFolding.Fold(input, PlatformFolds), Is.EqualTo(expected));
    }

    /// <summary>
    /// The table this replaces carried the lowercase thorn but not the uppercase one, so the same word
    /// folded or did not fold depending on whether it happened to start a title.
    /// </summary>
    [Test]
    public void Fold_FoldsBothCasesOfThorn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TextFolding.Fold("Þingvellir", PlatformFolds), Is.EqualTo("Thingvellir"));
            Assert.That(TextFolding.Fold("þingvellir", PlatformFolds), Is.EqualTo("thingvellir"));
        });
    }

    [Test]
    public void Fold_LeavesAnUndecomposableLetterAloneWhenNoTableCoversIt()
    {
        // The slashed o folds differently by language, so the platform deliberately claims no opinion.
        Assert.That(TextFolding.Fold("Bjørn", PlatformFolds), Is.EqualTo("Bjørn"));
    }

    [Test]
    public void Fold_AppliesTheTableAndDecompositionTogether()
    {
        Assert.That(TextFolding.Fold("Þórsdóttir", PlatformFolds), Is.EqualTo("Thorsdottir"));
    }

    /// <summary>
    /// A title arriving from a remote index is not guaranteed to be well-formed, and normalization is
    /// undefined for an unpaired surrogate. Folding must degrade rather than throw from inside a rename.
    /// </summary>
    [Test]
    public void Fold_DoesNotThrowOnAnUnpairedSurrogate()
    {
        var broken = "Café \ud800 Title";

        Assert.That(() => TextFolding.Fold(broken, PlatformFolds), Throws.Nothing);
    }

    [Test]
    public void Fold_StillAppliesTheTableToTextItCannotNormalize()
    {
        var result = TextFolding.Fold("\ud800Þ", PlatformFolds);

        Assert.That(result, Does.Contain("Th"));
    }

    [Test]
    public void Fold_RejectsMissingArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => TextFolding.Fold(null!), Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => TextFolding.Fold("x", null!), Throws.TypeOf<ArgumentNullException>());
        });
    }

    [Test]
    public void BuildFoldTable_TakesTheContributedFoldWhenItCollidesWithThePlatformOne()
    {
        var contributed = new StubProvider(null, new Dictionary<char, string> { ['þ'] = "z" });

        var table = TextFolding.BuildFoldTable([new DefaultDiacriticFoldingProvider(), contributed]);

        Assert.That(table['þ'], Is.EqualTo("z"));
    }

    [Test]
    public void BuildFoldTable_TakesTheContributedFoldRegardlessOfRegistrationOrder()
    {
        var contributed = new StubProvider(null, new Dictionary<char, string> { ['þ'] = "z" });

        var table = TextFolding.BuildFoldTable([contributed, new DefaultDiacriticFoldingProvider()]);

        Assert.That(table['þ'], Is.EqualTo("z"));
    }

    [Test]
    public void BuildFoldTable_KeepsThePlatformFoldsTheContributionDoesNotClaim()
    {
        var contributed = new StubProvider(null, new Dictionary<char, string> { ['ø'] = "o" });

        var table = TextFolding.BuildFoldTable([new DefaultDiacriticFoldingProvider(), contributed]);

        Assert.Multiple(() =>
        {
            Assert.That(table['ø'], Is.EqualTo("o"));
            Assert.That(table['Þ'], Is.EqualTo("Th"));
        });
    }

    [Test]
    public void BuildFoldTable_LeavesOutALanguageSpecificContributionWhenNoLanguageIsAsked()
    {
        var contributed = new StubProvider("de", new Dictionary<char, string> { ['ß'] = "ss" });

        var table = TextFolding.BuildFoldTable([contributed]);

        Assert.That(table, Does.Not.ContainKey('ß'));
    }

    [TestCase("de")]
    [TestCase("DE")]
    [TestCase("de-AT")]
    public void BuildFoldTable_TakesALanguageSpecificContributionForThatLanguage(string languageTag)
    {
        var contributed = new StubProvider("de", new Dictionary<char, string> { ['ß'] = "ss" });

        var table = TextFolding.BuildFoldTable([contributed], languageTag);

        Assert.That(table['ß'], Is.EqualTo("ss"));
    }

    [Test]
    public void BuildFoldTable_DoesNotMistakeALongerTagForTheSameLanguage()
    {
        var contributed = new StubProvider("de", new Dictionary<char, string> { ['ß'] = "ss" });

        var table = TextFolding.BuildFoldTable([contributed], "den");

        Assert.That(table, Does.Not.ContainKey('ß'));
    }

    [Test]
    public void BuildFoldTable_AlwaysTakesAContributionThatClaimsNoLanguage()
    {
        var contributed = new StubProvider(null, new Dictionary<char, string> { ['ø'] = "o" });

        var table = TextFolding.BuildFoldTable([contributed], "is-IS");

        Assert.That(table['ø'], Is.EqualTo("o"));
    }

    [Test]
    public void BuildFoldTable_ProducesAnEmptyTableFromNoProviders()
    {
        Assert.That(TextFolding.BuildFoldTable(Enumerable.Empty<IDiacriticFoldingProvider>()), Is.Empty);
    }

    [Test]
    public void BuildFoldTable_RejectsAMissingProviderSet()
    {
        Assert.That(() => TextFolding.BuildFoldTable(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void DefaultProvider_ClaimsNoLanguageAndCarriesBothCases()
    {
        var provider = new DefaultDiacriticFoldingProvider();

        Assert.Multiple(() =>
        {
            Assert.That(provider.Language, Is.Null);
            Assert.That(provider.Replacements.Keys, Is.EquivalentTo(new[] { 'ð', 'Ð', 'þ', 'Þ' }));
        });
    }

    private sealed class StubProvider(string? language, IReadOnlyDictionary<char, string> replacements)
        : IDiacriticFoldingProvider
    {
        public string? Language { get; } = language;

        public IReadOnlyDictionary<char, string> Replacements { get; } = replacements;
    }
}
