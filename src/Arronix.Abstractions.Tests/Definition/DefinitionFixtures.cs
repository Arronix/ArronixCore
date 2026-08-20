// Exercises the declarative media-kind area and the shape/intent types its sections reuse.

using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;


namespace Arronix.Abstractions.Tests.Definition;

/// <summary>
/// Builds the smallest well-formed values the definition fixtures share: a one-level shape, its intent
/// surface, and one row of each required section.
/// </summary>
internal static class DefinitionFixtures
{
    internal static MediaShape Shape() => new()
    {
        Kind = MediaKindId.FromString("example"),
        Name = "Example",
        PluralName = "Examples",
        Levels =
        [
            new MediaLevel
            {
                Id = MediaLevelId.FromString("item"),
                Name = "Item",
                PluralName = "Items",
                Identity = new LevelIdentity { HasCatalogRecord = true, HasLibraryRecord = true }
            }
        ],
        FileBinding = new FileBinding
        {
            AnchorLevelId = MediaLevelId.FromString("item"),
            UnitLevelId = MediaLevelId.FromString("item")
        },
        FormatFamilies =
        [
            new FormatFamily
            {
                FamilyId = "media",
                Name = "Media",
                FileExtensions = [".bin"],
                Ladder = [new QualityTier("Standard", Rank: 1)],
                Unknown = new QualityTier("Unknown", Rank: 0)
            }
        ],
        Tokens = []
    };

    internal static PluginIntentSurface Intent() => new()
    {
        MediaKind = MediaKindId.FromString("example")
    };

    internal static ParseDeclaration Parsing() => new()
    {
        TitlePatterns =
        [
            new TitlePattern
            {
                PatternId = "title-only",
                Regex = "^(?<title>.+)$",
                Captures = [new CaptureBinding("title", CaptureTarget.TitleText)]
            }
        ]
    };

    internal static MatchDeclaration Matching() => new()
    {
        Entry = new EntryResolution
        {
            IdentifierOrder = ["catalog"],
            Layers =
            [
                new MatchLayer
                {
                    LayerId = "own-title",
                    KeyTemplate = "{title}",
                    NormalizerId = "strip-non-alnum-upper"
                }
            ]
        },
        Units =
        [
            new UnitResolutionRule
            {
                Spaces = [new SpaceAttempt { SpaceId = "only" }]
            }
        ],
        Confidence =
        [
            new ConfidenceRule(MatchBasis.Identifier, null, MatchConfidence.Exact)
        ]
    };

    internal static QueryDeclaration Querying() => new()
    {
        Tiers =
        [
            new QueryTierTemplate
            {
                TierId = "text",
                SearchKindId = "item",
                FreeTextTemplate = "{title:query}"
            }
        ],
        Grammar = CoordinateGrammar.None
    };

    internal static MediaKindModel Model() => new()
    {
        Parsing = Parsing(),
        Matching = Matching(),
        Querying = Querying()
    };
}
