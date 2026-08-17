using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

#pragma warning disable ARX0013 // Shape contracts are experimental; these fixtures declare one.
#pragma warning disable ARX0019 // Definition contracts are experimental; these fixtures declare one.
#pragma warning disable ARX0020 // The typed media surface is experimental; these fixtures declare one.

namespace Arronix.Plugins.Tests.Support;

/// <summary>
/// The smallest well-formed media-kind models the registration tests need.
/// </summary>
/// <remarks>
/// The registry gates on capabilities and records; it does not validate section contents — that is the
/// host's admission gate. These fixtures therefore carry exactly one row per required section, and the
/// section-variant builders exist so a test can state "this kind also declares a catalog" without restating
/// the rest.
/// </remarks>
internal static class MediaKindModels
{
    /// <summary>
    /// A model with the required sections only: every defaulted section is left at its default.
    /// </summary>
    /// <returns>The model.</returns>
    public static MediaKindModel RequiredSectionsOnly() => new()
    {
        Parsing = new ParseDeclaration
        {
            TitlePatterns =
            [
                new TitlePattern
                {
                    PatternId = "title-only",
                    Regex = "^(?<title>.+)$",
                    Captures = [new CaptureBinding("title", CaptureTarget.TitleText)],
                },
            ],
            RungResolution = new RungResolutionTable
            {
                Rules = [new RungRule { RuleId = "standard", When = TagPredicate.Always, TierId = "HD" }],
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
                    },
                ],
            },
            Units = [new UnitResolutionRule { Spaces = [] }],
            Confidence = [new ConfidenceRule(MatchBasis.Identifier, null, MatchConfidence.Exact)],
        },
        Querying = new QueryDeclaration
        {
            Tiers =
            [
                new QueryTierTemplate
                {
                    TierId = "text",
                    SearchKindId = "item",
                    FreeTextTemplate = "{title}",
                },
            ],
            Grammar = CoordinateGrammar.None,
        },
    };

    /// <summary>
    /// The same model, additionally declaring a catalog section.
    /// </summary>
    /// <returns>The model.</returns>
    public static MediaKindModel WithCatalog()
        => RequiredSectionsOnly() with
        {
            Catalog = new CatalogDeclaration
            {
                Requests = [new RequestTemplate { RequestId = "lookup", Verb = "GET", Route = "/items" }],
                Responses =
                [
                    new ResponseMap
                    {
                        ExternalIdPath = "$.id",
                        ExternalIdScheme = "catalog",
                        Rows = [],
                    },
                ],
            },
        };

    /// <summary>
    /// The same model, additionally declaring a non-default quality section.
    /// </summary>
    /// <returns>The model.</returns>
    public static MediaKindModel WithQuality()
        => RequiredSectionsOnly() with
        {
            Quality = new QualityDeclaration { Fallback = RungFallback.Nearest },
        };

    /// <summary>
    /// The same model, additionally declaring a non-default naming section.
    /// </summary>
    /// <returns>The model.</returns>
    public static MediaKindModel WithNaming()
        => RequiredSectionsOnly() with
        {
            Naming = new NamingDeclaration { FolderSpine = "{root}/{title}" },
        };

    /// <summary>
    /// The same model, additionally declaring a non-default notification section.
    /// </summary>
    /// <returns>The model.</returns>
    public static MediaKindModel WithNotifications()
        => RequiredSectionsOnly() with
        {
            Notifications = new NotificationDeclaration { HeadlineTemplate = "{title} arrived" },
        };
}
