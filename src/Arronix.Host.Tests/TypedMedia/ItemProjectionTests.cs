using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Arronix.Host.Media.Catalog;
using Arronix.Host.Media.Typed;
using FluentAssertions;


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
    private static IMediaTypeRuntime Model { get; } =
        MediaTypeModelFactory.Build<Work, WorkTarget, WorkRelease, WorkParser, Works>();

    /// <summary>Host identity state, standing in for the one a running host owns.</summary>
    private static CatalogIdentity Identity { get; } = new();

    /// <summary>The axis a referenced group is addressed in, which is its own key space.</summary>
    private static MediaLevelId Collection => MediaLevelId.FromString("collection");

    /// <summary>
    /// The reference the host holds one entity under, assigned the way materializing it does.
    /// </summary>
    /// <remarks>
    /// Reached through the assigning contract, because that is the only way to reach it. Projection below
    /// is handed the reader, which has no such member — so these tests cannot accidentally arrange the
    /// state they then assert the read path did not create.
    /// </remarks>
    private static MediaItemRef Reference(IMediaEntity entity) =>
        ((ICatalogIdentityAssignment)Identity)
            .Identify(Model.Kind, Model.Shape.Levels[0].Id, entity.ExternalIds.Values);

    private static ItemView Project(IMediaEntity entity) => Model.Project(Reference(entity), entity, Identity);

    private static FieldValue Read(object item, string fieldId) => Model.Read(item, fieldId, Identity);

    private static Work Sample { get; } = new()
    {
        Title = "Arrival",
        OriginalTitle = "Arrival",
        Year = 2016,
        Runtime = TimeSpan.FromMinutes(116),
        ShippedBytes = 8_000_000_000,
        Stage = WorkStage.Published,
        PublishedOn = new DateOnly(2017, 2, 14),
        Genres = ["Science fiction", "Drama"],
        ExternalIds = ExternalIdSet.Of(ExternalId.Of("tmdb", "329865")),
        Artwork = ArtworkSet.Of(new ArtworkImage("poster", new Uri("https://example.invalid/p.jpg"))),
        AlternateTitles = [new AlternateTitle("Premier Contact", AlternateTitleRole.Translation)],
        Collections =
        [
            new WorkCollection
            {
                ExternalIds = ExternalIdSet.Of(ExternalId.Of("tmdb-collection", "7")),
                Title = "Villeneuve"
            }
        ]
    };

    [Test]
    public void ProjectingCarriesTheIdentityTheTitleAndEveryField()
    {
        var view = Project(Sample);

        Assert.Multiple(() =>
        {
            view.Ref.Kind.Should().Be(Works.Id);
            view.Ref.Level.Value.Should().Be("work");
            view.Ref.Should().Be(
                Reference(Sample),
                "the reference is the host's and is stated by the caller, not derived while projecting");
            view.Title.Should().Be("Arrival");
            view.ExternalIds.Should().Equal(ExternalId.Of("tmdb", "329865"));
            view.Fields.Keys.Should().BeEquivalentTo(
                Model.Shape.Levels[0].Fields.Select(field => field.FieldId));
        });
    }

    [Test]
    public void EveryProjectedValueCarriesTheShapeItsDescriptorPromised()
    {
        var view = Project(Sample);

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
        Read(Sample, fieldId).Text.Should().Be(expected);

    [Test]
    public void TypedValuesKeepTheirTypeRatherThanBecomingText()
    {
        Assert.Multiple(() =>
        {
            Read(Sample, "year").Number.Should().Be(2016);
            Read(Sample, "shippedBytes").Number.Should().Be(8_000_000_000);
            Read(Sample, "shippedBytes").Kind.Should().Be(FieldValueKind.ByteSize);
            Read(Sample, "runtime").Duration.Should().Be(TimeSpan.FromMinutes(116));
            Read(Sample, "publishedOn").Date.Should().Be(new DateOnly(2017, 2, 14));
        });
    }

    [Test]
    public void AMultivaluedFieldProjectsItsElementsRatherThanAJoinedString()
    {
        var genres = Read(Sample, "genres");

        Assert.Multiple(() =>
        {
            genres.Items.Should().HaveCount(2);
            genres.Items![0].Text.Should().Be("Science fiction");
        });
    }

    [Test]
    public void ARepeatedTupleProjectsAsCompositesWithComponentsInDeclaredOrder()
    {
        var alternates = Read(Sample, "alternateTitles");

        Assert.Multiple(() =>
        {
            alternates.Kind.Should().Be(FieldValueKind.Composite);
            alternates.Items.Should().ContainSingle();
            alternates.Items![0].Items.Should().HaveCount(3);
            var components = alternates.Items[0].Items!;
            components[0].Text.Should().Be("Premier Contact");
            components[1].Text.Should().Be("translation");
        });
    }

    [Test]
    public void GroupMembershipsCarryHandlesAndTheReferentsOwnTitles()
    {
        var identity = new CatalogIdentity();
        var group = ((ICatalogIdentityAssignment)identity).Identify(
            Model.Kind,
            Collection,
            [ExternalId.Of("tmdb-collection", "7")]);

        var memberships = Model.Read(Sample, "collections", identity);
        var reference = memberships.Items.Should().ContainSingle().Subject;

        Assert.Multiple(() =>
        {
            reference.Kind.Should().Be(FieldValueKind.Reference);
            reference.Text.Should().Be("Villeneuve");
            reference.Reference!.Value.Should().Be(
                group,
                "a referenced group is addressed in its own level, which is its own key space");

            // A group is addressed per axis rather than per level, so the axis identifier fills the level
            // slot: inventing a level per grouping axis is the fused shape the descriptor keeps apart.
            reference.Reference!.Value.Level.Value.Should().Be("collection");
            reference.External.Should().BeNull("a resolved reference carries the local handle, not both");
        });
    }

    /// <summary>
    /// A referenced group whose identifiers were assigned separately is addressed by the lowest of them,
    /// whatever order the referent lists them in.
    /// </summary>
    /// <remarks>
    /// Two identifiers, two assignments, no merge yet — which is the state that discriminates. Reading
    /// cannot merge, so it has to report the reference assignment would settle on, and that is the lowest.
    /// Taking the first identifier that resolves would address one group two ways depending on the order a
    /// cataloger listed them in; the witness is the same identity state read twice with only that reversed.
    /// </remarks>
    [TestCase(false, TestName = "AGroupHandleIsTheLowestAssignmentInDeclaredOrder")]
    [TestCase(true, TestName = "AGroupHandleIsTheLowestAssignmentInReversedOrder")]
    public void AGroupReferenceResolvesToTheLowestAssignmentWhateverOrderItsIdentifiersArrive(bool reversed)
    {
        var identity = new CatalogIdentity();
        var assign = (ICatalogIdentityAssignment)identity;
        var first = ExternalId.Of("tmdb-collection", "7");
        var second = ExternalId.Of("other-collection", "z9");

        var lower = assign.Identify(Model.Kind, Collection, [first]);
        var higher = assign.Identify(Model.Kind, Collection, [second]);

        var reference = Model
            .Read(Collecting(reversed ? [second, first] : [first, second]), "collections", identity)
            .Items!
            .Single();

        Assert.Multiple(() =>
        {
            higher.Id.Value.Should().BeGreaterThan(
                lower.Id.Value,
                "two separate assignments, unmerged, which is what makes the order observable");
            reference.Reference!.Value.Should().Be(
                lower,
                "the lowest wins, which is what assignment itself settles on when they converge");
            reference.External.Should().BeNull("a resolved reference carries the local handle, not both");
        });
    }

    /// <summary>
    /// A referent whose identifier still points at a superseded identity resolves to the survivor.
    /// </summary>
    /// <remarks>
    /// A merge repoints the aliases it can see, so this state is placed directly rather than grown: it is
    /// the durable case of a row written under an identity a later merge retired. The read path resolves it
    /// so no caller has to remember to canonicalize — which was G04's recorded limitation and is why
    /// projecting canonicalizes rather than trusting the lookup.
    /// </remarks>
    [Test]
    public void AGroupReferenceOnASupersededIdentityResolvesToTheSurvivingOne()
    {
        var identity = new CatalogIdentity();
        var assign = (ICatalogIdentityAssignment)identity;
        var surviving = assign.Identify(Model.Kind, Collection, [ExternalId.Of("tmdb-collection", "7")]);
        var retired = assign.Identify(Model.Kind, Collection, [ExternalId.Of("other-collection", "z9")]);
        assign.Identify(
            Model.Kind,
            Collection,
            [ExternalId.Of("tmdb-collection", "7"), ExternalId.Of("other-collection", "z9")]);

        // An alias the merge did not reach, still pointing at the identity it retired.
        var stale = ExternalId.Of("stale-collection", "s1");
        identity.Assign(Model.Kind, Collection, stale, retired.Id);

        var reference = Model.Read(Collecting([stale]), "collections", identity).Items!.Single();

        Assert.Multiple(() =>
        {
            identity.TryFind(Model.Kind, Collection, stale, out var raw).Should().BeTrue();
            raw.Id.Should().Be(retired.Id, "the lookup really does answer with the retired identity");
            reference.Reference!.Value.Should().Be(
                surviving,
                "and the read path resolves it, so no caller has to canonicalize");
        });
    }

    /// <summary>An item whose one collection states exactly these identifiers.</summary>
    private static Work Collecting(ExternalId[] collectionIds) => new()
    {
        Title = "Arrival",
        ExternalIds = ExternalIdSet.Of(ExternalId.Of("tmdb", "329865")),
        Collections = [new WorkCollection { Title = "Villeneuve", ExternalIds = ExternalIdSet.From(collectionIds) }],
    };

    /// <summary>
    /// A referent the host holds no identity for is projected under the catalog's identity, not given one.
    /// </summary>
    /// <remarks>
    /// This is what stops a browse page being a write. Rendering an item whose collection has never been
    /// taken in used to insert a group-level identity row per render; now it carries the catalog identifier
    /// a consumer can follow up with, and the local identity space is untouched.
    /// </remarks>
    [Test]
    public void AGroupReferenceTheHostHoldsNoIdentityForProjectsItsCatalogIdentityAndNoHandle()
    {
        var identity = new CatalogIdentity();

        var memberships = Model.Read(Sample, "collections", identity);
        var reference = memberships.Items.Should().ContainSingle().Subject;

        Assert.Multiple(() =>
        {
            reference.Kind.Should().Be(FieldValueKind.Reference);
            reference.Reference.Should().BeNull("the host has not named this group");
            reference.External.Should().Be(ExternalId.Of("tmdb-collection", "7"));
            reference.Text.Should().Be("Villeneuve", "a consumer that will not follow it still has a label");
            identity.TryFind(Model.Kind, Collection, ExternalId.Of("tmdb-collection", "7"), out _)
                .Should().BeFalse("and reading it named nothing");
        });
    }

    /// <summary>
    /// Projecting is a read. Rendering items and their group references allocates no durable identity.
    /// </summary>
    /// <remarks>
    /// Asserted through the ordinary lookup, and controlled by the assignment made afterwards: it receives
    /// the identity immediately after the item's, which it could not if three renders had spent any. The
    /// structural guard is stronger still — <c>Project</c> and <c>Read</c> are handed
    /// <see cref="ICatalogIdentityReader"/>, which has no member that could allocate.
    /// </remarks>
    [Test]
    public void ProjectingAPageAllocatesNothing()
    {
        var identity = new CatalogIdentity();
        var assign = (ICatalogIdentityAssignment)identity;
        var reference = assign.Identify(Model.Kind, Model.Shape.Levels[0].Id, Sample.ExternalIds.Values);

        for (var render = 0; render < 3; render++)
        {
            Model.Project(reference, Sample, identity);
            Model.Read(Sample, "collections", identity);
        }

        var named = assign.Identify(Model.Kind, Collection, [ExternalId.Of("tmdb-collection", "7")]);

        Assert.Multiple(() =>
        {
            identity.TryFind(Model.Kind, Collection, ExternalId.Of("tmdb-collection", "7"), out var found)
                .Should().BeTrue("the control: the lookup does find the group once it is named");
            found.Should().Be(named);
            named.Id.Value.Should().Be(
                reference.Id.Value + 1,
                "and it takes the very next identity, so three renders spent none");
        });
    }

    [Test]
    public void AnArtworkSetProjectsItsAddresses()
    {
        var images = Read(Sample, "artwork");

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
        var value = Read(Sample, "overview");

        Assert.Multiple(() =>
        {
            value.IsAbsent.Should().BeTrue();
            value.Kind.Should().Be(FieldValueKind.MultilineText);
        });
    }

    [Test]
    public void ReadingAnUnknownFieldIsRefusedRatherThanReturningNothing() =>
        FluentActions.Invoking(() => Read(Sample, "notAField"))
            .Should().Throw<ArgumentException>();

    [Test]
    public void ProjectingTheWrongTypeIsRefused() =>
        FluentActions.Invoking(() => Model.Project(Reference(Sample), "not a work", Identity))
            .Should().Throw<ArgumentException>();

    [Test]
    public void ProjectingUnderAReferenceFromAnotherKindIsRefused()
    {
        var reference = Reference(Sample) with { Kind = MediaKindId.FromString("other") };

        FluentActions.Invoking(() => Model.Project(reference, Sample, Identity))
            .Should().Throw<ArgumentException>()
            .WithParameterName("reference");
    }

    [Test]
    public void ProjectingUnderAReferenceFromAnotherLevelIsRefused()
    {
        var reference = Reference(Sample) with { Level = MediaLevelId.FromString("collection") };

        FluentActions.Invoking(() => Model.Project(reference, Sample, Identity))
            .Should().Throw<ArgumentException>()
            .WithParameterName("reference");
    }

    /// <summary>
    /// The root reference is supplied rather than derived, so an entity no catalog has named still projects
    /// under the reference the host states.
    /// </summary>
    [Test]
    public void AnEntityWithNoCatalogIdentifierProjectsUnderTheReferenceTheHostStates()
    {
        var unnamed = new Work { Title = "Unnamed", Stage = WorkStage.Published };
        var reference = new MediaItemRef(Model.Kind, Model.Shape.Levels[0].Id, MediaItemId.FromInt64(99));

        var view = Model.Project(reference, unnamed, Identity);

        Assert.Multiple(() =>
        {
            view.Ref.Should().Be(reference);
            view.ExternalIds.Should().BeEmpty();
            view.SortIndex.Should().Be(99);
        });
    }

    /// <summary>A referenced group with no catalog identifier cannot be addressed, and says so.</summary>
    [Test]
    public void AGroupReferenceWithNoCatalogIdentifierIsRefused()
    {
        var item = new Work
        {
            Title = "Arrival",
            ExternalIds = ExternalIdSet.Of(ExternalId.Of("tmdb", "329865")),
            Collections = [new WorkCollection { Title = "Unnamed collection" }],
        };

        FluentActions.Invoking(() => Model.Read(item, "collections", Identity))
            .Should().Throw<ArgumentException>().WithMessage("*states no catalog identifier*");
    }
}
