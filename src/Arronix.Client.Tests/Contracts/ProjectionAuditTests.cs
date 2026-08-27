using System.Collections;
using System.Linq;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Client.Contracts;
using FluentAssertions;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// What a projection has to be before anything renders it.
/// </summary>
/// <remarks>
/// Every case here is a projection a contract could return today: the tagged value has no type-level
/// invariant, the schema is a list of objects the contract chose, and the lists are the contract's own.
/// Each case changes exactly one thing away from a control that passes, so a refusal is evidence about the
/// rule it names rather than about a fixture that was wrong in several ways.
/// </remarks>
[TestFixture]
public sealed class ProjectionAuditTests
{
    private static readonly Type Entity = typeof(ProjectionAuditTests);

    private static FieldDescriptor Text(string id = "title") =>
        new() { FieldId = id, Name = id, ValueKind = FieldValueKind.Text };

    private static ProjectionDefect? Audit(IReadOnlyList<FieldDescriptor> schema, params FieldValue[] values)
        => Audit(schema, out _, values);

    /// <summary>
    /// Admits a schema the way the loader does, then projects against it the way a contract does.
    /// </summary>
    /// <remarks>
    /// The freeze is part of the path under test: a schema this client cannot describe is refused at
    /// admission rather than at the first payload, and its refusal is the one a caller sees.
    /// </remarks>
    private static ProjectionDefect? Audit(
        IReadOnlyList<FieldDescriptor> schema,
        out ProjectedEntity? trusted,
        params FieldValue[] values)
    {
        trusted = null;

        if (ClientContractSchema.Freeze(schema, out var admitted) is { } undescribable)
        {
            return undescribable;
        }

        return ProjectionAudit.Describe(
            Entity,
            admitted!,
            new ProjectedEntity(
                Entity,
                [.. Enumerable.Range(0, admitted!.Count)
                    .Select(index => new ProjectedField(admitted.Admitted[index], values[index]))]),
            out trusted);
    }

    /// <summary>Admits a schema and projects the fields a caller supplies, whatever they are.</summary>
    private static ProjectionDefect? AuditProjection(
        IReadOnlyList<FieldDescriptor> schema,
        Func<ClientContractSchema, ProjectedEntity> project,
        out ProjectedEntity? trusted)
    {
        trusted = null;

        return ClientContractSchema.Freeze(schema, out var admitted) is { } undescribable
            ? undescribable
            : ProjectionAudit.Describe(Entity, admitted!, project(admitted!), out trusted);
    }

    [Test]
    public void AProjectionOfTheDeclaredSchemaPasses()
    {
        var schema = new[] { Text() };

        Audit(schema, FieldValue.OfText("Inception")).Should().BeNull();
    }

    [Test]
    public void AProjectionNamingAnotherEntityTypeIsRefused()
    {
        var schema = new[] { Text() };
        AuditProjection(
                schema,
                admitted => new ProjectedEntity(
                    typeof(string),
                    [new ProjectedField(admitted.Admitted[0], FieldValue.OfText("x"))]),
                out _)!
            .Outcome.Should().Be(ContractPayloadOutcome.ProjectedTypeMismatch);
    }

    [Test]
    public void ANullProjectionIsAProjectionFailure()
        => AuditProjection([], _ => null!, out _)!.Outcome
            .Should().Be(ContractPayloadOutcome.ProjectionFailed);

    /// <summary>
    /// The four ways a field list can stop being the schema, each refused, and each for the same reason:
    /// a projected field carries the schema's own descriptor object, at its own position.
    /// </summary>
    [Test]
    public void ADroppedReorderedDuplicatedOrClonedDescriptorIsRefused()
    {
        var first = Text("title");
        var second = Text("overview");
        var schema = new[] { first, second };
        var one = FieldValue.OfText("a");
        var two = FieldValue.OfText("b");

        ProjectedEntity Project(params ProjectedField[] fields) => new(Entity, fields);

        // A clone that is equal in every value and is a different object.
        var clone = first with { };
        clone.Should().Be(first);
        clone.Should().NotBeSameAs(first);

        ContractPayloadOutcome Outcome(Func<ClientContractSchema, ProjectedEntity> project)
            => AuditProjection(schema, project, out _)!.Outcome;

        using var scope = new FluentAssertions.Execution.AssertionScope();

        Outcome(_ => Project(new ProjectedField(first, one)))
            .Should().Be(ContractPayloadOutcome.SchemaDisagreement, "a field was dropped");

        Outcome(_ => Project(new ProjectedField(second, two), new ProjectedField(first, one)))
            .Should().Be(ContractPayloadOutcome.SchemaDisagreement, "the fields were reordered");

        Outcome(_ => Project(new ProjectedField(first, one), new ProjectedField(first, one)))
            .Should().Be(ContractPayloadOutcome.SchemaDisagreement, "a field was duplicated");

        Outcome(_ => Project(new ProjectedField(clone, one), new ProjectedField(second, two)))
            .Should().Be(ContractPayloadOutcome.SchemaDisagreement, "an equal clone is not the descriptor");

        // The frozen copy is equal to the admitted descriptor too, and is just as much not it.
        Outcome(admitted => Project(
                new ProjectedField(admitted.Frozen[0], one),
                new ProjectedField(admitted.Admitted[1], two)))
            .Should().Be(
                ContractPayloadOutcome.SchemaDisagreement,
                "the frozen copy is what a page renders, not what a contract projects with");
    }

    [Test]
    public void AValueOfTheWrongShapeIsRefused()
        => Audit([Text()], FieldValue.OfInteger(3))!.Outcome
            .Should().Be(ContractPayloadOutcome.ValueInvariant);

    [Test]
    public void AValueCarryingTheWrongPayloadSlotIsRefused()
    {
        // Tagged Text, carrying a number. Nothing in the type refuses this.
        var confused = new FieldValue { Kind = FieldValueKind.Text, Number = 3 };

        Audit([Text()], confused)!.Message.Should().Contain("Text carrying Number");
    }

    [Test]
    public void AValueCarryingTwoPayloadSlotsIsRefused()
    {
        var both = new FieldValue { Kind = FieldValueKind.Text, Text = "x", Number = 3 };

        Audit([Text()], both)!.Outcome.Should().Be(ContractPayloadOutcome.ValueInvariant);
    }

    [Test]
    public void AnAbsentValueCarryingAPayloadIsRefused()
    {
        var contradiction = new FieldValue { Kind = FieldValueKind.Text, IsAbsent = true, Text = "x" };

        Audit([Text()], contradiction)!.Message.Should().Contain("is absent and carries");
    }

    /// <remarks>
    /// The distinction the tagged value exists to keep: a field the item has no value for and a field
    /// holding an empty list are different facts, and a reader is told which.
    /// </remarks>
    [Test]
    public void AbsentAndPresentEmptyAreBothAcceptedAndAreNotTheSameValue()
    {
        var many = Text() with { Multivalued = true };
        var absent = FieldValue.Absent(FieldValueKind.Text);
        var empty = FieldValue.OfItems(FieldValueKind.Text, []);

        using var _ = new FluentAssertions.Execution.AssertionScope();

        Audit([many], absent).Should().BeNull();
        Audit([many], empty).Should().BeNull();
        absent.IsAbsent.Should().BeTrue();
        absent.Items.Should().BeNull();
        empty.IsAbsent.Should().BeFalse();
        empty.Items.Should().BeEmpty();
    }

    [Test]
    public void APresentMultivaluedFieldWithNoListIsRefused()
    {
        var many = Text() with { Multivalued = true };
        var neither = new FieldValue { Kind = FieldValueKind.Text };

        Audit([many], neither)!.Message.Should().Contain("no list");
    }

    [Test]
    public void AnElementOfTheWrongShapeIsRefused()
    {
        var many = Text() with { Multivalued = true };
        var mixed = FieldValue.OfItems(
            FieldValueKind.Text,
            [FieldValue.OfText("a"), FieldValue.OfInteger(2)]);

        Audit([many], mixed)!.Message.Should().Contain("[1]");
    }

    [Test]
    public void ACompositeWhoseComponentCountDisagreesWithItsFieldIsRefused()
    {
        var composite = Composite();

        Audit([composite], FieldValue.OfComposite([FieldValue.OfText("US")]))!
            .Message.Should().Contain("1 component(s) where its field declares 2");
    }

    [Test]
    public void ACompositeComponentOfTheWrongShapeIsRefused()
    {
        var composite = Composite();
        var wrong = FieldValue.OfComposite([FieldValue.OfText("US"), FieldValue.OfText("13")]);

        Audit([composite], wrong)!.Message.Should().Contain("certification.minimumAge");
    }

    [Test]
    public void AWellFormedCompositeIsAccepted()
    {
        var composite = Composite();
        var right = FieldValue.OfComposite([FieldValue.OfText("US"), FieldValue.OfInteger(13)]);

        Audit([composite], right).Should().BeNull();
    }

    [Test]
    public void AMultivaluedCompositeIsAListOfTuplesRatherThanOneTuple()
    {
        var composite = Composite() with { Multivalued = true };
        var tuples = FieldValue.OfItems(
            FieldValueKind.Composite,
            [
                FieldValue.OfComposite([FieldValue.OfText("US"), FieldValue.OfInteger(13)]),
                FieldValue.OfComposite([FieldValue.OfText("DE"), FieldValue.OfInteger(16)]),
            ]);

        using var _ = new FluentAssertions.Execution.AssertionScope();

        Audit([composite], tuples).Should().BeNull();

        // One tuple offered where a list of them was declared: its components are read as elements.
        Audit([composite], FieldValue.OfComposite([FieldValue.OfText("US"), FieldValue.OfInteger(13)]))!
            .Outcome.Should().Be(ContractPayloadOutcome.ValueInvariant);
    }

    [Test]
    public void AnEnumeratedValueOutsideItsDeclaredChoicesIsRefused()
    {
        var field = Enumerated();

        using var _ = new FluentAssertions.Execution.AssertionScope();

        Audit([field], FieldValue.OfEnumerated("released")).Should().BeNull();
        Audit([field], FieldValue.OfEnumerated("<script>"))!
            .Message.Should().Contain("does not declare as a choice");
    }

    [Test]
    public void AnEnumeratedFieldWithNoChoicesCannotCarryAValue()
        => Audit([Text() with { ValueKind = FieldValueKind.Enumerated }], FieldValue.OfEnumerated("x"))!
            .Message.Should().Contain("declares no choices");

    [Test]
    public void ChoicesOnAFieldThatIsNotEnumeratedAreRefused()
        => Audit([Text() with { Choices = [new FacetValue("a", "A")] }], FieldValue.OfText("x"))!
            .Message.Should().Contain("is not enumerated");

    [Test]
    public void ComponentsOnAFieldThatIsNotACompositeAreRefused()
        => Audit([Text() with { Components = [Text("part")] }], FieldValue.OfText("x"))!
            .Message.Should().Contain("is not a composite");

    [Test]
    public void AValueThatContainsItselfIsRefused()
    {
        var many = Text() with { Multivalued = true };
        var knot = new Knot();
        var value = new FieldValue { Kind = FieldValueKind.Text, Items = knot };
        knot.Self = value;

        Audit([many], value)!.Message.Should().Contain("contains itself");
    }

    [Test]
    public void AGraphDeeperThanTheLimitIsRefused()
    {
        var leaf = Text("leaf");
        var field = leaf;

        for (var depth = 0; depth < ClientContractLimits.MaxDepth + 2; depth++)
        {
            field = new FieldDescriptor
            {
                FieldId = "level" + depth,
                Name = "level",
                ValueKind = FieldValueKind.Composite,
                Components = [field],
            };
        }

        var value = FieldValue.OfText("x");

        for (var depth = 0; depth < ClientContractLimits.MaxDepth + 2; depth++)
        {
            value = FieldValue.OfComposite([value]);
        }

        Audit([field], value)!.Message.Should().Contain("nests deeper");
    }

    /// <remarks>
    /// The list is charged before it is walked, so a list that claims more entries than the budget costs the
    /// refusal rather than the walk. Its indexer throws to prove nothing indexed it.
    /// </remarks>
    [Test]
    public void AListClaimingMoreEntriesThanTheBudgetIsRefusedWithoutBeingRead()
    {
        var many = Text() with { Multivalued = true };
        var vast = new FieldValue { Kind = FieldValueKind.Text, Items = new Vast() };

        Audit([many], vast)!.Message.Should().Contain("more than " + ClientContractLimits.MaxNodes);
    }

    /// <remarks>
    /// One budget across the whole walk, not a cap on any one list: two lists that each fit and together do
    /// not are refused.
    /// </remarks>
    [Test]
    public void TwoListsThatEachFitAndTogetherDoNotAreRefused()
    {
        var many = Text() with { Multivalued = true };
        var other = Text("other") with { Multivalued = true };
        // Each list costs its charge plus one per element, so a quarter of the budget is a list that fits
        // and two of them are one past it.
        var quarter = ClientContractLimits.MaxNodes / 4;

        FieldValue List(int count) => FieldValue.OfItems(
            FieldValueKind.Text,
            [.. Enumerable.Range(0, count).Select(_ => FieldValue.OfText("x"))]);

        using var _ = new FluentAssertions.Execution.AssertionScope();

        Audit([many], List(quarter)).Should().BeNull();
        Audit([many, other], List(quarter), List(quarter))!
            .Message.Should().Contain("more than " + ClientContractLimits.MaxNodes);
    }

    /// <remarks>
    /// Every string is bounded on its own and the node budget bounds how many there are, but the two
    /// multiply: a graph within both limits can still describe more text than a browser should hold from a
    /// payload that is itself capped. The total is charged across the whole projection.
    /// </remarks>
    [Test]
    public void MoreTextThanOneProjectionMayRenderIsRefusedEvenWhenEveryValueFits()
    {
        var many = Text() with { Multivalued = true };
        var page = new string('x', ClientContractLimits.MaxTextLength);

        FieldValue List(int count) => FieldValue.OfItems(
            FieldValueKind.Text,
            [.. Enumerable.Range(0, count).Select(_ => FieldValue.OfText(page))]);

        var fits = ClientContractLimits.MaxProjectionCharacters / ClientContractLimits.MaxTextLength;

        using var _ = new FluentAssertions.Execution.AssertionScope();

        // Every value is exactly at its own limit, and the count is far inside the node budget.
        fits.Should().BeLessThan(ClientContractLimits.MaxNodes);
        Audit([many], List(fits - 1)).Should().BeNull();
        Audit([many], List(fits + 1))!
            .Message.Should().Contain("characters one projection may render in total");
    }

    /// <remarks>
    /// An address is text this client writes into a document, in both shapes artwork takes. Charging the
    /// whole image and not the bare address would leave the total reachable through a list of the latter.
    /// </remarks>
    [Test]
    public void AnAddressIsChargedInBothShapesArtworkTakes()
    {
        var many = Text() with { ValueKind = FieldValueKind.Artwork, Multivalued = true };
        var address = new Uri("https://example.test/" + new string('a', 4000));
        var whole = new ArtworkImage("poster", address);
        var perValue = address.OriginalString.Length;
        var over = (ClientContractLimits.MaxProjectionCharacters / perValue) + 2;

        FieldValue List(int count, bool bare) => FieldValue.OfItems(
            FieldValueKind.Artwork,
            [.. Enumerable.Range(0, count).Select(_ => bare
                ? FieldValue.OfArtwork(address)
                : FieldValue.OfArtwork(whole))]);

        using var _ = new FluentAssertions.Execution.AssertionScope();

        over.Should().BeLessThan(ClientContractLimits.MaxNodes, "the node budget is not what refuses this");

        Audit([many], List(over, bare: false))!
            .Message.Should().Contain("characters one projection may render in total");
        Audit([many], List(over, bare: true))!
            .Message.Should().Contain("characters one projection may render in total");
    }

    /// <remarks>
    /// A contract's list may throw from anything, and a throw is the same refusal as a wrong answer: it is
    /// contained here rather than escaping into whatever was rendering.
    /// </remarks>
    [Test]
    public void AListThatThrowsIsAContainedRefusalRatherThanAnEscape()
    {
        var many = Text() with { Multivalued = true };
        var hostile = new FieldValue { Kind = FieldValueKind.Text, Items = new Throwing() };

        var defect = Audit([many], hostile);

        using var _ = new FluentAssertions.Execution.AssertionScope();

        defect!.Outcome.Should().Be(ContractPayloadOutcome.ValueInvariant);
        defect.Message.Should().Contain("could not be read");
    }

    /// <remarks>
    /// The reason reading once is not enough on its own. This list answers with a safe address while it is
    /// checked and an executable one afterwards; if anything read it a second time — the report, the
    /// renderer, a proof — the unsafe value would reach the document. What the audit returns is a copy it
    /// made while it was proving, so a second read of the contract's list never happens.
    /// </remarks>
    [Test]
    public void AListThatChangesAfterItIsCheckedCannotChangeWhatIsRendered()
    {
        var many = Text() with { ValueKind = FieldValueKind.Link, Multivalued = true };
        var stepping = new Stepping(
            FieldValue.OfLink(new Uri("https://example.test/safe")),
            FieldValue.OfLink(new Uri("javascript:alert(1)")));

        var defect = Audit(
            [many],
            out var trusted,
            new FieldValue { Kind = FieldValueKind.Link, Items = stepping });

        using var _ = new FluentAssertions.Execution.AssertionScope();

        defect.Should().BeNull("the list answered safely while it was checked");
        stepping.Reads.Should().Be(1, "each entry is read exactly once");

        var captured = trusted!.Fields.Single().Value.Items;
        captured.Should().NotBeSameAs(stepping);
        captured!.Single().Link!.Scheme.Should().Be("https");

        // Reading the captured list again answers the same, however many times it is read.
        captured.Single().Link!.Scheme.Should().Be("https");
        stepping.Reads.Should().Be(1);
    }

    /// <remarks>
    /// The same rule for what a descriptor names: a component list that changes after it was proved would
    /// give a rendered part a description nothing checked.
    /// </remarks>
    [Test]
    public void ADescriptorListThatChangesAfterItIsCheckedCannotChangeWhatIsRendered()
    {
        var first = new FieldDescriptor { FieldId = "region", Name = "Region", ValueKind = FieldValueKind.Text };
        var second = new FieldDescriptor { FieldId = "swapped", Name = "Swapped", ValueKind = FieldValueKind.Artwork };
        var stepping = new SteppingFields(first, second);
        var composite = new FieldDescriptor
        {
            FieldId = "certification",
            Name = "Certification",
            ValueKind = FieldValueKind.Composite,
            Components = stepping,
        };

        var defect = Audit([composite], out var trusted, FieldValue.OfComposite([FieldValue.OfText("US")]));

        using var _ = new FluentAssertions.Execution.AssertionScope();

        defect.Should().BeNull();
        trusted!.Fields.Single().Descriptor.Components.Should().NotBeSameAs(stepping);
        trusted.Fields.Single().Descriptor.Components.Single().FieldId.Should().Be("region");
        trusted.Fields.Single().Descriptor.Components.Single().FieldId.Should().Be("region");
    }

    [Test]
    public void AnUnsafeLinkIsRefusedAsAnAddress()
    {
        var link = Text() with { ValueKind = FieldValueKind.Link };
        var script = new Uri("javascript:alert(1)");

        var defect = Audit([link], FieldValue.OfLink(script));

        using var _ = new FluentAssertions.Execution.AssertionScope();

        defect!.Outcome.Should().Be(ContractPayloadOutcome.AddressUnsafe);
        Audit([link], FieldValue.OfLink(new Uri("https://example.test/x"))).Should().BeNull();
    }

    [Test]
    public void ArtworkKeepsItsRoleAndMeasurementsAndIsHeldToTheImageAddressRule()
    {
        var artwork = Text() with { ValueKind = FieldValueKind.Artwork };
        var good = new ArtworkImage("poster", new Uri("https://example.test/p.jpg"), 8, 12);

        using var _ = new FluentAssertions.Execution.AssertionScope();

        Audit([artwork], FieldValue.OfArtwork(good)).Should().BeNull();

        Audit([artwork], FieldValue.OfArtwork(good with { Role = string.Empty }))!
            .Message.Should().Contain("role is empty");

        Audit([artwork], FieldValue.OfArtwork(good with { Width = 0 }))!
            .Message.Should().Contain("width is not a positive");

        Audit([artwork], FieldValue.OfArtwork(good with { Address = new Uri("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=") }))!
            .Outcome.Should().Be(ContractPayloadOutcome.AddressUnsafe);
    }

    [Test]
    public void ANegativeSizeOrCountIsRefused()
    {
        var size = Text() with { ValueKind = FieldValueKind.ByteSize };
        var count = Text() with { ValueKind = FieldValueKind.Count };

        using var _ = new FluentAssertions.Execution.AssertionScope();

        Audit([size], FieldValue.OfByteSize(0)).Should().BeNull();
        Audit([size], FieldValue.OfByteSize(-1))!.Message.Should().Contain("never negative");
        Audit([count], FieldValue.OfCount(-1))!.Message.Should().Contain("never negative");
    }

    [Test]
    public void AProportionOutsideZeroToOneIsRefused()
    {
        var ratio = Text() with { ValueKind = FieldValueKind.Ratio };

        using var _ = new FluentAssertions.Execution.AssertionScope();

        Audit([ratio], FieldValue.OfRatio(0.5d)).Should().BeNull();
        Audit([ratio], FieldValue.OfRatio(1.5d))!.Message.Should().Contain("runs from zero to one");
        Audit([ratio], FieldValue.OfRatio(double.NaN))!.Message.Should().Contain("runs from zero to one");
    }

    [Test]
    public void ANonNumericDecimalIsRefused()
    {
        var real = Text() with { ValueKind = FieldValueKind.Decimal };

        Audit([real], FieldValue.OfDecimal(double.PositiveInfinity))!
            .Outcome.Should().Be(ContractPayloadOutcome.ValueInvariant);
    }

    [Test]
    public void AReferenceThatNamesNothingIsRefused()
    {
        var reference = Text() with { ValueKind = FieldValueKind.Reference };
        var real = new MediaItemRef(new MediaKindId("movies"), MediaLevelId.FromString("item"), new MediaItemId(7));

        using var _ = new FluentAssertions.Execution.AssertionScope();

        Audit([reference], FieldValue.OfReference(real)).Should().BeNull();
        Audit([reference], FieldValue.OfReference(real with { Id = new MediaItemId(0) }))!
            .Message.Should().Contain("assigned identifier is positive");
        Audit([reference], FieldValue.OfReference(default))!
            .Outcome.Should().Be(ContractPayloadOutcome.ValueInvariant);
    }

    /// <remarks>
    /// A semantic identifier made of spaces names nothing and renders as though a field were unlabeled, so
    /// it is refused with the empty one. Every place a contract states one is covered, because a rule that
    /// held for four of them would be a rule with a way round it.
    /// </remarks>
    [Test]
    public void AWhiteSpaceIdentifierNamesNothingAndIsRefused()
    {
        const string blank = "   ";
        var real = new MediaItemRef(new MediaKindId("movies"), MediaLevelId.FromString("item"), new MediaItemId(7));

        using var _ = new FluentAssertions.Execution.AssertionScope();

        Audit(
                [Text() with { ValueKind = FieldValueKind.Reference }],
                FieldValue.OfReference(real with { Kind = new MediaKindId(blank) }))!
            .Message.Should().Contain("media kind is white space");

        Audit(
                [Text() with { ValueKind = FieldValueKind.ExternalIdentifier }],
                FieldValue.OfExternalIdentifier(new ExternalId("tmdb", blank)))!
            .Message.Should().Contain("value is white space");

        Audit(
                [Text() with { ValueKind = FieldValueKind.ExternalIdentifier }],
                FieldValue.OfExternalIdentifier(new ExternalId(blank, "27205")))!
            .Message.Should().Contain("scheme is white space");

        Audit(
                [Text() with { ValueKind = FieldValueKind.Language }],
                FieldValue.OfLanguage(new Language(blank, "English")))!
            .Message.Should().Contain("code is white space");

        Audit(
                [Text() with { ValueKind = FieldValueKind.Quality }],
                FieldValue.OfQuality(new QualityTier(blank, 1)))!
            .Message.Should().Contain("name is white space");

        Audit(
                [Text() with { ValueKind = FieldValueKind.Artwork }],
                FieldValue.OfArtwork(new ArtworkImage(blank, new Uri("https://example.test/p.jpg"))))!
            .Message.Should().Contain("role is white space");

        Audit(
                [Text() with
                {
                    ValueKind = FieldValueKind.Enumerated,
                    Choices = [new FacetValue(blank, "Blank")],
                }],
                FieldValue.OfEnumerated(blank))!
            .Message.Should().Contain("stored value is white space");
    }

    /// <remarks>An elapsed length of time runs forward; a negative one reads as a measurement.</remarks>
    [Test]
    public void ANegativeElapsedTimeIsRefused()
    {
        var duration = Text() with { ValueKind = FieldValueKind.Duration };

        using var _ = new FluentAssertions.Execution.AssertionScope();

        Audit([duration], FieldValue.OfDuration(TimeSpan.FromMinutes(148))).Should().BeNull();
        Audit([duration], FieldValue.OfDuration(TimeSpan.Zero)).Should().BeNull();
        Audit([duration], FieldValue.OfDuration(TimeSpan.FromMinutes(-1)))!
            .Message.Should().Contain("runs backwards");
    }

    [Test]
    public void TextPastTheSizeLimitIsRefused()
        => Audit([Text()], FieldValue.OfText(new string('x', ClientContractLimits.MaxTextLength + 1)))!
            .Message.Should().Contain("past the");

    [Test]
    public void AFieldIdentifierPastTheSizeLimitIsRefused()
        => Audit(
                [Text(new string('f', ClientContractLimits.MaxIdentifierLength + 1))],
                FieldValue.OfText("x"))!
            .Message.Should().Contain("field identifier");

    private static FieldDescriptor Composite() => new()
    {
        FieldId = "certification",
        Name = "Certification",
        ValueKind = FieldValueKind.Composite,
        Components =
        [
            new FieldDescriptor { FieldId = "region", Name = "Region", ValueKind = FieldValueKind.Text },
            new FieldDescriptor { FieldId = "minimumAge", Name = "Minimum age", ValueKind = FieldValueKind.Integer },
        ],
    };

    private static FieldDescriptor Enumerated() => new()
    {
        FieldId = "status",
        Name = "Status",
        ValueKind = FieldValueKind.Enumerated,
        Choices = [new FacetValue("released", "Released"), new FacetValue("announced", "Announced")],
    };

    /// <summary>A list holding one value, which is the value that holds it.</summary>
    private sealed class Knot : IReadOnlyList<FieldValue>
    {
        internal FieldValue? Self { get; set; }

        public FieldValue this[int index] => Self!;

        public int Count => 1;

        public IEnumerator<FieldValue> GetEnumerator()
        {
            yield return Self!;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Reports more entries than could be held; indexing one is a failure the walk must avoid.</summary>
    private sealed class Vast : IReadOnlyList<FieldValue>
    {
        public FieldValue this[int index] =>
            throw new InvalidOperationException("the walk asked for an entry it was told not to expect");

        public int Count => int.MaxValue;

        public IEnumerator<FieldValue> GetEnumerator() =>
            throw new InvalidOperationException("the walk enumerated a list it was told not to expect");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A list of one entry that answers differently every time it is read.</summary>
    private sealed class Stepping(FieldValue first, FieldValue rest) : IReadOnlyList<FieldValue>
    {
        internal int Reads { get; private set; }

        public FieldValue this[int index] => Reads++ == 0 ? first : rest;

        public int Count => 1;

        public IEnumerator<FieldValue> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A component list of one entry that answers differently every time it is read.</summary>
    private sealed class SteppingFields(FieldDescriptor first, FieldDescriptor rest)
        : IReadOnlyList<FieldDescriptor>
    {
        private int _reads;

        public FieldDescriptor this[int index] => _reads++ == 0 ? first : rest;

        public int Count => 1;

        public IEnumerator<FieldDescriptor> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A list that refuses to be read at all.</summary>
    private sealed class Throwing : IReadOnlyList<FieldValue>
    {
        public FieldValue this[int index] => throw new InvalidOperationException("the list refuses to be read");

        public int Count => throw new InvalidOperationException("the list refuses to be counted");

        public IEnumerator<FieldValue> GetEnumerator() =>
            throw new InvalidOperationException("the list refuses to be enumerated");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
