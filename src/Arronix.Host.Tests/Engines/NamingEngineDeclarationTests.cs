using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Naming;
using FluentAssertions;


namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The declaration-driven half: template selection rows, the folder spine with inserted segments,
/// declared token fallbacks, and the rename-policy seam a declared kind registers through.
/// </summary>
[TestFixture]
internal sealed class NamingEngineDeclarationTests
{
    private static NamingEngine Engine() => new(Declaration(), languages: NamingEngineTestSupport.Languages());

    private static NamingDeclaration Declaration() => new()
    {
        DefaultTemplates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["file"] = "{Entry Title} ({Entry Year}) {Quality Title}",
            ["flat"] = "{Entry Title} - {Work Title}",
            ["folder"] = "{Entry Title:the} ({Entry Year})",
            ["group-folder"] = "{Entry Title:first}",
        },
        Selection =
        [
            new TemplateSelectionRule
            {
                RuleId = "flat-when-asked",
                When = new TagPredicate([new PredicateAtom { Subject = "options.flat", Op = PredicateOp.Equals, Values = ["true"] }]),
                TemplateId = "flat",
                FallbackTemplateId = "file",
            },
            new TemplateSelectionRule
            {
                RuleId = "group-by-letter",
                When = new TagPredicate([new PredicateAtom { Subject = "options.grouped", Op = PredicateOp.Equals, Values = ["true"] }]),
                InsertSpineSegment = "group-folder",
            },
        ],
        FolderSpine = "{root}/[group-folder/]{folder}",
        Fallbacks =
        [
            new TokenFallbackRule { Token = "{Original Title}", Order = ["file.SceneName", "file.OriginalFileName"] },
        ],
    };

    private static Func<string, IReadOnlyList<string>?> Options(params (string Key, string Value)[] options) =>
        subject =>
        {
            foreach (var (key, value) in options)
            {
                if (string.Equals(subject, key, StringComparison.Ordinal))
                {
                    return [value];
                }
            }

            return null;
        };

    private static NamingTokenBindings FullBindings() => NamingEngineTestSupport.Bind(
        NamingEngineTestSupport.File(),
        NamingEngineTestSupport.EntryItem(),
        NamingEngineTestSupport.WorkItem());

    [Test]
    public void AnInvalidDeclaredTemplateRefusesTheDeclaration()
    {
        var declaration = Declaration() with
        {
            DefaultTemplates = new Dictionary<string, string>(StringComparer.Ordinal) { ["file"] = "{Broken" },
        };

        var construction = () => new NamingEngine(declaration);

        construction.Should().Throw<ArgumentException>().WithMessage("*file*");
    }

    [Test]
    public void SelectionRowsRunInDeclaredOrderAndFirstPassingRowWins()
    {
        var engine = Engine();

        engine.SelectSlot("file", Options(("options.flat", "true")), FullBindings()).Should().Be("flat");
        engine.SelectSlot("file", Options(), FullBindings()).Should().Be("file");
    }

    [Test]
    public void AChosenTemplateMissingItsTokensDegradesToTheDeclaredFallback()
    {
        var engine = Engine();

        // No work item bound: {Work Title} has no value, so the row's fallback slot is taken.
        var entryOnly = NamingEngineTestSupport.Bind(null, NamingEngineTestSupport.EntryItem());

        engine.SelectSlot("file", Options(("options.flat", "true")), entryOnly).Should().Be("file");
    }

    [Test]
    public void TheSpineRendersItsSegmentsAndHonorsInsertedOnes()
    {
        var engine = Engine();
        var bindings = FullBindings();

        engine.RenderSpine(Options(), bindings, null)
            .Should().Equal("Fixture Show, The (2020)");

        engine.RenderSpine(Options(("options.grouped", "true")), FullBindings(), null)
            .Should().Equal("T", "Fixture Show, The (2020)");
    }

    [Test]
    public void ADeclaredFallbackRowFillsAnUnboundToken()
    {
        var engine = Engine();
        var file = NamingEngineTestSupport.File() with { SceneName = "Fixture.Show.2020.1080p-GRP" };

        engine.RenderTemplate("{Original Title}", NamingEngineTestSupport.Bind(null), file)
            .Should().Be("Fixture.Show.2020.1080p-GRP");
    }

    [Test]
    public void ATemplateRenderingNothingFallsBackToTheOriginalStem()
    {
        var engine = Engine();

        engine.RenderTemplate("{Quality Real}", NamingEngineTestSupport.Bind(), NamingEngineTestSupport.File())
            .Should().Be("original.file.name");
    }

    [Test]
    public async Task ThePolicySeamRendersFromAnItemChainAndFileFacts()
    {
        var policy = Policy();

        var name = await policy.GenerateFileNameAsync(
            MediaItemId.FromInt64(2),
            NamingEngineTestSupport.File(),
            "{Entry Title} - R{Run:00}I{Index:00} - {Work Title} {Quality Title}-{Release Group}");

        name.Should().Be("The Fixture Show - R01I03 - A Long Awaited Part HD-1080p-GROUP");
    }

    [Test]
    public async Task ResolvedTokensCarryTheirDisplaySpellings()
    {
        var tokens = await Policy().ResolveTokensAsync(MediaItemId.FromInt64(2));

        tokens.Should().ContainKey("{Entry Title}").WhoseValue.Should().Be("The Fixture Show");
        tokens.Should().ContainKey("{Work Title}").WhoseValue.Should().Be("A Long Awaited Part");
        tokens.Should().ContainKey("{Index}").WhoseValue.Should().Be("3");
    }

    [Test]
    public void TheValidatorRefusesATokenTheShapeCannotDerive()
    {
        var policy = Policy();

        policy.ValidateTemplate("{Entry Title} ({Entry Year})").Should().BeTrue();
        policy.ValidateTemplate("{Entry Titel}").Should().BeFalse();
        policy.ValidateTemplate("{Broken").Should().BeFalse();
    }

    private static DeclarativeRenamePolicy Policy() => new(
        NamingEngineTestSupport.Kind,
        NamingEngineTestSupport.Shape(),
        Declaration(),
        new FixtureResolver());

    private sealed class FixtureResolver : INamingItemResolver
    {
        public Task<ItemView?> GetItemAsync(MediaItemId itemId, CancellationToken cancellationToken = default)
        {
            var item = itemId.ToInt64() switch
            {
                1 => NamingEngineTestSupport.EntryItem(),
                2 => NamingEngineTestSupport.WorkItem(),
                _ => null,
            };

            return Task.FromResult(item);
        }
    }
}
