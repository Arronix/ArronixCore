// Exercises the declarative media-kind area.

using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Tests.Definition;

[TestFixture]
public class QueryDeclarationTests
{
    [Test]
    public void ASingletonKindDeclaresTheEmptyGrammar()
    {
        Assert.That(CoordinateGrammar.None.Spellings, Is.Empty);
    }

    [Test]
    public void ASweepTierMayLegitimatelyNameNothing()
    {
        var sweep = new QueryTierTemplate
        {
            TierId = "sweep",
            SearchKindId = "item",
            Origins = [SearchOrigin.Rss]
        };

        Assert.That(sweep.FreeTextTemplate, Is.Empty);
    }

    [Test]
    public void AnAliasRowCanRideAlongWithoutEverBecomingItsOwnQuery()
    {
        var translated = new AliasTemplate
        {
            AliasId = "translated-spellings",
            Template = "{translatedTitles:query}",
            Order = 4,
            FilterByAcceptedLanguages = true,
            NeverOwnQuery = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(translated.FilterByAcceptedLanguages, Is.True);
            Assert.That(translated.NeverOwnQuery, Is.True);
        });
    }

    [Test]
    public void ATierCanRequireAFieldBeforeItPlansAtAll()
    {
        var text = new QueryTierTemplate
        {
            TierId = "text",
            SearchKindId = "item",
            FreeTextTemplate = "{title:query} {year}",
            RequiredFields = ["year"],
            FanOutPerAlias = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(text.RequiredFields, Is.EqualTo(new[] { "year" }).AsCollection);
            Assert.That(text.FanOutPerAlias, Is.True);
        });
    }
}
