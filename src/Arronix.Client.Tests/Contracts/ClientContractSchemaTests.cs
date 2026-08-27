using System.Collections;
using System.Linq;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.Shape;
using Arronix.Client.Contracts;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// A schema is read once, at admission, and the whole graph — not just its root list.
/// </summary>
/// <remarks>
/// Every list in a schema belongs to the contract: the roots, each field's components, each field's
/// choices. What is hashed at admission, what a payload is later proved against, and what a page renders
/// have to be one read of those lists, or a contract can be admitted under one schema and rendered under
/// another. Each case below changes a different one of those lists after admission and asserts that
/// nothing downstream noticed.
/// </remarks>
[TestFixture]
public sealed class ClientContractSchemaTests
{
    private static readonly Type Entity = typeof(ClientContractSchemaTests);

    private static FieldDescriptor Text(string id) =>
        new() { FieldId = id, Name = id, ValueKind = FieldValueKind.Text };

    private static ClientContractSchema Admit(IReadOnlyList<FieldDescriptor> declared)
    {
        ClientContractSchema.Freeze(declared, out var schema).Should().BeNull();
        return schema!;
    }

    /// <remarks>
    /// The root list is the one the reviewer's `ToArray` would have covered, and it is covered: the roots
    /// are captured, and the list may answer with anything afterwards.
    /// </remarks>
    [Test]
    public void ARootListThatChangesAfterAdmissionChangesNothing()
    {
        var declared = Text("title");
        var swapped = Text("swapped");
        var stepping = new SteppingFields(declared, swapped);

        var schema = Admit(stepping);

        using var _ = new AssertionScope();

        stepping.Reads.Should().Be(1, "the root list is read once");
        schema.Admitted.Should().ContainSingle().Which.Should().BeSameAs(declared);
        schema.Frozen.Should().ContainSingle().Which.FieldId.Should().Be("title");

        // Read again, as often as a report, a renderer or a proof would.
        schema.Admitted[0].Should().BeSameAs(declared);
        schema.Frozen[0].FieldId.Should().Be("title");
        stepping.Reads.Should().Be(1);
    }

    /// <remarks>
    /// The hole the root list alone leaves. A field's components are a second contract-owned list, and a
    /// composite that grows a component after its hash was published would be rendered under a shape the
    /// published hash never covered.
    /// </remarks>
    [Test]
    public void NestedComponentsThatChangeAfterAdmissionChangeNothing()
    {
        var region = Text("region");
        var swapped = Text("swapped");
        var stepping = new SteppingFields(region, swapped);
        var composite = new FieldDescriptor
        {
            FieldId = "certification",
            Name = "Certification",
            ValueKind = FieldValueKind.Composite,
            Components = stepping,
        };

        var schema = Admit([composite]);
        var frozen = schema.Frozen.Single();

        using var _ = new AssertionScope();

        stepping.Reads.Should().Be(1, "a component list is read once");
        frozen.Components.Should().NotBeSameAs(stepping, "the copy is this client's own list");
        frozen.Components.Single().FieldId.Should().Be("region");
        frozen.Components.Single().FieldId.Should().Be("region");
        stepping.Reads.Should().Be(1);

        // And the hash is taken over the copy, so it describes what will be rendered rather than what the
        // list happened to answer while it was being hashed.
        ClientContractDigest.OfProjection(Entity, schema.Frozen)
            .Should().Be(ClientContractDigest.OfProjection(Entity, schema.Frozen));
    }

    /// <remarks>
    /// The same hole one level along. Choices decide which values an enumerated field may carry at all, so
    /// a choice list that grows after admission would let a payload through a field that was closed.
    /// </remarks>
    [Test]
    public void ChoicesThatChangeAfterAdmissionChangeNothing()
    {
        var stepping = new SteppingChoices(
            new FacetValue("released", "Released"),
            new FacetValue("smuggled", "Smuggled"));

        var enumerated = new FieldDescriptor
        {
            FieldId = "status",
            Name = "Status",
            ValueKind = FieldValueKind.Enumerated,
            Choices = stepping,
        };

        var schema = Admit([enumerated]);
        var frozen = schema.Frozen.Single();

        using var scope = new AssertionScope();

        stepping.Reads.Should().Be(1, "a choice list is read once");
        frozen.Choices.Should().NotBeSameAs(stepping);
        frozen.Choices.Single().Value.Should().Be("released");
        frozen.Choices.Single().Value.Should().Be("released");
        stepping.Reads.Should().Be(1);

        // The value a payload may carry is decided by the frozen choices, not by what the list says now.
        ProjectionAudit.Describe(
                Entity,
                schema,
                Projection(schema, FieldValue.OfEnumerated("smuggled")),
                out _)!
            .Message.Should().Contain("does not declare as a choice");

        ProjectionAudit.Describe(
                Entity,
                schema,
                Projection(schema, FieldValue.OfEnumerated("released")),
                out _)
            .Should().BeNull();
    }

    /// <remarks>
    /// The identity rule survives the freeze: what a contract projects with must be the object it was
    /// admitted with, and the frozen copy — equal in every value — is not that object either.
    /// </remarks>
    [Test]
    public void NeitherAnEqualCloneNorTheFrozenCopyCanStandInForTheAdmittedDescriptor()
    {
        var first = Text("title");
        var second = Text("overview");
        var schema = Admit([first, second]);
        var value = FieldValue.OfText("x");

        ContractPayloadOutcome Outcome(params FieldDescriptor[] descriptors)
            => ProjectionAudit.Describe(
                Entity,
                schema,
                new ProjectedEntity(Entity, [.. descriptors.Select(d => new ProjectedField(d, value))]),
                out var ignored)!.Outcome;

        using var scope = new AssertionScope();

        schema.Frozen[0].Should().Be(first, "the copy is equal in every value");
        schema.Frozen[0].Should().NotBeSameAs(first);

        Outcome(first with { }, second).Should().Be(ContractPayloadOutcome.SchemaDisagreement);
        Outcome(schema.Frozen[0], second).Should().Be(ContractPayloadOutcome.SchemaDisagreement);
        Outcome(second, first).Should().Be(ContractPayloadOutcome.SchemaDisagreement);
        Outcome(first, first).Should().Be(ContractPayloadOutcome.SchemaDisagreement);
    }

    /// <remarks>
    /// The whole point, stated once: nothing a consumer holds after the proof is a list the contract can
    /// still reach.
    /// </remarks>
    [Test]
    public void TheTrustedProjectionIsDetachedFromEveryContractOwnedSchemaList()
    {
        var components = new SteppingFields(Text("region"), Text("swapped"));
        var choices = new SteppingChoices(new FacetValue("a", "A"), new FacetValue("b", "B"));
        var roots = new SteppingFields(
            new FieldDescriptor
            {
                FieldId = "certification",
                Name = "Certification",
                ValueKind = FieldValueKind.Composite,
                Components = components,
            },
            Text("swapped"));

        var schema = Admit(roots);

        ProjectionAudit.Describe(
                Entity,
                schema,
                Projection(schema, FieldValue.OfComposite([FieldValue.OfText("US")])),
                out var trusted)
            .Should().BeNull();

        var descriptor = trusted!.Fields.Single().Descriptor;

        using var _ = new AssertionScope();

        descriptor.Should().BeSameAs(schema.Frozen[0]);
        descriptor.Should().NotBeSameAs(schema.Admitted[0]);
        descriptor.Components.Should().NotBeSameAs(components);
        descriptor.Choices.Should().NotBeSameAs(choices);
        trusted.Fields.Single().Value.Items.Should().NotBeNull();

        roots.Reads.Should().Be(1);
        components.Reads.Should().Be(1);
    }

    /// <remarks>
    /// One total, not one per walk. Moving the schema read to admission would otherwise hand every payload
    /// a fresh full budget, so a contract could declare a schema at the limit and then render values at the
    /// limit again. A projection continues from what its own schema already spent.
    /// </remarks>
    [Test]
    public void OneTotalCoversTheSchemaAndTheValuesRenderedBesideIt()
    {
        var page = new string('x', ClientContractLimits.MaxTextLength);
        var half = ClientContractLimits.MaxProjectionCharacters / 2 / ClientContractLimits.MaxTextLength;

        var notes = new FieldDescriptor
        {
            FieldId = "notes",
            Name = "N",
            ValueKind = FieldValueKind.Text,
            Multivalued = true,
        };

        // Descriptions each at the per-value limit: half the total, spent by the schema alone.
        var described = Enumerable.Range(0, half).Select(index => new FieldDescriptor
        {
            FieldId = "d" + index,
            Name = "D",
            Description = page,
            ValueKind = FieldValueKind.Text,
        });

        var values = FieldValue.OfItems(
            FieldValueKind.Text,
            [.. Enumerable.Range(0, half).Select(_ => FieldValue.OfText(page))]);

        var absent = Enumerable.Repeat(FieldValue.Absent(FieldValueKind.Text), half);

        ProjectionDefect? Render(IReadOnlyList<FieldDescriptor> fields, FieldValue[] carried)
        {
            var schema = Admit(fields);

            return ProjectionAudit.Describe(Entity, schema, Projection(schema, carried), out var ignored);
        }

        using var scope = new AssertionScope();

        Render([notes], [values]).Should().BeNull("the values alone are inside the total");

        Render([notes, .. described], [values, .. absent])!.Message
            .Should().Contain(
                "characters one projection may render in total",
                "the schema this contract declared is rendered beside them");

        // Each projection starts from the same remainder rather than from where the last one stopped.
        var repeated = Admit([notes]);
        ProjectionAudit.Describe(Entity, repeated, Projection(repeated, values), out var first).Should().BeNull();
        ProjectionAudit.Describe(Entity, repeated, Projection(repeated, values), out var second)
            .Should().BeNull("a contract is admitted once and read many times");
        first.Should().NotBeNull();
        second.Should().NotBeNull();
    }

    [Test]
    public void ASchemaThatIsNotAListOfFieldsIsRefusedAtAdmission()
    {
        using var _ = new AssertionScope();

        ClientContractSchema.Freeze(null, out var none)!.Outcome
            .Should().Be(ContractPayloadOutcome.SchemaDisagreement);
        none.Should().BeNull();

        ClientContractSchema.Freeze(new Throwing(), out var refused)!.Message
            .Should().Contain("could not be read");
        refused.Should().BeNull();
    }

    private static ProjectedEntity Projection(ClientContractSchema schema, params FieldValue[] values)
        => new(
            Entity,
            [.. Enumerable.Range(0, schema.Count)
                .Select(index => new ProjectedField(schema.Admitted[index], values[index]))]);

    /// <summary>A field list of one entry that answers differently every time it is read.</summary>
    private sealed class SteppingFields(FieldDescriptor first, FieldDescriptor rest)
        : IReadOnlyList<FieldDescriptor>
    {
        internal int Reads { get; private set; }

        public FieldDescriptor this[int index] => Reads++ == 0 ? first : rest;

        public int Count => 1;

        public IEnumerator<FieldDescriptor> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A choice list of one entry that answers differently every time it is read.</summary>
    private sealed class SteppingChoices(FacetValue first, FacetValue rest) : IReadOnlyList<FacetValue>
    {
        internal int Reads { get; private set; }

        public FacetValue this[int index] => Reads++ == 0 ? first : rest;

        public int Count => 1;

        public IEnumerator<FacetValue> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A schema that refuses to be read at all.</summary>
    private sealed class Throwing : IReadOnlyList<FieldDescriptor>
    {
        public FieldDescriptor this[int index] => throw new InvalidOperationException("no");

        public int Count => throw new InvalidOperationException("the schema refuses to be counted");

        public IEnumerator<FieldDescriptor> GetEnumerator() => throw new InvalidOperationException("no");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
