using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Parsing;
using Arronix.Host.Tests.Support;


namespace Arronix.Host.Tests.Engines;

/// <summary>A deliberately media-neutral title-reading fixture.</summary>
internal static class ParseEngineFixtures
{
    internal static MediaKindModel Model(ParseDeclaration? parsing = null) => new()
    {
        Parsing = parsing ?? Parsing(),
        Matching = Matching(),
        Querying = Querying(),
    };

    internal static DeclarativeReleaseParser Parser(ParseDeclaration? parsing = null) =>
        new(Shape(), Model(parsing));

    internal static MediaShape Shape() => ShapeFixtures.Fused();

    internal static ParseDeclaration Parsing() => new()
    {
        TitlePatterns =
        [
            new TitlePattern
            {
                PatternId = "numbered-run",
                Regex = @"^(?<title>.+?)[ ._]#(?<from>\d{1,3})-(?<to>\d{1,3})(?:[ ._]|$)",
                Captures = [new CaptureBinding("title", CaptureTarget.TitleText)],
                Expansion = new RangeExpansion { FromGroup = "from", ToGroup = "to", MaxSpan = 25 },
                Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.SequenceSpan },
            },
            new TitlePattern
            {
                PatternId = "title-then-year",
                Regex = @"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|(1(8|9)|20)\d{2}|\]|\W(1(8|9)|20)\d{2})))+(\W+|_|$)(?!\\)",
                Captures =
                [
                    new CaptureBinding("title", CaptureTarget.TitleText),
                    new CaptureBinding("year", CaptureTarget.TitleYear),
                ],
                Scope = new AcquisitionScope { Kind = AcquisitionScopeKind.Single },
            },
            new TitlePattern
            {
                PatternId = "year-then-title-folder",
                Regex = @"^(?:(?:[-_\W](?<![)!]))*(?<year>(19|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?<title>.+?)?$",
                Captures =
                [
                    new CaptureBinding("title", CaptureTarget.TitleText),
                    new CaptureBinding("year", CaptureTarget.TitleYear),
                ],
                Sources = [MatchSource.FolderName],
            },
        ],
        TokenTables =
        [
            new TokenTable
            {
                TableId = "embedded-ids",
                Rows = [new TokenRow(@"(?<catalogid>id\d{7,8})", "catalogId", Constraint: "length 9..10")],
            },
        ],
    };

    private static MatchDeclaration Matching() => new()
    {
        Entry = new EntryResolution
        {
            IdentifierOrder = ["fixture"],
            Layers = [new MatchLayer { LayerId = "title", KeyTemplate = "{title}", NormalizerId = "plain" }],
        },
        Units = [new UnitResolutionRule { Spaces = [new SpaceAttempt { SpaceId = "only" }] }],
        Confidence = [],
    };

    private static QueryDeclaration Querying() => new()
    {
        Tiers = [new QueryTierTemplate { TierId = "text", SearchKindId = "unit" }],
        Grammar = CoordinateGrammar.None,
    };
}
