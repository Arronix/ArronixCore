using System;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;


namespace Arronix.Abstractions.Tests.Media;

/// <summary>Common values retain semantics without taking ownership of a media-specific payload.</summary>
[TestFixture]
public sealed class CommonMediaValuesTests
{
    [Test]
    public void EveryMediaEntityHasTheSameCompiledFloor()
        => Assert.That(
            typeof(IMediaEntity).GetProperties().Select(static property => property.Name),
            Is.EquivalentTo(new[]
            {
                nameof(IMediaEntity.ExternalIds),
                nameof(IMediaEntity.Title),
                nameof(IMediaEntity.TitleLanguage),
                nameof(IMediaEntity.Overview),
                nameof(IMediaEntity.Artwork)
            }));

    [Test]
    public void ItemInfoIsTheCommonLocalizedTitleAndOverviewPayload()
    {
        var localized = new Localized<ItemInfo>(
            new Language("fr", "French"),
            new ItemInfo("Le titre", "Résumé"));

        Assert.Multiple(() =>
        {
            Assert.That(localized.Value.Title, Is.EqualTo("Le titre"));
            Assert.That(localized.Value.Overview, Is.EqualTo("Résumé"));
        });
    }

    [Test]
    public void TheClosedMediaItemIsAConcreteCompiledShape()
    {
        var item = new MediaItem<TestLifecycle, TestStatus>
        {
            Title = "Example",
            Lifecycle = new TestLifecycle(new DateOnly(2026, 8, 20), TestStatus.Available)
        };

        Assert.Multiple(() =>
        {
            Assert.That(item.GetType().IsAbstract, Is.False);
            Assert.That(item.Lifecycle.Published, Is.EqualTo(new DateOnly(2026, 8, 20)));
            Assert.That(item.Status, Is.EqualTo(TestStatus.Available));
        });
    }

    [Test]
    public void TheCommonCollectionIsUsableWithoutAMediaSpecificWrapper()
    {
        var collection = new MediaCollection<MediaItem<TestLifecycle, TestStatus>>
        {
            Title = "A Collection",
            MemberCount = 3
        };

        Assert.Multiple(() =>
        {
            Assert.That(collection.Title, Is.EqualTo("A Collection"));
            Assert.That(collection.MemberCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void TheCommonTargetDirectlyClosesOverTheItemType()
    {
        var item = new MediaItem<TestLifecycle, TestStatus>
        {
            Title = "Requested item",
            Lifecycle = new TestLifecycle(null, TestStatus.Unknown)
        };

        var target = new ReleaseTarget<MediaItem<TestLifecycle, TestStatus>>(item);

        Assert.That(target.Item, Is.SameAs(item));
    }

    [Test]
    public void TheCommonReleaseDirectlyClosesOverTheRepresentationType()
    {
        var representation = new TestRepresentation("example");
        var release = new Release<TestRepresentation>("Example", 2026, "Expanded", representation);

        Assert.Multiple(() =>
        {
            Assert.That(release.Title, Is.EqualTo("Example"));
            Assert.That(release.Year, Is.EqualTo(2026));
            Assert.That(release.Edition, Is.EqualTo("Expanded"));
            Assert.That(release.Representation, Is.SameAs(representation));
        });
    }

    [Test]
    public void ARatingRetainsItsOriginalScaleAndComparableUnitValue()
    {
        var rating = new Rating("example", 3.5m, RatingScale.OutOfFive, RatingVoice.Audience, 42);

        Assert.Multiple(() =>
        {
            Assert.That(rating.Value, Is.EqualTo(3.5m));
            Assert.That(rating.Scale, Is.EqualTo(RatingScale.OutOfFive));
            Assert.That(rating.NormalizedValue, Is.EqualTo(0.7m));
            Assert.That(rating.SampleSize, Is.EqualTo(42));
        });
    }

    [Test]
    public void AScaleCanExpressAnAuthoritysActualInterval()
    {
        var scale = new RatingScale(1m, 6m);

        Assert.Multiple(() =>
        {
            Assert.That(scale.Contains(1m), Is.True);
            Assert.That(scale.Contains(6m), Is.True);
            Assert.That(scale.Normalize(3.5m), Is.EqualTo(0.5m));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = scale.Normalize(7m));
        });
    }

    [Test]
    public void TheDefaultStructValueIsNotMistakenForARealScale()
        => Assert.Multiple(() =>
        {
            Assert.That(default(RatingScale).IsValid, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                _ = new Rating("example", 0m, default, RatingVoice.Unspecified));
        });

    [Test]
    public void ACertificationNamesBothRegionAndIssuingVocabulary()
    {
        var certification = new ContentCertification("AU", "ACB", "MA 15+", 15);

        Assert.Multiple(() =>
        {
            Assert.That(certification.Region, Is.EqualTo("AU"));
            Assert.That(certification.Authority, Is.EqualTo("ACB"));
            Assert.That(certification.Code, Is.EqualTo("MA 15+"));
            Assert.That(certification.MinimumAge, Is.EqualTo(15));
        });
    }

    [Test]
    public void LocalizationKeepsTheOwnerShapedPayloadTyped()
    {
        var localized = new Localized<TestText>(new Language("fr", "French"), new TestText("Le titre", "Résumé"));

        Assert.Multiple(() =>
        {
            Assert.That(localized.Language.Code, Is.EqualTo("fr"));
            Assert.That(localized.Value.Title, Is.EqualTo("Le titre"));
            Assert.That(localized.Value.Summary, Is.EqualTo("Résumé"));
        });
    }

    [Test]
    public void CatalogCandidateCarriesTheWholeTypedItemIncludingArtwork()
    {
        var item = new TestItem
        {
            Title = "Example",
            Artwork = ArtworkSet.Of(new ArtworkImage("cover", new Uri("https://example.invalid/cover.jpg"))),
        };
        var row = new CatalogCandidateRow<TestItem> { Item = item, Artwork = item.Artwork, Add = true };

        Assert.Multiple(() =>
        {
            Assert.That(row.Item, Is.SameAs(item));
            Assert.That(row.Artwork.Images, Has.Count.EqualTo(1));
            Assert.That(row.Add, Is.True);
        });
    }

    private sealed record TestText(string Title, string Summary);

    private sealed record TestRepresentation(string Value) : IRepresentation;

    private sealed record TestLifecycle(DateOnly? Published, TestStatus Stage)
        : IReleaseTimeline<TestStatus>;

    private enum TestStatus
    {
        Unknown,
        Available
    }

    private sealed class TestItem : IMediaItem
    {
        public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

        public required string Title { get; init; }

        public Language? TitleLanguage { get; init; }

        public string? Overview { get; init; }

        public required ArtworkSet Artwork { get; init; }

        public CatalogRecordState CatalogState { get; init; }
    }
}
