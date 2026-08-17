// Exercises the experimental shape contracts.
#pragma warning disable ARX0013

using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Tests.Shape;

[TestFixture]
public class MediaFileFactsTests
{
    [Test]
    public void AFileRecordCarriesItsOwnFactsWithEmptyDefaults()
    {
        var facts = new MediaFileFacts
        {
            Id = MediaFileId.FromInt64(7),
            Path = "/library/example/item.bin",
            SizeBytes = 1024,
            Quality = new QualityTier("Standard", Rank: 1)
        };

        Assert.Multiple(() =>
        {
            Assert.That(facts.SceneName, Is.Null);
            Assert.That(facts.Languages, Is.Empty);
            Assert.That(facts.TechnicalFacets, Is.Empty);
            Assert.That(facts.KindFacets, Is.Empty);
        });
    }
}
