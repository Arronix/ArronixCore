using System.Text;
using Arronix.Host.Engines.Naming;
using FluentAssertions;

// The shape and definition contracts are experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// Rendering and materialization: affix elision, optional groups, substitutions, modifiers, spans,
/// and the byte-budget truncation ladder the surveyed engine hard-codes for one token.
/// </summary>
[TestFixture]
internal sealed class NamingEngineRenderingTests
{
    private static string Render(string template, NamingTokenBindings bindings, RenderOptions? options = null)
        => new TemplateRenderer(options).RenderComponent(NamingTemplateParser.Parse(template), bindings);

    private static NamingTokenBindings Bindings() => NamingEngineTestSupport.Bind(
        NamingEngineTestSupport.File(),
        NamingEngineTestSupport.EntryItem(),
        NamingEngineTestSupport.WorkItem());

    [Test]
    public void ATemplateRendersDerivedTokensAndFileGlobals()
        => Render("{Entry Title} ({Entry Year}) [{Quality Title}]-{Release Group}", Bindings())
            .Should().Be("The Fixture Show (2020) [HD-1080p]-GROUP");

    [Test]
    public void AnEmptyTokenTakesItsAffixesWithIt()
        => Render("{Entry Title}{ [(Quality Real)]}", Bindings())
            .Should().Be("The Fixture Show");

    [Test]
    public void AnOptionalGroupDropsWhenNoTokenInsideResolved()
    {
        Render("{Entry Title} <edition [{Quality Real}]>", Bindings()).Should().Be("The Fixture Show");
        Render("{Entry Title} <in [{Quality Title}]>", Bindings()).Should().Be("The Fixture Show in [HD-1080p]");
    }

    [Test]
    public void TheSmartColonSubstitutionIsTheDefault()
    {
        var bindings = NamingEngineTestSupport.Bind(
            null,
            NamingEngineTestSupport.EntryItem(title: "Fixture: The Reckoning"));

        Render("{Entry Title}", bindings).Should().Be("Fixture - The Reckoning");
    }

    [Test]
    public void ModifiersApplyLeftToRight()
    {
        var bindings = Bindings();

        Render("{Entry Title:the}", bindings).Should().Be("Fixture Show, The");
        Render("{Entry Title:lower+dot}", bindings).Should().Be("the.fixture.show");
        Render("{Entry Title:first}", bindings).Should().Be("T");
        Render("{Entry Title:noyear+year}", bindings).Should().Be("The Fixture Show (2020)");
    }

    [Test]
    public void PaddingAndGraphemeCapsApply()
    {
        var bindings = Bindings();

        Render("R{Run:00}I{Index:00}", bindings).Should().Be("R01I03");
        Render("{Entry Title:7}", bindings).Should().Be("The Fix");
        Render("{Entry Title:-4}", bindings).Should().Be("Show");
    }

    [Test]
    public void ASequenceExceptionNamesTheAxisValue()
    {
        var bindings = NamingEngineTestSupport.Bind(
            null,
            NamingEngineTestSupport.EntryItem(),
            NamingEngineTestSupport.WorkItem(run: 0, index: 2));

        Render("{Run Name}", bindings).Should().Be("Extras");
        Render("{Run}", bindings).Should().Be("0");
    }

    [Test]
    public void ASpanRendersHeadThenTailPerUnit()
    {
        var bindings = Bindings();
        bindings.Set(new TokenBinding
        {
            CanonicalName = "index",
            DisplayName = "Index",
            Values = ["3", "4", "5"],
        });

        Render("{span}R01I{Index:00}{|}-{Index:00}{/span}", bindings).Should().Be("R01I03-04-05");
        Render("{span range}R01I{Index:00}{|}-{Index:00}{/span}", bindings).Should().Be("R01I03-05");
    }

    [Test]
    public void DroppableTokensVanishBeforeElasticOnesShrink()
    {
        var bindings = Bindings();
        var options = new RenderOptions { MaxComponentBytes = 30 };

        var rendered = Render("{Entry Title}{ [Quality Full]}", bindings, options);

        rendered.Should().Be("The Fixture Show");
        Encoding.UTF8.GetByteCount(rendered).Should().BeLessThanOrEqualTo(30);
    }

    [Test]
    public void TheDeepestElasticTokenAbsorbsTheOvershoot()
    {
        var bindings = Bindings();
        var options = new RenderOptions { MaxComponentBytes = 30 };

        // 38 bytes over a 30-byte budget: the deeper work title shrinks; the entry title survives.
        var rendered = Render("{Entry Title} - {Work Title}", bindings, options);

        rendered.Should().Be("The Fixture Show - A Long A…");
        Encoding.UTF8.GetByteCount(rendered).Should().BeLessThanOrEqualTo(30);
    }

    [Test]
    public void AnExplicitCapPinsATokenOutOfTheElasticPool()
    {
        var bindings = Bindings();
        var options = new RenderOptions { MaxComponentBytes = 20 };

        // The capped work title is pinned, so the entry title absorbs the cut instead.
        var rendered = Render("{Work Title:6} - {Entry Title}", bindings, options);

        rendered.Should().Be("A Long - The Fixt…");
        Encoding.UTF8.GetByteCount(rendered).Should().BeLessThanOrEqualTo(20);
    }

    [Test]
    public void TruncationNeverSplitsAMultiByteGrapheme()
    {
        var bindings = NamingEngineTestSupport.Bind(
            null,
            NamingEngineTestSupport.EntryItem(title: "Ærøskøbing Ærøskøbing Ærøskøbing"));

        var rendered = Render("{Entry Title}", bindings, new RenderOptions { MaxComponentBytes = 20 });

        Encoding.UTF8.GetByteCount(rendered).Should().BeLessThanOrEqualTo(20);
        rendered.Should().NotContain("�");
    }

    [Test]
    public void SeparatorRunsCollapseAcrossFragmentBoundaries()
    {
        var bindings = NamingEngineTestSupport.Bind(null, NamingEngineTestSupport.EntryItem(title: "Trailing."));

        Render("{Entry Title:dot}.{Quality Title}", bindings).Should().NotContain("..");
    }

    [Test]
    public void AReservedDeviceNameIsDefused()
    {
        var bindings = NamingEngineTestSupport.Bind(null, NamingEngineTestSupport.EntryItem(title: "CON"));

        Render("{Entry Title}", bindings).Should().NotBe("CON");
    }

    [Test]
    public void MultiUnitStylesRenderFromDeclaredRows()
    {
        var repeat = new Abstractions.Definition.MultiUnitStyle { StyleId = "repeat", Joiner = "E" };
        var range = new Abstractions.Definition.MultiUnitStyle { StyleId = "range", Joiner = "-", RangeOnly = true };
        var restate = new Abstractions.Definition.MultiUnitStyle { StyleId = "restate", Joiner = " ", RestateOuter = true, RepeatPrefix = "E" };

        MultiUnitStyleRenderer.Render(repeat, [3, 4, 5], "S01", 2).Should().Be("03E04E05");
        MultiUnitStyleRenderer.Render(range, [3, 4, 5], "S01", 2).Should().Be("03-05");
        MultiUnitStyleRenderer.Render(restate, [3, 4], "S01", 2).Should().Be("03 S01E04");
        MultiUnitStyleRenderer.Render(range, [7], "S01", 2).Should().Be("07");
    }
}
