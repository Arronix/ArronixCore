using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Client.Rendering;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Rendering;

/// <summary>
/// A composite's parts are described by its components, and a list's elements by the field itself.
/// </summary>
/// <remarks>
/// Both arrive as <see cref="FieldValue.Items"/> and they are not the same thing. Reusing the parent
/// descriptor for a composite gives every part the parent's kind, unit and choices, which renders as
/// something plausible: the fixtures below are chosen so that the wrong descriptor produces different text
/// rather than the same text by luck.
/// </remarks>
[TestFixture]
public sealed class FieldValueRenderingTests
{
    /// <summary>A rating: a source, a value out of a scale, who voted, and how many.</summary>
    /// <param name="multivalued">Whether the field holds a list of tuples rather than one.</param>
    private static FieldDescriptor Rating(bool multivalued = false) => new()
    {
        FieldId = "ratings",
        Name = "Ratings",
        ValueKind = FieldValueKind.Composite,
        Multivalued = multivalued,
        Components =
        [
            new FieldDescriptor { FieldId = "source", Name = "Source", ValueKind = FieldValueKind.Text },
            new FieldDescriptor { FieldId = "value", Name = "Value", ValueKind = FieldValueKind.Decimal },
            new FieldDescriptor
            {
                FieldId = "voice",
                Name = "Voice",
                ValueKind = FieldValueKind.Enumerated,
                Choices = [new FacetValue("audience", "Audience"), new FacetValue("critic", "Critic")],
            },
            new FieldDescriptor
            {
                FieldId = "sampleSize",
                Name = "Sample size",
                ValueKind = FieldValueKind.Count,
                Unit = "votes",
            },
        ],
    };

    private static FieldValue Tuple(string source, double value, string voice, long votes)
        => FieldValue.OfComposite(
        [
            FieldValue.OfText(source),
            FieldValue.OfDecimal(value),
            FieldValue.OfEnumerated(voice),
            FieldValue.OfCount(votes),
        ]);

    /// <remarks>
    /// Each part is rendered by its own component: the number formats as a decimal, the choice resolves to
    /// its display name, and the count takes the component's unit. Under the parent descriptor every part
    /// would be a composite, and a composite with no parts renders as a dash.
    /// </remarks>
    [Test]
    public void EachCompositePartIsRenderedByItsOwnComponent()
    {
        var text = FieldValueFormatter.Format(Rating(), Tuple("tmdb", 8.6d, "critic", 37412));

        using var _ = new AssertionScope();

        text.Should().Be("tmdb, 8.6, Critic, 37,412 votes");
        text.Should().NotContain("—", "no part is rendered as a composite with nothing in it");
    }

    /// <remarks>
    /// The decisive mutation, and the reason it is worth a case: reusing the parent descriptor still
    /// renders something. The choice is shown as its stored value rather than its display name, and the
    /// count loses its unit — a reader sees a plausible line with a raw identifier in it.
    /// </remarks>
    [Test]
    public void RenderingACompositePartUnderItsParentShowsAStoredChoiceAndDropsAUnit()
    {
        var field = Rating();
        var tuple = Tuple("tmdb", 8.6d, "critic", 37412);

        var underParent = string.Join(
            ", ",
            tuple.Items!.Select(part => FieldValueFormatter.Format(field, part)));

        using var _ = new AssertionScope();

        underParent.Should().Be("tmdb, 8.6, critic, 37,412", "this is what reusing the parent produces");
        FieldValueFormatter.Format(field, tuple).Should().Be("tmdb, 8.6, Critic, 37,412 votes");
    }

    [Test]
    public void AMultivaluedCompositeIsAListOfTuplesAndEachTupleKeepsItsComponents()
    {
        var text = FieldValueFormatter.Format(
            Rating(multivalued: true),
            FieldValue.OfItems(
                FieldValueKind.Composite,
                [Tuple("tmdb", 8.6d, "audience", 37412), Tuple("critics", 87d, "critic", 320)]));

        text.Should().Be("tmdb, 8.6, Audience, 37,412 votes, critics, 87, Critic, 320 votes");
    }

    /// <remarks>
    /// A homogeneous list uses the element shape: the field's own kind, choices and unit, with the
    /// multivalued flag no longer applying to one element.
    /// </remarks>
    [Test]
    public void AHomogeneousListRendersEachElementUnderTheFieldsOwnShape()
    {
        var genres = new FieldDescriptor
        {
            FieldId = "genres",
            Name = "Genres",
            ValueKind = FieldValueKind.Enumerated,
            Multivalued = true,
            Choices = [new FacetValue("action", "Action"), new FacetValue("scifi", "Science Fiction")],
        };

        var runtimes = new FieldDescriptor
        {
            FieldId = "runtimes",
            Name = "Runtimes",
            ValueKind = FieldValueKind.Integer,
            Multivalued = true,
            Unit = "min",
        };

        using var _ = new AssertionScope();

        FieldValueFormatter.Format(
                genres,
                FieldValue.OfItems(
                    FieldValueKind.Enumerated,
                    [FieldValue.OfEnumerated("action"), FieldValue.OfEnumerated("scifi")]))
            .Should().Be("Action, Science Fiction");

        // The unit belongs to each element, not to the list.
        FieldValueFormatter.Format(
                runtimes,
                FieldValue.OfItems(FieldValueKind.Integer, [FieldValue.OfInteger(148), FieldValue.OfInteger(92)]))
            .Should().Be("148 min, 92 min");
    }

    /// <remarks>
    /// A projection this client proved has one component per part. One it did not may not, and a hostile
    /// tuple with more parts than components must not silently borrow a neighbor's description.
    /// </remarks>
    [Test]
    public void APartWithNoComponentIsRenderedUnderItsOwnShapeRatherThanANeighborsName()
    {
        var field = Rating();
        var extra = FieldValue.OfComposite(
        [
            FieldValue.OfText("tmdb"),
            FieldValue.OfDecimal(8.6d),
            FieldValue.OfEnumerated("critic"),
            FieldValue.OfCount(37412),
            FieldValue.OfText("smuggled"),
        ]);

        var text = FieldValueFormatter.Format(field, extra);

        using var _ = new AssertionScope();

        text.Should().EndWith("smuggled");
        text.Should().Contain("37,412 votes", "the parts that do have components keep them");
    }

    [Test]
    public void AbsentAndPresentEmptyRenderTheSameAndAreDifferentValues()
    {
        var many = new FieldDescriptor
        {
            FieldId = "genres",
            Name = "Genres",
            ValueKind = FieldValueKind.Text,
            Multivalued = true,
        };

        using var _ = new AssertionScope();

        FieldValueFormatter.Format(many, FieldValue.Absent(FieldValueKind.Text)).Should().Be("—");
        FieldValueFormatter.Format(many, FieldValue.OfItems(FieldValueKind.Text, [])).Should().Be("—");
    }

    [Test]
    public void AnElementIsNoLongerAListEvenWhenTheFieldIs()
    {
        var many = new FieldDescriptor
        {
            FieldId = "genres",
            Name = "Genres",
            ValueKind = FieldValueKind.Text,
            Multivalued = true,
            Unit = "genre",
        };

        FieldValueFormatter.Format(many, FieldValue.OfText("Action"), element: true)
            .Should().Be("Action genre");
    }

    [Test]
    public void ADurationAndASizeAreFormattedForTheReaderRatherThanPrinted()
    {
        var runtime = new FieldDescriptor { FieldId = "r", Name = "R", ValueKind = FieldValueKind.Duration };
        var size = new FieldDescriptor { FieldId = "s", Name = "S", ValueKind = FieldValueKind.ByteSize };

        using var _ = new AssertionScope();

        FieldValueFormatter.Format(runtime, FieldValue.OfDuration(TimeSpan.FromMinutes(148)))
            .Should().Be(string.Create(CultureInfo.CurrentCulture, $"2h 28m"));
        FieldValueFormatter.Format(size, FieldValue.OfByteSize(1536)).Should().Be("1.5 KB");
    }
}
