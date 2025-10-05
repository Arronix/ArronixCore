using Arronix.Abstractions.DTOs;

namespace Arronix.Abstractions.Tests.DTOs;

[TestFixture]
public class LanguageTests
{
    [Test]
    public void Language_HasPredefinedLanguages()
    {
        Assert.That(Language.English.Code, Is.EqualTo("en"));
        Assert.That(Language.English.Name, Is.EqualTo("English"));
        Assert.That(Language.Unknown.Code, Is.EqualTo("und"));
        Assert.That(Language.Unknown.Name, Is.EqualTo("Unknown"));
    }

    [Test]
    public void Language_ToStringReturnsCode()
    {
        var lang = new Language("fr", "French");
        Assert.That(lang.ToString(), Is.EqualTo("fr"));
    }

    [Test]
    public void Language_CanBeCompared()
    {
        var lang1 = new Language("en", "English");
        var lang2 = new Language("en", "English");
        var lang3 = new Language("fr", "French");

        Assert.That(lang1, Is.EqualTo(lang2));
        Assert.That(lang1, Is.Not.EqualTo(lang3));
    }
}
