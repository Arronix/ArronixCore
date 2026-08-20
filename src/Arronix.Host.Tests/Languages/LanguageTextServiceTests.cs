using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Plugins;
using Arronix.Host.Languages;
using Arronix.Languages.Reference;


namespace Arronix.Host.Tests.Languages;

/// <summary>The host composes language-owned rules without merging their equivalence spaces.</summary>
[TestFixture]
public sealed class LanguageTextServiceTests
{
    [Test]
    public void ComparisonKeysKeepEachLanguageSpaceDistinct()
    {
        var registry = new LanguageDefinitionRegistry();
        registry.Register(PluginId.FromString("languages.reference"), new GermanLanguageDefinition());
        var service = new LanguageTextService(registry);

        var keys = service.ComparisonKeys("Tür", new Language("de", "German"));

        Assert.That(keys, Is.EquivalentTo(new[] { "und:TUR", "de:TUER" }));
    }

    [Test]
    public void QueryPreparationIsSelectedByTheTextsStatedLanguage()
    {
        var registry = new LanguageDefinitionRegistry();
        registry.Register(PluginId.FromString("languages.reference"), new EnglishLanguageDefinition());
        var service = new LanguageTextService(registry);

        Assert.That(
            service.Query("The Lord & the Rings", Language.English),
            Is.EqualTo("Lord and the Rings"));
    }

    [Test]
    public void NamingUsesTheStatedLanguagesConjunctionAndArticles()
    {
        var registry = new LanguageDefinitionRegistry();
        registry.Register(PluginId.FromString("languages.reference"), new EnglishLanguageDefinition());
        registry.Register(PluginId.FromString("languages.reference"), new GermanLanguageDefinition());
        var service = new LanguageTextService(registry);

        Assert.Multiple(() =>
        {
            Assert.That(service.FileName("Law & Order", Language.English), Is.EqualTo("Law and Order"));
            Assert.That(service.FileName("Tür & Tor", new Language("de", "German")), Is.EqualTo("Tür und Tor"));
            Assert.That(service.Sort("Die Welle", new Language("de", "German")), Is.EqualTo("Welle, Die"));
            Assert.That(service.Sort("The Wave"), Is.EqualTo("The Wave"));
        });
    }

    [Test]
    public void TwoPluginsCannotOwnTheSameLanguageByLoadOrder()
    {
        var registry = new LanguageDefinitionRegistry();
        registry.Register(PluginId.FromString("first"), new EnglishLanguageDefinition());

        var failure = Assert.Throws<InvalidOperationException>(() =>
            registry.Register(PluginId.FromString("second"), new EnglishLanguageDefinition()));

        Assert.That(failure!.Message, Does.Contain("already owned"));
    }
}
