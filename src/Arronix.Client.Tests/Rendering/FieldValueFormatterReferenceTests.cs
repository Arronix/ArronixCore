using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Client.Rendering;
using FluentAssertions;

namespace Arronix.Client.Tests.Rendering;

/// <summary>
/// Rendering a reference the host has not taken in.
/// </summary>
/// <remarks>
/// The host resolves references on the read path and never assigns one, so a referent it does not hold
/// arrives with the referent's own title and its catalog identifier and no local handle. Rendering that as
/// the absent dash would put a real collection behind the mark a missing field uses, and a browse page
/// would report a movie as belonging to nothing.
/// </remarks>
[TestFixture]
public class FieldValueFormatterReferenceTests
{
    private static FieldDescriptor Descriptor { get; } = new()
    {
        FieldId = "collections",
        Name = "Collections",
        ValueKind = FieldValueKind.Reference,
    };

    [Test]
    public void AReferenceTheHostHoldsRendersItsHandle()
    {
        var handle = new MediaItemRef(
            MediaKindId.FromString("movies"),
            MediaLevelId.FromString("collection"),
            MediaItemId.FromInt64(7));

        var value = new FieldValue
        {
            Kind = FieldValueKind.Reference,
            Reference = handle,
            Text = "Villeneuve",
        };

        FieldValueFormatter.Format(Descriptor, value).Should().Be(handle.ToString());
    }

    [Test]
    public void AnUnheldReferenceRendersTheReferentsTitleRatherThanTheAbsentMarker()
    {
        var value = new FieldValue
        {
            Kind = FieldValueKind.Reference,
            Reference = null,
            External = ExternalId.Of("tmdb-collection", "7"),
            Text = "Villeneuve",
        };

        FieldValueFormatter.Format(Descriptor, value).Should().Be("Villeneuve");
    }

    /// <summary>A referent with no title of its own is still addressable, so its identifier is shown.</summary>
    [Test]
    public void AnUnheldReferenceWithNoTitleRendersItsCatalogIdentifier()
    {
        var value = new FieldValue
        {
            Kind = FieldValueKind.Reference,
            Reference = null,
            External = ExternalId.Of("tmdb-collection", "7"),
        };

        FieldValueFormatter.Format(Descriptor, value).Should().Be(
            ExternalId.Of("tmdb-collection", "7").ToString());
    }

    /// <summary>A reference carrying nothing at all is genuinely absent, which is the control.</summary>
    [Test]
    public void AReferenceCarryingNeitherHandleTitleNorIdentifierIsAbsent()
    {
        var value = new FieldValue { Kind = FieldValueKind.Reference };

        FieldValueFormatter.Format(Descriptor, value).Should().Be("—");
    }

    /// <summary>An explicitly absent field is absent whatever it happens to carry.</summary>
    [Test]
    public void AnAbsentReferenceFieldStaysAbsent()
    {
        var value = new FieldValue { Kind = FieldValueKind.Reference, IsAbsent = true, Text = "Villeneuve" };

        FieldValueFormatter.Format(Descriptor, value).Should().Be("—");
    }
}
