// Exercises the declarative media-kind area.
#pragma warning disable ARX0019

using System.Linq;
using Arronix.Abstractions.Definition;

namespace Arronix.Abstractions.Tests.Definition;

[TestFixture]
public class ParseDeclarationTests
{
    [Test]
    public void OrderedTablesPreserveDeclaredOrderExactly()
    {
        // Rule order is the algorithm: pre-release before broadcast, weak signals last. The declaration
        // must hand rows back byte-for-byte in the order they were written, so no engine can sort them.
        var declaration = new RungResolutionTable
        {
            Rules =
            [
                new RungRule { RuleId = "raw-first", When = TagPredicate.Always, TierId = "Top" },
                new RungRule { RuleId = "strong-second", When = TagPredicate.Always, TierId = "Middle" },
                new RungRule { RuleId = "weak-last", When = TagPredicate.Always, TierId = "Bottom" }
            ],
            UnknownTierId = "Unknown"
        };

        Assert.That(
            declaration.Rules.Select(rule => rule.RuleId),
            Is.EqualTo(new[] { "raw-first", "strong-second", "weak-last" }).AsCollection);
    }

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
    public void OccurrenceSelectionLivesOnTheTokenScanNotTheRuleTable()
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

        Assert.That(
            typeof(RungResolutionTable).GetProperty("Selection"),
            Is.Null,
            "A fixed rule-selection mode cannot express per-release occurrence order; the mode was moved to the scan.");
    }

    [Test]
    public void RungRuleCanCarryTheStatedResolutionOntoItsTier()
    {
        // A surveyed rung keeps its identity while adopting whatever resolution the release stated; a
        // pure tier identifier could not compose the two.
        var rule = new RungRule
        {
            RuleId = "carrying",
            When = TagPredicate.Always,
            TierId = "Middle",
            CarryStatedResolution = true
        };

        Assert.That(rule.CarryStatedResolution, Is.True);
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
