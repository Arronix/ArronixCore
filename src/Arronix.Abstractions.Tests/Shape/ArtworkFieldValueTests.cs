using System;
using System.Text.Json;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Tests.Shape;

/// <summary>
/// The artwork slot, and the derived value that reads it.
/// </summary>
[TestFixture]
public sealed class ArtworkFieldValueTests
{
    private static readonly ArtworkImage Poster =
        new("poster", new Uri("https://example.test/poster.jpg"), 1000, 1500);

    [Test]
    public void AnImageValueCarriesTheImageAndNothingElse()
    {
        var value = FieldValue.OfArtwork(Poster);

        Assert.Multiple(() =>
        {
            Assert.That(value.Image, Is.SameAs(Poster));
            Assert.That(value.Link, Is.Null, "exactly one payload slot is populated");
            Assert.That(value.Address, Is.EqualTo(Poster.Address));
        });
    }

    [Test]
    public void AnAddressValueStillCarriesOnlyTheAddress()
    {
        var value = FieldValue.OfArtwork(Poster.Address);

        Assert.Multiple(() =>
        {
            Assert.That(value.Link, Is.EqualTo(Poster.Address));
            Assert.That(value.Image, Is.Null);
            Assert.That(value.Address, Is.EqualTo(Poster.Address));
        });
    }

    /// <remarks>
    /// The address is read from whichever slot is populated, so writing it beside that slot would give a
    /// payload two ways to state one thing — the rule every other derived value on this contract follows.
    /// </remarks>
    [Test]
    public void TheDerivedAddressIsNotSerialized()
    {
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(FieldValue.OfArtwork(Poster)));
        var root = payload.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.TryGetProperty("Address", out _), Is.False, "the derived value is not written");
            Assert.That(root.TryGetProperty("Image", out var image), Is.True);
            Assert.That(image.GetProperty("Address").GetString(), Is.EqualTo(Poster.Address.ToString()),
                "the image's own address is a fact of the image, and is written");
            Assert.That(image.GetProperty("Role").GetString(), Is.EqualTo("poster"));
        });
    }
}
