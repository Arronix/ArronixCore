using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;
using Arronix.Host.Media;
using Arronix.Host.Tests.Support;
using FluentAssertions;


namespace Arronix.Host.Tests.DefinitionBinding;

/// <summary>
/// The model gate: every cross-reference resolves at load or the model is refused with a
/// diagnostic naming the row.
/// </summary>
[TestFixture]
internal sealed class DefinitionValidationTests
{
    private static bool Validate(
        MediaKindModel model,
        out ValidatedDefinition? validated,
        out IReadOnlyList<ShapeDefect> defects)
        => ValidatedDefinition.TryValidate(
            ShapeFixtures.Kind,
            DefinitionFixtures.Shape(),
            DefinitionFixtures.Intent(),
            model,
            out validated,
            out defects);

    private static IReadOnlyList<ShapeDefect> DefectsOf(MediaKindModel model)
    {
        Validate(model, out _, out var defects)
            .Should().BeFalse("the edited model carries a deliberate fault");
        return defects;
    }

    [Test]
    public void ASoundModelValidatesAndIsHeldVerbatim()
    {
        var model = DefinitionFixtures.Sound();

        Validate(model, out var validated, out var defects)
            .Should().BeTrue(string.Join("; ", defects.Select(defect => $"{defect.Path}: {defect.Message}")));

        validated!.Model.Should().BeSameAs(
            model,
            "what the engines execute and the wire publishes is what was derived");
        validated.Kind.Should().Be(ShapeFixtures.Kind);
    }

    [Test]
    public void RuleOrderSurvivesValidationByteForByte()
    {
        var model = DefinitionFixtures.Sound();

        Validate(model, out var validated, out _);

        validated!.Model.Parsing!.TitlePatterns
            .Select(pattern => pattern.PatternId)
            .Should().Equal("coordinate", "title-only");
    }

    [Test]
    public void ResolvedLookupsAreTotalAfterTheGate()
    {
        Validate(DefinitionFixtures.Sound(), out var validated, out _);

        validated!.GuardOf("revision-token").CaseSensitive.Should().BeTrue();
        validated.PatternOf("coordinate").Captures.Should().HaveCount(3);
    }

    [Test]
    public void AMalformedShapeShortCircuitsTheSectionChecks()
    {
        var model = DefinitionFixtures.Sound() with
        {
            Querying = DefinitionFixtures.Sound().Querying with
            {
                // A section fault that would be reported if section checks ran.
                Tiers = [DefinitionFixtures.Sound().Querying.Tiers[0] with { SearchKindId = "nonexistent" }],
            },
        };

        // A structure with no levels does not resolve, so nothing above it can be checked against it.
        var broken = DefinitionFixtures.Shape() with { Levels = [] };

        ValidatedDefinition.TryValidate(
                ShapeFixtures.Kind,
                broken,
                DefinitionFixtures.Intent(),
                model,
                out _,
                out var defects)
            .Should().BeFalse();

        defects.Should().NotBeEmpty();
        defects.Should().NotContain(
            defect => defect.Path.StartsWith("querying", StringComparison.Ordinal),
            "a section cannot be checked against a structure that did not resolve");
    }

    [Test]
    public void ACaptureNamingAnUndeclaredCoordinateSpaceIsRefused()
    {
        var model = DefinitionFixtures.Sound().WithParsing(parsing => parsing with
        {
            TitlePatterns =
            [
                parsing.TitlePatterns[0] with
                {
                    Captures =
                    [
                        .. parsing.TitlePatterns[0].Captures.Take(1),
                        new CaptureBinding("group", CaptureTarget.CoordinateComponent, "imaginary", "group"),
                        parsing.TitlePatterns[0].Captures[2],
                    ],
                },
                .. parsing.TitlePatterns.Skip(1),
            ],
        });

        DefectsOf(model).Should().Contain(defect =>
            defect.Path == "parsing.titlePatterns[0].captures[1]" && defect.Message.Contains("imaginary", StringComparison.Ordinal));
    }

    [Test]
    public void ACaptureNamingAnUndeclaredComponentIsRefused()
    {
        var model = DefinitionFixtures.Sound().WithParsing(parsing => parsing with
        {
            TitlePatterns =
            [
                parsing.TitlePatterns[0] with
                {
                    Captures =
                    [
                        .. parsing.TitlePatterns[0].Captures.Take(2),
                        new CaptureBinding("index", CaptureTarget.CoordinateComponent, "ordinal", "episode"),
                    ],
                },
                .. parsing.TitlePatterns.Skip(1),
            ],
        });

        DefectsOf(model).Should().Contain(defect => defect.Message.Contains("'episode'", StringComparison.Ordinal));
    }

    [Test]
    public void ACaptureGroupAbsentFromTheExpressionIsRefused()
    {
        var model = DefinitionFixtures.Sound().WithParsing(parsing => parsing with
        {
            TitlePatterns =
            [
                .. parsing.TitlePatterns.Take(1),
                parsing.TitlePatterns[1] with
                {
                    Captures = [new CaptureBinding("headline", CaptureTarget.TitleText)],
                },
            ],
        });

        DefectsOf(model).Should().Contain(defect => defect.Message.Contains("'headline'", StringComparison.Ordinal));
    }

    [Test]
    public void AGuardReferenceWithNoDeclarationIsRefused()
    {
        var model = DefinitionFixtures.Sound().WithParsing(parsing => parsing with
        {
            TitlePatterns =
            [
                parsing.TitlePatterns[0] with { Guards = [new GuardRef("br-disk")] },
                .. parsing.TitlePatterns.Skip(1),
            ],
        });

        DefectsOf(model).Should().Contain(defect => defect.Message.Contains("'br-disk'", StringComparison.Ordinal));
    }

    [Test]
    public void AnUnparseableExpressionIsRefusedNotThrown()
    {
        var model = DefinitionFixtures.Sound().WithParsing(parsing => parsing with
        {
            TitlePatterns =
            [
                .. parsing.TitlePatterns.Take(1),
                parsing.TitlePatterns[1] with { Regex = "(?<title>.+" },
            ],
        });

        DefectsOf(model).Should().Contain(defect =>
            defect.Path == "parsing.titlePatterns[1]" && defect.Message.Contains("does not parse", StringComparison.Ordinal));
    }

    [Test]
    public void AQueryTierNamingAnUndeclaredSearchKindIsRefused()
    {
        var model = DefinitionFixtures.Sound();
        model = model with
        {
            Querying = model.Querying with
            {
                Tiers = [model.Querying.Tiers[0] with { SearchKindId = "undeclared-search" }],
            },
        };

        DefectsOf(model).Should().Contain(defect =>
            defect.Path == "querying.tiers[0]" && defect.Message.Contains("undeclared-search", StringComparison.Ordinal));
    }

    [Test]
    public void ATitleLookupAttemptWithoutANormalizerIsRefused()
    {
        var model = DefinitionFixtures.Sound();
        model = model with
        {
            Matching = model.Matching with
            {
                Units =
                [
                    new UnitResolutionRule
                    {
                        Spaces =
                        [
                            new SpaceAttempt
                            {
                                SpaceId = Support.ShapeFixtures.OrdinalSpaceId,
                                Kind = SpaceAttemptKind.TitleLookup,
                            },
                        ],
                    },
                ],
            },
        };

        DefectsOf(model).Should().Contain(defect => defect.Message.Contains("title-lookup", StringComparison.Ordinal));
    }

    [Test]
    public void AUnitRowForAReleaseKindNoPatternCapturesIsRefused()
    {
        var model = DefinitionFixtures.Sound();
        model = model with
        {
            Matching = model.Matching with
            {
                Units =
                [
                    .. model.Matching.Units,
                    new UnitResolutionRule { ReleaseKind = "special", Spaces = [] },
                ],
            },
        };

        DefectsOf(model).Should().Contain(defect =>
            defect.Path == "matching.units[1]" && defect.Message.Contains("'special'", StringComparison.Ordinal));
    }

    /// <summary>
    /// Four cases used to sit here: an unknown strategy identifier, a strategy version beyond the host, a
    /// host verb the build does not speak, and an enum ordinal beyond the build's own maximum.
    /// </summary>
    /// <remarks>
    /// They are gone because what they checked is gone. A media kind no longer names a host strategy by
    /// string — the two the engines have are derived from the kind's own declaration — so there is no
    /// identifier that can be unknown, no version to negotiate, and no required-vocabulary section to state
    /// one in. A typed kind's vocabulary is whatever it compiled against, which the compiler already
    /// checked, and the strategy the matcher uses is chosen from its unit rules by
    /// <c>DeclarativeMatcher</c>. The replacement coverage is
    /// <c>MatchEngineAssignmentTests.AKindWhoseUnitsCannotSpanGetsNoAssignmentStrategy</c> and its
    /// counterpart, which pin the derivation those strings used to configure.
    /// </remarks>
    [Test]
    public void EveryFaultIsReportedNotOnlyTheFirst()
    {
        var model = DefinitionFixtures.Sound() with
        {
            Querying = DefinitionFixtures.Sound().Querying with
            {
                Tiers =
                [
                    DefinitionFixtures.Sound().Querying.Tiers[0] with { SearchKindId = "undeclared-search" },
                ],
            },
        };

        var defects = DefectsOf(model);

        defects.Should().Contain(defect => defect.Path == "querying.tiers[0]");
    }

}
