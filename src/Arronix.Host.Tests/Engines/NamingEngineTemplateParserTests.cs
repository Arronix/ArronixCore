using System.Linq;
using Arronix.Host.Engines.Naming;
using FluentAssertions;

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The template grammar: one language for what the four surveyed applications parse with four
/// different regexes, plus the constructs those regexes cannot express.
/// </summary>
[TestFixture]
internal sealed class NamingEngineTemplateParserTests
{
    [Test]
    public void CanonicalizationFoldsSpellingsTogether()
    {
        NamingTemplateParser.Canonicalize("Entry Title").Should().Be("entrytitle");
        NamingTemplateParser.Canonicalize("entry.title").Should().Be("entrytitle");
        NamingTemplateParser.Canonicalize("ENTRY_TITLE").Should().Be("entrytitle");
    }

    [Test]
    public void ATokenCarriesItsAffixes()
    {
        var compiled = NamingTemplateParser.Parse("{Entry Title}{ (Year)}");

        compiled.IsValid.Should().BeTrue();

        var tokens = compiled.Nodes.OfType<NamingTemplateNode.Token>().ToList();
        tokens.Should().HaveCount(2);
        tokens[1].Reference.Prefix.Should().Be(" (");
        tokens[1].Reference.Suffix.Should().Be(")");
        tokens[1].Reference.CanonicalName.Should().Be("year");
    }

    [Test]
    public void EscapedBracesAreLiteral()
    {
        var compiled = NamingTemplateParser.Parse("{{literal}} {Entry Title}");

        compiled.IsValid.Should().BeTrue();
        compiled.Nodes[0].Should().BeOfType<NamingTemplateNode.Literal>()
            .Which.Text.Should().Be("{literal} ");
    }

    [Test]
    public void ModifiersPaddingAndCapsParseFromTheSpec()
    {
        var compiled = NamingTemplateParser.Parse("{Index:00} {Entry Title:clean+the} {Group:-17}");

        compiled.IsValid.Should().BeTrue();

        var tokens = compiled.Nodes.OfType<NamingTemplateNode.Token>().ToList();
        tokens[0].Reference.PadWidth.Should().Be(2);
        tokens[1].Reference.Modifiers.Should().Equal(NamingModifier.Clean, NamingModifier.The);
        tokens[2].Reference.GraphemeCap.Should().Be(-17);
    }

    [Test]
    public void AnUnknownModifierIsAnError()
        => NamingTemplateParser.Parse("{Entry Title:sparkle}").Errors.Should().ContainSingle()
            .Which.Should().Contain("sparkle");

    [Test]
    public void AnOptionalGroupMustContainAToken()
    {
        NamingTemplateParser.Parse("<literal only>").IsValid.Should().BeFalse();
        NamingTemplateParser.Parse("<[{Tags}]>").IsValid.Should().BeTrue();
    }

    [Test]
    public void UnbalancedDelimitersAreErrors()
    {
        NamingTemplateParser.Parse("{Entry Title").IsValid.Should().BeFalse();
        NamingTemplateParser.Parse("Entry} Title").IsValid.Should().BeFalse();
        NamingTemplateParser.Parse("<{Entry Title}").IsValid.Should().BeFalse();
    }

    [Test]
    public void ASpanGroupParsesHeadTailAndOptions()
    {
        var compiled = NamingTemplateParser.Parse("{span:ordinal.index range}E{Index:00}{|}-{Index:00}{/span}");

        compiled.IsValid.Should().BeTrue();

        var span = compiled.Nodes.OfType<NamingTemplateNode.Span>().Single();
        span.ComponentRef.Should().Be("ordinal.index");
        span.RangeOnly.Should().BeTrue();
        span.Head.Should().NotBeEmpty();
        span.Tail.Should().NotBeEmpty();
    }

    [Test]
    public void ASpanWithoutATailIsLegal()
    {
        var compiled = NamingTemplateParser.Parse("{span}{Work Title}{/span}");

        compiled.IsValid.Should().BeTrue();
        compiled.Nodes.OfType<NamingTemplateNode.Span>().Single().Tail.Should().BeEmpty();
    }

    [Test]
    public void ANestedSpanIsAnError()
        => NamingTemplateParser.Parse("{span}{span}{Index}{/span}{/span}").IsValid.Should().BeFalse();

    [Test]
    public void ANestedBraceInsideATokenIsAnError()
        => NamingTemplateParser.Parse("{tag-{Entry Ext Id}}").IsValid.Should().BeFalse();

    [Test]
    public void ReferencedTokensAreCollectedAcrossGroups()
    {
        var compiled = NamingTemplateParser.Parse("{Entry Title} <({Year})> {span}{Work Title}{/span}");

        compiled.ReferencedTokens.Should().BeEquivalentTo(["entrytitle", "year", "worktitle"]);
    }

    [Test]
    public void PathSeparatorsSplitSegments()
        => NamingTemplateParser.Parse("{Entry Title}/{Work Title}").Nodes
            .Should().ContainSingle(node => node is NamingTemplateNode.Separator);
}
