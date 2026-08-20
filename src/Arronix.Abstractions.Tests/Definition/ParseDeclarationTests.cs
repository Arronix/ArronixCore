// Exercises the declarative media-kind area.

using Arronix.Abstractions.Definition;

namespace Arronix.Abstractions.Tests.Definition;

[TestFixture]
public class ParseDeclarationTests
{
    [Test]
    public void GuardPatternDefaultsToTheNormalizedTextCaseInsensitively()
    {
        var guard = new GuardPattern("br-disk", "some-expression");

        Assert.Multiple(() =>
        {
            Assert.That(guard.Input, Is.EqualTo(GuardInput.Normalized));
            Assert.That(guard.CaseSensitive, Is.False);
        });
    }

    [Test]
    public void GuardPatternCanDemandTheRawTextAndExactCase()
    {
        // The surveyed sources rely on both dimensions: an upper-case token that is a revision marker
        // where the same word in lower case is just a word, and bracketed literals that only survive in
        // the raw text.
        var guard = new GuardPattern("upper-real", @"\b(?<real>REAL)\b", GuardInput.Raw, CaseSensitive: true);

        Assert.Multiple(() =>
        {
            Assert.That(guard.Input, Is.EqualTo(GuardInput.Raw));
            Assert.That(guard.CaseSensitive, Is.True);
        });
    }

    [Test]
    public void OccurrenceSelectionLivesOnTheTokenScan()
    {
        // Last-ness is a property of scanning the text — the rightmost token varies per release — so the
        // mode is declared on the table that scans, and the rung table has no selection mode at all.
        var table = new TokenTable
        {
            TableId = "embedded-ids",
            Rows = [new TokenRow(@"(?<id>tt\d{7,8})", "catalogId")]
        };

        Assert.That(table.Occurrence, Is.EqualTo(OccurrenceSelection.FirstOccurrence));

        var lastWins = table with { Occurrence = OccurrenceSelection.LastOccurrence };
        Assert.That(lastWins.Occurrence, Is.EqualTo(OccurrenceSelection.LastOccurrence));
    }

    [Test]
    public void TagPredicateIsAConjunctionAndTheEmptyOneAlwaysHolds()
    {
        Assert.That(TagPredicate.Always.All, Is.Empty);

        var atom = new PredicateAtom { Subject = "tags.SourceGroup", Op = PredicateOp.Equals, Values = ["disc"] };

        Assert.Multiple(() =>
        {
            Assert.That(atom.Negated, Is.False, "Negation is per atom; conjunctions cannot be negated.");
            Assert.That(new TagPredicate([atom]).All, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void NormalizationOptionsDefaultToTheSafeGuards()
    {
        var options = NormalizationOptions.Default;

        Assert.Multiple(() =>
        {
            Assert.That(options.FoldDiacritics, Is.True);
            Assert.That(options.NumericTitlesPassThrough, Is.True, "An all-numeric title is a title, not a year.");
            Assert.That(options.EmptyResultFallsBackToRaw, Is.True, "Consuming the whole text must not yield an empty title.");
        });
    }
}
