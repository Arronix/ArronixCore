using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Host.Tests.Support;

// Definition, shape and intent contracts are experimental; these fixtures declare against them.
#pragma warning disable ARX0013
#pragma warning disable ARX0016
#pragma warning disable ARX0019

#pragma warning disable ARX0020 // The typed media surface is experimental; these fixtures build one.

namespace Arronix.Host.Tests.DefinitionBinding;

/// <summary>
/// Definitions the declarative registry-path tests are written against, built over the shared shape
/// fixtures so a definition test asserts definition rules, never shape rules.
/// </summary>
/// <remarks>
/// <see cref="Sound"/> is the reference: it exercises every cross-reference family the validator checks —
/// coordinate captures, guard references, rung rows, query tiers, a coordinate spelling, a strategy
/// binding, a vocabulary statement and a corpus case — against <see cref="ShapeFixtures.Layered"/>. Tests
/// produce one malformed definition per rule by editing it with <c>with</c>, the same idiom the shape
/// defect corpus uses.
/// </remarks>
internal static class DefinitionFixtures
{
    /// <summary>
    /// The structure the sound model is checked against.
    /// </summary>
    /// <returns>The shape.</returns>
    internal static MediaShape Shape() => ShapeFixtures.Layered();

    /// <summary>
    /// The intent surface that accompanies the sound model.
    /// </summary>
    /// <returns>The surface.</returns>
    internal static PluginIntentSurface Intent() => new() { MediaKind = ShapeFixtures.Kind };

    /// <summary>
    /// A model that passes every check.
    /// </summary>
    /// <returns>The model.</returns>
    internal static MediaKindModel Sound() => new()
    {
        Parsing = new ParseDeclaration
        {
            Guards =
            [
                // Deliberately raw-input and case-sensitive: the F-9 amendment's two dimensions.
                new GuardPattern("revision-token", @"\bREAL\b", GuardInput.Raw, CaseSensitive: true),
            ],
            TitlePatterns =
            [
                new TitlePattern
                {
                    PatternId = "coordinate",
                    Regex = @"^(?<title>.+?)\s+(?<group>\d+)x(?<index>\d+)$",
                    Captures =
                    [
                        new CaptureBinding("title", CaptureTarget.TitleText),
                        new CaptureBinding(
                            "group",
                            CaptureTarget.CoordinateComponent,
                            ShapeFixtures.OrdinalSpaceId,
                            ShapeFixtures.GroupComponentId),
                        new CaptureBinding(
                            "index",
                            CaptureTarget.CoordinateComponent,
                            ShapeFixtures.OrdinalSpaceId,
                            ShapeFixtures.IndexComponentId),
                    ],
                    Guards = [new GuardRef("revision-token", Negated: true)],
                },
                new TitlePattern
                {
                    PatternId = "title-only",
                    Regex = "^(?<title>.+)$",
                    Captures = [new CaptureBinding("title", CaptureTarget.TitleText)],
                },
            ],
            RungResolution = new RungResolutionTable
            {
                Rules =
                [
                    new RungRule
                    {
                        RuleId = "guarded-high",
                        When = new TagPredicate(
                        [
                            new PredicateAtom
                            {
                                Subject = "guard:revision-token",
                                Op = PredicateOp.GuardMatches,
                            },
                        ]),
                        TierId = "High",
                    },
                    new RungRule { RuleId = "everything-else", When = TagPredicate.Always, TierId = "Low" },
                ],
                UnknownTierId = "Unknown",
            },
        },
        Matching = new MatchDeclaration
        {
            Entry = new EntryResolution
            {
                IdentifierOrder = [],
                Layers =
                [
                    new MatchLayer
                    {
                        LayerId = "own-title",
                        KeyTemplate = "{title}",
                        NormalizerId = "strip-non-alnum-upper",
                        PreferSpaceId = ShapeFixtures.OrdinalSpaceId,
                    },
                ],
            },
            Units =
            [
                new UnitResolutionRule
                {
                    Spaces =
                    [
                        new SpaceAttempt { SpaceId = ShapeFixtures.OrdinalSpaceId },
                        new SpaceAttempt
                        {
                            SpaceId = ShapeFixtures.OrdinalSpaceId,
                            Kind = SpaceAttemptKind.TitleLookup,
                            NormalizerId = "strip-non-alnum-upper",
                        },
                    ],
                },
            ],
            Confidence = [new ConfidenceRule(MatchBasis.Identifier, null, MatchConfidence.Exact)],
            Variant = new VariantChoiceDeclaration
            {
                FeatureCatalogId = "candidate-distance",
                Features = [new FeatureParameter { FeatureId = "title", Weight = 1.0 }],
            },
        },
        Querying = new QueryDeclaration
        {
            Tiers =
            [
                new QueryTierTemplate
                {
                    TierId = "unit-text",
                    SearchKindId = "unit",
                    FreeTextTemplate = "{title}",
                },
            ],
            Grammar = new CoordinateGrammar
            {
                Spellings =
                [
                    new CoordinateSpelling
                    {
                        SpellingId = "group-index",
                        SpaceId = ShapeFixtures.OrdinalSpaceId,
                        Template = "{0}x{00}",
                    },
                ],
            },
        },
        Corpus =
        [
            new CorpusCase
            {
                CaseId = "plain-title",
                Input = "Something 1x02",
                ExpectedPatternId = "coordinate",
                ExpectedTierId = "Low",
            },
        ],
    };

    /// <summary>
    /// Applies an edit to the parsing section, so a test states one fault without restating the rest.
    /// </summary>
    /// <param name="model">The sound model.</param>
    /// <param name="edit">The fault to introduce.</param>
    /// <returns>The edited model.</returns>
    internal static MediaKindModel WithParsing(
        this MediaKindModel model,
        Func<ParseDeclaration, ParseDeclaration> edit)
        => model with { Parsing = edit(model.Parsing) };
}
