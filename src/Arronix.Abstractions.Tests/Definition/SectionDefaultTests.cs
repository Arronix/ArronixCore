// Exercises the declarative media-kind area.
#pragma warning disable ARX0019

using Arronix.Abstractions.Definition;

namespace Arronix.Abstractions.Tests.Definition;

[TestFixture]
public class SectionDefaultTests
{
    [Test]
    public void QualityDefaultsToPureLadderDerivation()
    {
        var quality = QualityDeclaration.LadderDerived;

        Assert.Multiple(() =>
        {
            Assert.That(quality.Defaults, Is.Empty);
            Assert.That(quality.Fallback, Is.EqualTo(RungFallback.RoundUp), "Round up, never down.");
            Assert.That(quality.CrossFamily, Is.EqualTo(CrossFamilyRule.NeverCompare));
        });
    }

    [Test]
    public void NamingDefaultsToASingleSegmentSpineAndNoTemplatesOfItsOwn()
    {
        var naming = NamingDeclaration.Default;

        Assert.Multiple(() =>
        {
            Assert.That(naming.FolderSpine, Is.EqualTo("{root}/{folder}"));
            Assert.That(naming.DefaultTemplates, Is.Empty);
            Assert.That(naming.Selection, Is.Empty);
            Assert.That(naming.MultiUnitStyles, Is.Empty);
            Assert.That(naming.Fallbacks, Is.Empty);
        });
    }

    [Test]
    public void NotificationsDefaultToTheHostGenericSummary()
    {
        var notifications = NotificationDeclaration.Default;

        Assert.Multiple(() =>
        {
            Assert.That(notifications.HeadlineTemplate, Is.Null, "Null means the host-generic headline.");
            Assert.That(notifications.HeadlineMaxLength, Is.EqualTo(256));
            Assert.That(notifications.BodyMaxLength, Is.EqualTo(300));
            Assert.That(notifications.Fields, Is.Empty);
        });
    }

    [Test]
    public void PagingDefaultsToABoundedFetchWhereTruncationIsIncompleteness()
    {
        var paging = PagingPolicy.Default;

        Assert.Multiple(() =>
        {
            Assert.That(paging.MaxPages, Is.EqualTo(10));
            Assert.That(paging.TruncationIsFailure, Is.True);
        });
    }

    [Test]
    public void TierDefaultsUseTheSameClosedPredicateVocabularyAsEveryOtherRow()
    {
        var row = new TierDefault
        {
            When = new TagPredicate(
            [
                new PredicateAtom { Subject = "tags.SourceGroup", Op = PredicateOp.Present, Negated = true }
            ]),
            SourceGroup = "stream"
        };

        Assert.Multiple(() =>
        {
            Assert.That(row.Resolution, Is.Null, "A row asserts only what it states.");
            Assert.That(row.IgnoreStatedResolution, Is.False);
        });
    }
}
