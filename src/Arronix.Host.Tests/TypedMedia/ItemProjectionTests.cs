using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed;
using FluentAssertions;

// Every contract these tests read is experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0020

namespace Arronix.Host.Tests.TypedMedia;

/// <summary>
/// The projection that keeps the derived descriptor honest for a consumer that cannot load the assembly.
/// </summary>
/// <remarks>
/// A .NET client can be handed the entity type and deserialize into it. A command line, a text interface or
/// anything not on this runtime cannot, and reads the descriptor plus these values instead. If the two ever
/// disagreed, the descriptor would be decoration — so what is asserted is that a value's shape is the shape
/// its own field descriptor promised.
/// </remarks>
[TestFixture]
internal sealed class ItemProjectionTests
{
    private static IMediaType Model => MediaTypeModelFactory.Build<Work, Works>();

    private static Work Sample { get; } = new()
    {
        Id = MediaItemId.FromInt64(42),
        Title = "Arrival",
        OriginalTitle = "Arrival",
        Year = 2016,
        Runtime = TimeSpan.FromMinutes(116),
        ShippedBytes = 8_000_000_000,
        Stage = WorkStage.Published,
        PublishedOn = new DateOnly(2017, 2, 14),
        Genres = ["Science fiction", "Drama"],
        ExternalIds = ExternalIdSet.Of(ExternalId.Of("tmdb", "329865")),
        Images = ArtworkSet.Of(new ArtworkImage("poster", new Uri("https://example.invalid/p.jpg"))),
        AlternateTitles = [new AlternateTitle("Premier Contact", AlternateTitleRole.Translation)],
        Collection = new WorkCollection { Id = MediaItemId.FromInt64(7), Title = "Villeneuve" }
    };

    [Test]
    public void ProjectingCarriesTheIdentityTheTitleAndEveryField()
    {
        var view = Model.Project(Sample);

        Assert.Multiple(() =>
        {
            view.Ref.Kind.Should().Be(Works.Kind);
            view.Ref.Level.Value.Should().Be("work");
            view.Ref.Id.Value.Should().Be(42);
            view.Title.Should().Be("Arrival");
            view.ExternalIds.Should().Equal(ExternalId.Of("tmdb", "329865"));
            view.Fields.Keys.Should().BeEquivalentTo(
                Model.Shape.Levels[0].Fields.Select(field => field.FieldId));
        });
    }

    [Test]
    public void EveryProjectedValueCarriesTheShapeItsDescriptorPromised()
    {
        var view = Model.Project(Sample);

        foreach (var descriptor in Model.Shape.Levels[0].Fields)
        {
            view.Fields[descriptor.FieldId].Kind.Should().Be(
                descriptor.ValueKind,
                "the projection of '{0}' must carry the shape its descriptor declares",
                descriptor.FieldId);
        }
    }

    [TestCase("title", "Arrival")]
    [TestCase("stage", "published")]
    public void ScalarValuesReadBackAsTheirDeclaredShape(string fieldId, string expected) =>
        Model.Read(Sample, fieldId).Text.Should().Be(expected);

    [Test]
    public void TypedValuesKeepTheirTypeRatherThanBecomingText()
    {
        Assert.Multiple(() =>
        {
            Model.Read(Sample, "year").Number.Should().Be(2016);
            Model.Read(Sample, "shippedBytes").Number.Should().Be(8_000_000_000);
            Model.Read(Sample, "shippedBytes").Kind.Should().Be(FieldValueKind.ByteSize);
            Model.Read(Sample, "runtime").Duration.Should().Be(TimeSpan.FromMinutes(116));
            Model.Read(Sample, "publishedOn").Date.Should().Be(new DateOnly(2017, 2, 14));
        });
    }

    [Test]
    public void AMultivaluedFieldProjectsItsElementsRatherThanAJoinedString()
    {
        var genres = Model.Read(Sample, "genres");

        Assert.Multiple(() =>
        {
            genres.Items.Should().HaveCount(2);
            genres.Items![0].Text.Should().Be("Science fiction");
        });
    }

    [Test]
    public void ARepeatedTupleProjectsAsCompositesWithComponentsInDeclaredOrder()
    {
        var alternates = Model.Read(Sample, "alternateTitles");

        Assert.Multiple(() =>
        {
            alternates.Kind.Should().Be(FieldValueKind.Composite);
            alternates.Items.Should().ContainSingle();
            alternates.Items![0].Items.Should().HaveCount(3);
            var components = alternates.Items[0].Items!;
            components[0].Text.Should().Be("Premier Contact");
            components[1].Text.Should().Be("Translation");
        });
    }

    [Test]
    public void AReferenceCarriesAHandleAndTheReferentsOwnTitle()
    {
        var reference = Model.Read(Sample, "collection");

        Assert.Multiple(() =>
        {
            reference.Kind.Should().Be(FieldValueKind.Reference);
            reference.Text.Should().Be("Villeneuve");
            reference.Reference!.Value.Id.Value.Should().Be(7);

            // A group is addressed per axis rather than per level, so the axis identifier fills the level
            // slot: inventing a level per grouping axis is the fused shape the descriptor keeps apart.
            reference.Reference!.Value.Level.Value.Should().Be("workCollection");
        });
    }

    [Test]
    public void AnArtworkSetProjectsItsAddresses()
    {
        var images = Model.Read(Sample, "images");

        Assert.Multiple(() =>
        {
            images.Kind.Should().Be(FieldValueKind.Artwork);
            images.Items.Should().ContainSingle()
                .Which.Link.Should().Be(new Uri("https://example.invalid/p.jpg"));
        });
    }

    [Test]
    public void AnAbsentValueIsAbsentRatherThanEmpty()
    {
        var value = Model.Read(Sample, "overview");

        Assert.Multiple(() =>
        {
            value.IsAbsent.Should().BeTrue();
            value.Kind.Should().Be(FieldValueKind.MultilineText);
        });
    }

    [Test]
    public void ReadingAnUnknownFieldIsRefusedRatherThanReturningNothing() =>
        FluentActions.Invoking(() => Model.Read(Sample, "notAField"))
            .Should().Throw<ArgumentException>();

    [Test]
    public void ProjectingTheWrongTypeIsRefused() =>
        FluentActions.Invoking(() => Model.Project("not a work"))
            .Should().Throw<ArgumentException>();
}
