using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Arronix.Host.Media.Typed;
using FluentAssertions;


namespace Arronix.Host.Tests.TypedMedia;

/// <summary>
/// A typed kind in, the structure descriptor out, pinned member by member.
/// </summary>
/// <remarks>
/// The claim under test is that the descriptor a typed kind produces is the same object every engine, the
/// binder and the client already consume — so what is asserted is the descriptor's contents, not that some
/// derivation ran. A test that only checked "a shape came out" would pass for a derivation that had quietly
/// stopped reading half the entity.
/// </remarks>
[TestFixture]
internal sealed class ShapeDerivationTests
{
    private static IMediaTypeRuntime Model =>
        MediaTypeModelFactory.Build<Work, WorkTarget, WorkRelease, WorkParser, Works>();

    private static MediaShape Shape => Model.Shape;

    private static MediaLevel Level => Shape.Levels[0];

    private static FieldDescriptor Field(string fieldId) =>
        Level.Fields.Single(field => string.Equals(field.FieldId, fieldId, StringComparison.Ordinal));

    [Test]
    public void TheKindNamesItselfFromTheDefinitionAndTheLevelFromTheType()
    {
        Assert.Multiple(() =>
        {
            Shape.Kind.Should().Be(Works.Id);
            Shape.Name.Should().Be("Work");
            Shape.PluralName.Should().Be("Works");
            Shape.Levels.Should().HaveCount(1);
            Level.Id.Value.Should().Be("work");
            Level.Name.Should().Be("Work");
            Level.Parent.Should().BeNull();
        });
    }

    [Test]
    public void TheBaseConstructorOwnsEveryRequiredDefinitionValue()
    {
        var definition = new Works();

        Assert.Multiple(() =>
        {
            definition.Kind.Should().Be(Works.Id);
            definition.SingularName.Should().Be("Work");
            definition.PluralName.Should().Be("Works");
            definition.Files.Should().Be(FileBindingDefinition.OnePerItem);
            definition.Formats.Should().ContainSingle()
                .Which.Should().BeOfType<FormatUse<WorkRepresentation>>();
            definition.Availability.Should()
                .BeOfType<OrderedSelectionDefinition<Work, WorkStage>>();
        });
    }

    [Test]
    public void TheBaseConstructorRefusesAnEmptyFormatSet()
    {
        FluentActions.Invoking(static () => new EmptyFormatWorks())
            .Should().Throw<ArgumentException>()
            .WithParameterName("formats");
    }

    [Test]
    public void EveryLevelRoleIsDerivedBecauseOneLevelCarriesAllOfThem()
    {
        // Zero authoring. With one level and one file per item, every role is this level's, and asking a
        // kind to restate that was the archetype of a declared row that says nothing.
        Level.Roles.Should().Be(
            MediaLevelRoles.LibraryEntry
            | MediaLevelRoles.AcquisitionUnit
            | MediaLevelRoles.CompletenessUnit
            | MediaLevelRoles.FileBearing);
    }

    [Test]
    public void TheFileBindingIsTheDegenerateCornerOfTheJoin()
    {
        Assert.Multiple(() =>
        {
            Shape.FileBinding.AnchorLevelId.Should().Be(Level.Id);
            Shape.FileBinding.UnitLevelId.Should().Be(Level.Id);
            Shape.FileBinding.AtMostOneFilePerUnit.Should().BeTrue();
            Shape.FileBinding.AtMostOneUnitPerFile.Should().BeTrue();
            Shape.FileBinding.OrdinalIsMeaningful.Should().BeFalse();
            Shape.FileBinding.SpanConstraints.Should().BeEmpty();
        });
    }

    [Test]
    public void ACoordinateSpaceIsDerivedWithoutTheKindDeclaringOne()
    {
        Assert.Multiple(() =>
        {
            Shape.CoordinateSpaces.Should().HaveCount(1);
            Shape.CoordinateSpaces[0].SpaceId.Should().Be(ShapeDerivation.SingletonSpaceId);
            Shape.CoordinateSpaces[0].Kind.Should().Be(CoordinateKind.Singleton);
            Shape.CoordinateSpaces[0].IsCanonical.Should().BeTrue();
            Shape.CoordinateSpaces[0].IsDense.Should().BeTrue();
            Level.CoordinateSpaceIds.Should().Equal(ShapeDerivation.SingletonSpaceId);
            Level.SequenceAxes.Should().BeEmpty();
            Level.Variant.Should().BeNull();
        });
    }

    [Test]
    public void TheMonitoringDimensionIsAHostDefaultRatherThanAPerKindRow()
    {
        Level.MonitorDimensions.Should().ContainSingle()
            .Which.DimensionId.Should().Be("wanted");
    }

    [Test]
    public void IdentityCarriesRolesAndNoSchemeUntilACatalogerIsInstalled()
    {
        Assert.Multiple(() =>
        {
            Level.Identity.RequiredRoles.Should().Equal(IdentifierRole.PrimaryWork);
            Level.Identity.AdmittedRoles.Should().Equal(IdentifierRole.SecondaryWork);
            Level.Identity.SupportsIdentifierRedirects.Should().BeFalse();

            // The scheme half is the host's, composed from whatever catalogers are installed. Empty here
            // is the honest answer, not an omission: no cataloger is installed in a unit test.
            Level.Identity.ExternalIds.Should().BeEmpty();
        });
    }

    [Test]
    public void EveryPublicPropertyBecomesAFieldExceptTheOneMarkedNotToBe()
    {
        Assert.Multiple(() =>
        {
            Level.Fields.Select(field => field.FieldId).Should().Contain(
                ["key", "externalIds", "title", "originalTitle", "year", "stage", "collections"]);

            Level.Fields.Should().NotContain(field =>
                string.Equals(field.FieldId, "isPublished", StringComparison.Ordinal));
        });
    }

    [Test]
    public void FieldIdentifiersAndNamesAreDerivedFromThePropertyName()
    {
        Assert.Multiple(() =>
        {
            Field("originalTitle").Name.Should().Be("Original title");
            Field("shippedBytes").Name.Should().Be("Shipped bytes");
            Field("externalIds").Name.Should().Be("External ids");
        });
    }

    [TestCase("title", FieldValueKind.Text)]
    [TestCase("overview", FieldValueKind.MultilineText)]
    [TestCase("year", FieldValueKind.Integer)]
    [TestCase("runtime", FieldValueKind.Duration)]
    [TestCase("publishedOn", FieldValueKind.Date)]
    [TestCase("stage", FieldValueKind.Enumerated)]
    [TestCase("shippedBytes", FieldValueKind.ByteSize)]
    [TestCase("artwork", FieldValueKind.Artwork)]
    [TestCase("externalIds", FieldValueKind.ExternalIdentifier)]
    [TestCase("originalLanguage", FieldValueKind.Language)]
    [TestCase("collections", FieldValueKind.Reference)]
    [TestCase("genres", FieldValueKind.Text)]
    [TestCase("scores", FieldValueKind.Composite)]
    public void TheValueShapeIsReadOffTheClrType(string fieldId, FieldValueKind expected) =>
        Field(fieldId).ValueKind.Should().Be(expected);

    [Test]
    public void ARepeatedTupleIsOneMultivaluedCompositeRatherThanParallelLists()
    {
        // The defect this closes: three lists correlated by position, with the correlation undeclarable, so
        // any consumer filtering one silently desynchronized the rest.
        var alternates = Field("alternateTitles");

        Assert.Multiple(() =>
        {
            alternates.ValueKind.Should().Be(FieldValueKind.Composite);
            alternates.Multivalued.Should().BeTrue();
            alternates.Components.Select(component => component.FieldId)
                .Should().Equal("title", "role", "language");
            alternates.Components
                .Single(component => string.Equals(component.FieldId, "role", StringComparison.Ordinal))
                .Choices.Select(choice => choice.Value)
                .Should().Equal("release", "translation");
        });
    }

    [Test]
    public void SemanticsAreTheUnionOfTheAttributesPlusWhatTheTypeImplies()
    {
        Assert.Multiple(() =>
        {
            // Sortable is implied by Title rather than written: every kind's default listing is ordered by
            // it, and a kind that had to remember to say so would eventually forget.
            Field("title").Semantics.Should().Be(
                FieldSemantics.Title | FieldSemantics.Searchable | FieldSemantics.Sortable);

            // Identity is derived twice over and written neither time: the key carries it, and so does any
            // external-identifier set, because both are how something names the same entity.
            Field("key").Semantics.Should().HaveFlag(FieldSemantics.Identity);
            Field("externalIds").Semantics.Should().HaveFlag(FieldSemantics.Identity);
            Field("shippedBytes").Semantics.Should().HaveFlag(FieldSemantics.Size);
            Field("stage").Semantics.Should().HaveFlag(FieldSemantics.Status);
        });
    }

    [Test]
    public void ADerivedPropertyIsNotEditableEvenWhereTheEntityAlsoSaysEditable()
    {
        Assert.Multiple(() =>
        {
            Field("title").Editable.Should().BeTrue();
            Field("stage").Editable.Should().BeFalse();
            Field("releaseDate").Editable.Should().BeFalse();
        });
    }

    [Test]
    public void ProminenceDefaultsToDetailAndIsOnlyWrittenWhereItDiffers()
    {
        Assert.Multiple(() =>
        {
            Field("title").Prominence.Should().Be(Prominence.Primary);
            Field("keywords").Prominence.Should().Be(Prominence.Diagnostic);
            Field("overview").Prominence.Should().Be(Prominence.Detail);
        });
    }

    [Test]
    public void AnEnumerationsMembersBecomeTheFieldsChoices() =>
        Field("stage").Choices.Select(choice => choice.Value)
            .Should().Equal("withdrawn", "rumored", "announced", "previewing", "published");

    [Test]
    public void TheGroupingAxisDerivesItsArityAndCarriesItsOwnFields()
    {
        var axis = Shape.GroupingAxes.Should().ContainSingle().Subject;

        Assert.Multiple(() =>
        {
            axis.AxisId.Should().Be("collection");
            axis.Name.Should().Be("Collection");
            axis.PluralName.Should().Be("Collections");
            axis.MemberLevelId.Should().Be(Level.Id);

            // Membership is plural in the item model: an item may belong to several groups and every group
            // may contain several items.
            axis.Arity.Should().Be(GroupingArity.ManyToMany);
            axis.Position.Should().Be(MemberPosition.None);
            axis.HasPrimaryMember.Should().BeFalse();

            axis.IsMonitorable.Should().BeTrue();
            axis.IsDiscoverySource.Should().BeTrue();
            axis.Lifetime.Should().Be(GroupLifetime.Independent);

            // The closed defect: a group could always say it had metadata of its own and never what it was.
            axis.HasOwnMetadata.Should().BeTrue();
            axis.Fields.Select(field => field.FieldId)
                .Should().Contain(["title", "overview", "artwork", "memberCount"]);
        });
    }

    [Test]
    public void AnOrderedSelectionIsAThresholdAndSaysSo()
    {
        var facet = Shape.SelectionFacets
            .Single(candidate => string.Equals(candidate.FacetId, "stage", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            facet.Name.Should().Be("Minimum availability");
            facet.AppliesToLevelId.Should().Be(Level.Id);
            facet.Kind.Should().Be(SelectionFacetKind.Enumerated);

            // Read off the source being an enumeration. The descriptor could always carry this and the
            // string surface had no way to produce it.
            facet.ValuesAreOrdered.Should().BeTrue();

            // Enumeration order, not call order.
            facet.Values.Select(value => value.Value)
                .Should().Equal("rumored", "announced", "previewing", "published");
            facet.DefaultAllowed.Should().Equal("published");

            // The third answer: the item exists, is visible, and only a grab is refused.
            facet.Application.Should().Be(FacetApplication.Acquisition);
        });
    }

    [Test]
    public void ASelectionWithNoBackingPropertyIsStillDeclarable()
    {
        var facet = Shape.SelectionFacets.Single(candidate =>
            string.Equals(candidate.FacetId, "availabilityDelay", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            facet.Kind.Should().Be(SelectionFacetKind.Threshold);
            facet.ThresholdDirection.Should().Be(ThresholdDirection.AtLeast);
            facet.DefaultNumber.Should().Be(0);
            facet.Unit.Should().Be("days");
        });
    }

    [Test]
    public void SearchKindsCarrySemanticTermsWithoutAProviderProtocolCategory()
    {
        var search = Shape.SearchKinds
            .Single(candidate => string.Equals(candidate.SearchKindId, "work", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            search.TargetLevelId.Should().Be(Level.Id);
            search.Scope.Kind.Should().Be(AcquisitionScopeKind.Single);
            search.RequiredTerms.Should().Equal(SearchTerm.WorkTitle);
            search.OptionalTerms.Should().Equal(SearchTerm.Year, SearchTerm.FreeText);
            search.Categories.Should().BeEmpty();
        });
    }

    [Test]
    public void TheFormatDescriptorCarriesDiscoveryDataButNoExecutablePolicy()
    {
        var family = Shape.FormatFamilies.Should().ContainSingle().Subject;

        Assert.Multiple(() =>
        {
            family.FamilyId.Should().Be("work");
            family.FileExtensions.Should().Equal(".mkv", ".mp4");
            family.Ladder.Should().BeEmpty();
            family.Unknown.Should().BeNull();
            Model.HasReleasePolicy.Should().BeTrue();

            // Defaults, unstated by the kind and therefore unwritten.
            family.CoexistsWithOtherFamilies.Should().BeFalse();
            family.SupportsEmbeddedMetadata.Should().BeFalse();

            Level.FormatFamilyIds.Should().Equal("work");
        });
    }

    [Test]
    public void TokensAreDerivedForNameableFieldsAndForTheHostsTitleTransforms()
    {
        var names = Shape.Tokens.Select(token => token.Name).ToArray();

        Assert.Multiple(() =>
        {
            names.Should().Contain(["{Work Title}", "{Work OriginalTitle}", "{Work Year}"]);
            names.Should().Contain(
                ["{Work TitleClean}", "{Work TitleThe}", "{Work TitleCleanThe}", "{Work TitleFirstCharacter}"]);

            // The identity stamp renders whichever catalog is installed, so no catalog's name appears in a
            // kind's folder template.
            names.Should().Contain("{Work Id}");
            names.Should().Contain(["{Collection Title}", "{Collection TitleThe}"]);

            // Artwork, references and composites are not interpolable, so no token is minted for them.
            names.Should().NotContain(["{Work Artwork}", "{Work Collection}", "{Work Scores}"]);
        });
    }

    [Test]
    public void TheModelKnowsItsItemAndGroupTypes()
    {
        Assert.Multiple(() =>
        {
            Model.Kind.Should().Be(Works.Id);
            Model.ItemType.Should().Be<Work>();
            Model.GroupTypes.Should().Equal(typeof(WorkCollection));
        });
    }
}
