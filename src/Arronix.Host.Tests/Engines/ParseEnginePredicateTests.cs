using Arronix.Abstractions.Definition;
using Arronix.Host.Engines.Parsing;
using FluentAssertions;

// The definition contracts are experimental (ARX0019).
#pragma warning disable ARX0019

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The closed predicate vocabulary, and the load gate that refuses a definition whose cross-references
/// dangle — parse, don't validate, applied to the declaration itself.
/// </summary>
[TestFixture]
internal sealed class ParseEnginePredicateTests
{
    private static ParsePredicateContext Context(
        ScannedReleaseTags tags,
        string raw = "raw",
        string normalized = "normalized",
        CompiledGuardSet? guards = null) =>
        new(
            raw,
            normalized,
            tags,
            new Dictionary<string, string>(StringComparer.Ordinal),
            [],
            guards ?? new CompiledGuardSet([]));

    [Test]
    public void AnEmptyConjunctionHoldsVacuously() =>
        TagPredicateEvaluator.Holds(TagPredicate.Always, Context(ScannedReleaseTags.Empty))
            .Should().BeTrue();

    [Test]
    public void EqualsComparesCaseInsensitively()
    {
        var context = Context(new ScannedReleaseTags { SourceGroup = "WebDL" });

        TagPredicateEvaluator.Holds(
            new TagPredicate([ParseEngineFixtures.Src("webdl")]), context).Should().BeTrue();
    }

    [Test]
    public void AnAbsentSubjectFailsEveryComparisonAndPassesNegatedPresence()
    {
        var context = Context(ScannedReleaseTags.Empty);

        TagPredicateEvaluator.Holds(
            new TagPredicate([ParseEngineFixtures.Src("webdl")]), context).Should().BeFalse();
        TagPredicateEvaluator.Holds(
            new TagPredicate([ParseEngineFixtures.NoSource()]), context).Should().BeTrue();
        TagPredicateEvaluator.Holds(
            new TagPredicate([ParseEngineFixtures.ResAny()]), context).Should().BeFalse();
    }

    [Test]
    public void NumericComparisonsCompareNumbersNotText()
    {
        var context = Context(new ScannedReleaseTags { StatedResolution = 720 });

        var atLeast = new PredicateAtom
        {
            Subject = "tags.StatedResolution",
            Op = PredicateOp.GreaterOrEqual,
            Values = ["576"],
        };

        var atMost = new PredicateAtom
        {
            Subject = "tags.StatedResolution",
            Op = PredicateOp.LessOrEqual,
            Values = ["1080"],
        };

        TagPredicateEvaluator.Holds(new TagPredicate([atLeast, atMost]), context).Should().BeTrue();
    }

    /// <summary>A non-numeric operand cannot be at least or at most anything.</summary>
    [Test]
    public void ANonNumericOperandFailsNumericComparison()
    {
        var context = Context(new ScannedReleaseTags { SourceGroup = "webdl" });

        var atom = new PredicateAtom
        {
            Subject = "tags.SourceGroup",
            Op = PredicateOp.LessOrEqual,
            Values = ["1080"],
        };

        TagPredicateEvaluator.Holds(new TagPredicate([atom]), context).Should().BeFalse();
    }

    [Test]
    public void TokenTableTagsAreReachableThroughTheSameSubjects()
    {
        var tags = new ScannedReleaseTags
        {
            Extra = new Dictionary<string, string>(StringComparer.Ordinal) { ["flavor"] = "silver" },
        };

        var atom = new PredicateAtom
        {
            Subject = "tags.flavor",
            Op = PredicateOp.Equals,
            Values = ["silver"],
        };

        TagPredicateEvaluator.Holds(new TagPredicate([atom]), Context(tags)).Should().BeTrue();
    }

    [Test]
    public void AGuardChoosesItsInputAndItsCaseSensitivity()
    {
        var guards = new CompiledGuardSet(
        [
            new GuardPattern("on-raw", "MARKER", GuardInput.Raw),
            new GuardPattern("case-strict", "MARKER", GuardInput.Normalized, CaseSensitive: true),
        ]);

        var context = Context(ScannedReleaseTags.Empty, raw: "has MARKER", normalized: "has marker", guards: guards);

        TagPredicateEvaluator.Holds(
            new TagPredicate([ParseEngineFixtures.G("on-raw")]), context).Should().BeTrue();

        // The strict guard runs against the normalized text, whose marker is lower case.
        TagPredicateEvaluator.Holds(
            new TagPredicate([ParseEngineFixtures.G("case-strict")]), context).Should().BeFalse();
    }

    // ---- the load gate -----------------------------------------------------------------------------

    [Test]
    public void AnUndeclaredGuardRefusesTheDefinition()
    {
        var parsing = ParseEngineFixtures.Parsing();
        var broken = parsing with
        {
            RungResolution = parsing.RungResolution! with
            {
                Rules =
                [
                    ParseEngineFixtures.R("dangling", "SDTV", ParseEngineFixtures.G("no-such-guard")),
                ],
            },
        };

        var act = () => ParseEngineFixtures.Parser(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*no-such-guard*");
    }

    [Test]
    public void ARungRuleNamingAnUnknownTierRefusesTheDefinition()
    {
        var parsing = ParseEngineFixtures.Parsing();
        var broken = parsing with
        {
            RungResolution = parsing.RungResolution! with
            {
                Rules = [ParseEngineFixtures.R("dangling", "No-Such-Tier")],
            },
        };

        var act = () => ParseEngineFixtures.Parser(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*No-Such-Tier*");
    }

    [Test]
    public void ACaptureNamingAnUndeclaredGroupRefusesTheDefinition()
    {
        var parsing = ParseEngineFixtures.Parsing();
        var pattern = parsing.TitlePatterns[^1] with
        {
            Captures = [new CaptureBinding("no-such-group", CaptureTarget.TitleText)],
        };
        var broken = parsing with { TitlePatterns = [pattern] };

        var act = () => ParseEngineFixtures.Parser(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*no-such-group*");
    }

    [Test]
    public void ASubjectOutsideTheVocabularyRefusesTheDefinition()
    {
        var parsing = ParseEngineFixtures.Parsing();
        var broken = parsing with
        {
            RungResolution = parsing.RungResolution! with
            {
                Rules =
                [
                    ParseEngineFixtures.R(
                        "dangling",
                        "SDTV",
                        new PredicateAtom { Subject = "filesystem.path", Op = PredicateOp.Present }),
                ],
            },
        };

        var act = () => ParseEngineFixtures.Parser(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*filesystem.path*");
    }

    [Test]
    public void AWrongOperatorArityRefusesTheDefinition()
    {
        var parsing = ParseEngineFixtures.Parsing();
        var broken = parsing with
        {
            RungResolution = parsing.RungResolution! with
            {
                Rules =
                [
                    ParseEngineFixtures.R(
                        "dangling",
                        "SDTV",
                        new PredicateAtom
                        {
                            Subject = "tags.SourceGroup",
                            Op = PredicateOp.Equals,
                            Values = ["one", "two"],
                        }),
                ],
            },
        };

        var act = () => ParseEngineFixtures.Parser(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*exactly one value*");
    }

    [Test]
    public void AnUnknownTokenConstraintRefusesTheDefinition()
    {
        var parsing = ParseEngineFixtures.Parsing();
        var broken = parsing with
        {
            TokenTables =
            [
                new TokenTable
                {
                    TableId = "broken",
                    Rows = [new TokenRow(@"\d+", "count", Constraint: "prime")],
                },
            ],
        };

        var act = () => ParseEngineFixtures.Parser(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*prime*");
    }

    /// <summary>
    /// The relocation the critique forced (F-2), pinned: occurrence selection lives on the token scan,
    /// and the rung table deliberately has no rule-selection member at all.
    /// </summary>
    [Test]
    public void TheRungTableHasNoSelectionMode() =>
        typeof(RungResolutionTable).GetProperty("Selection").Should().BeNull();
}
