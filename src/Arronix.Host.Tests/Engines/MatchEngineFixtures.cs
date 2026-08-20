using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Matching;


namespace Arronix.Host.Tests.Engines;

/// <summary>
/// A synthetic media kind for the match engine: a two-level hierarchy — works and their parts — with
/// the declaration shaped like the rendered exhibit, and an in-memory read window.
/// </summary>
internal static class MatchEngineFixtures
{
    internal static readonly MediaKindId Kind = MediaKindId.FromString("fixture");
    internal static readonly MediaLevelId EntryLevel = MediaLevelId.FromString("work");
    internal static readonly MediaLevelId UnitLevel = MediaLevelId.FromString("part");

    internal static MediaItemRef EntryRef(long id) => new(Kind, EntryLevel, MediaItemId.FromInt64(id));

    internal static MediaItemRef UnitRef(long id) => new(Kind, UnitLevel, MediaItemId.FromInt64(id));

    internal static ItemView Entry(
        long id,
        string title,
        long? year = null,
        long? secondaryYear = null,
        string? originalTitle = null,
        IReadOnlyList<string>? alternativeTitles = null,
        IReadOnlyList<ExternalId>? externalIds = null)
    {
        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        if (year is { } declaredYear)
        {
            fields["year"] = FieldValue.OfInteger(declaredYear);
        }

        if (secondaryYear is { } declaredSecondary)
        {
            fields["secondaryYear"] = FieldValue.OfInteger(declaredSecondary);
        }

        if (originalTitle is not null)
        {
            fields["originalTitle"] = FieldValue.OfText(originalTitle);
        }

        if (alternativeTitles is not null)
        {
            fields["alternativeTitles"] = new FieldValue
            {
                Kind = FieldValueKind.Text,
                Items = alternativeTitles.Select(FieldValue.OfText).ToArray(),
            };
        }

        return new ItemView
        {
            Ref = EntryRef(id),
            Title = title,
            Fields = fields,
            ExternalIds = externalIds ?? [],
        };
    }

    internal static ItemView Unit(long id, string title, params CoordinateReading[] coordinates) => new()
    {
        Ref = UnitRef(id),
        Parent = null,
        Title = title,
        Fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal),
        Coordinates = CoordinateSet.Of(coordinates),
    };

    /// <summary>A unit addressed by a singleton position in the <c>only</c> space.</summary>
    internal static ItemView SingletonUnit(long id, string title) =>
        Unit(id, title, new CoordinateReading("only", Coordinate.Singleton, CoordinateConfidence.Verified));

    /// <summary>The exhibit-shaped declaration: four key layers, year agreement, one singleton unit row.</summary>
    internal static MatchDeclaration Declaration() => new()
    {
        Entry = new EntryResolution
        {
            IdentifierOrder = ["aaa", "bbb"],
            ScopeReplacesSearch = true,
            Layers =
            [
                new MatchLayer
                {
                    LayerId = "own-title",
                    KeyTemplate = "{title}|{originalTitle}",
                    NormalizerId = MatchKeyNormalizers.CleanTitle,
                },
                new MatchLayer
                {
                    LayerId = "roman-rewrite",
                    KeyTemplate = "{title}",
                    NormalizerId = MatchKeyNormalizers.CleanTitle,
                    ExpanderIds = [MatchKeyExpanders.RomanNumeralVariants],
                },
                new MatchLayer
                {
                    LayerId = "alternative-titles",
                    KeyTemplate = "{alternativeTitles}",
                    NormalizerId = MatchKeyNormalizers.CleanTitle,
                    ExpanderIds = [MatchKeyExpanders.RomanNumeralVariants],
                },
            ],
            Agreements =
            [
                new AgreementRule
                {
                    RuleId = "year-agrees",
                    Subject = "reading.TitleYear",
                    AgreesWith = ["entry.year", "entry.secondaryYear"],
                    AbsentAgrees = true,
                    MinimumValue = 1800,
                },
            ],
            Ambiguity = AmbiguityPolicy.Reject,
        },
        Units =
        [
            new UnitResolutionRule
            {
                ReleaseKind = null,
                Spaces = [new SpaceAttempt { SpaceId = "only" }],
                Expansion = SpanExpansion.None,
            },
        ],
        Confidence =
        [
            new ConfidenceRule(MatchBasis.Identifier, null, MatchConfidence.Exact),
            new ConfidenceRule(MatchBasis.TitleWithYear, null, MatchConfidence.High),
            new ConfidenceRule(
                MatchBasis.TitleOnly,
                CoordinateConfidence.Verified,
                MatchConfidence.Medium,
                SourceIn: [MatchSource.ReleaseName]),
            new ConfidenceRule(MatchBasis.TitleOnly, null, MatchConfidence.Low),
            new ConfidenceRule(MatchBasis.Coordinate, null, MatchConfidence.High),
            new ConfidenceRule(MatchBasis.Scope, null, MatchConfidence.Medium),
        ],
        Variant = null,
    };

    internal static DeclarativeMatcher Matcher(MatchDeclaration declaration, StubReader reader) =>
        new(
            Kind,
            declaration,
            MatchStrategyRegistry.CreateDefault(TimeProvider.System),
            reader);

    internal static MatchRequest Request(
        string text,
        string? parsedTitle = null,
        string? parsedYear = null,
        MatchSource source = MatchSource.ReleaseName,
        MediaItemRef? scope = null,
        CoordinateSet? coordinates = null,
        IReadOnlyList<ExternalId>? externalIds = null,
        IReadOnlyDictionary<string, string>? metadata = null) => new()
    {
        MediaKind = Kind,
        Text = text,
        Parsed = parsedTitle is null && parsedYear is null && metadata is null
            ? null
            : new ParsedRelease(Kind, parsedTitle ?? text, parsedYear, AdditionalMetadata: metadata),
        Scope = scope,
        Source = source,
        Coordinates = coordinates ?? CoordinateSet.Empty,
        ExternalIds = externalIds ?? [],
    };

    /// <summary>An in-memory read window over fixture entries and units.</summary>
    internal sealed class StubReader : IMatchEntryReader
    {
        private readonly List<ItemView> _entries = [];
        private readonly Dictionary<MediaItemRef, List<ItemView>> _units = [];

        internal StubReader Add(ItemView entry, params ItemView[] units)
        {
            _entries.Add(entry);
            _units[entry.Ref] = [.. units];
            return this;
        }

        internal StubReader AddChildren(MediaItemRef parent, params ItemView[] children)
        {
            _units[parent] = [.. children];
            return this;
        }

        public Task<MediaItemRef?> ResolveExternalAsync(
            MediaKindId kind,
            ExternalId externalId,
            CancellationToken cancellationToken = default)
        {
            var match = _entries.FirstOrDefault(entry => entry.ExternalIds.Contains(externalId));
            return Task.FromResult<MediaItemRef?>(match?.Ref);
        }

        public Task<IReadOnlyList<ItemView>> GetEntriesAsync(
            MediaKindId kind,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ItemView>>(_entries);

        public Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entries.FirstOrDefault(entry => entry.Ref == reference)
                ?? _units.Values.SelectMany(units => units).FirstOrDefault(unit => unit.Ref == reference));

        public Task<IReadOnlyList<ItemView>> GetUnitsAsync(
            MediaItemRef entry,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ItemView>>(
                _units.TryGetValue(entry, out var units) ? units : []);
    }
}
