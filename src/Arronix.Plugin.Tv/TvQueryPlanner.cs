#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Tv.Seed;

namespace Arronix.Plugin.Tv;

/// <summary>
/// Turns "acquire these units" into an ordered set of indexer queries.
/// </summary>
/// <remarks>
/// <para>The reference implementation needed seven <c>Fetch</c> overloads to express what this class
/// expresses as four declared search kinds and a tiered plan: three addressing schemes crossed with unit and
/// whole-run, plus out-of-run entries. Overload resolution is a compile-time mechanism and cannot survive an
/// extension boundary, so the arity has to move into data — and once it is data, adding a fifth media kind
/// touches no core interface.</para>
/// <para>Which strings to search for is entirely this extension's business: the entry title, its alternative
/// titles, and — for an entry that opts into the release-community numbering — the alias coordinate rendered
/// the way that community writes it, which is a different string from the canonical one.</para>
/// </remarks>
public sealed class TvQueryPlanner : IReleaseQueryPlanner
{
    private readonly TvCatalog _catalog;

    /// <summary>Creates a planner over a catalog.</summary>
    /// <param name="catalog">The catalog the units are drawn from.</param>
    public TvQueryPlanner(TvCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public MediaKindId MediaKind => TvIds.MediaKind;

    /// <inheritdoc />
    public Task<ReleaseQueryPlan> PlanAsync(
        AcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var units = request.Units
            .Where(unit => unit.Level == TvIds.EpisodeLevel)
            .Select(unit => _catalog.TryGetEpisode(unit.Id.Value, out var episode) ? episode : null)
            .Where(episode => episode is not null)
            .Select(episode => episode!)
            .ToList();

        if (units.Count == 0)
        {
            return Task.FromResult(new ReleaseQueryPlan([]));
        }

        var series = ResolveSeries(units);

        if (series is null)
        {
            return Task.FromResult(new ReleaseQueryPlan([]));
        }

        var plan = request.SearchKindId switch
        {
            TvIds.UnitSearchKindId => PlanUnit(series, units, request.Origin),
            TvIds.SeasonPackSearchKindId => PlanWholeRun(series, units, request.Origin),
            TvIds.DailySearchKindId => PlanCalendar(series, units, request.Origin),
            TvIds.SeriesSearchKindId => PlanWholeEntry(series, request.Origin),
            _ => new ReleaseQueryPlan([])
        };

        return Task.FromResult(plan);
    }

    private static ReleaseQueryPlan PlanUnit(
        TvSeriesRecord series,
        IReadOnlyList<TvEpisodeRecord> units,
        SearchOrigin origin)
    {
        var identifierTier = new List<ReleaseQuery>();
        var textTier = new List<ReleaseQuery>();

        foreach (var unit in units)
        {
            var arguments = new List<SearchArgument>
            {
                new(
                    SearchTerm.Ordinal,
                    FieldValue.OfInteger(unit.SeasonNumber),
                    TvIds.SeasonComponentId),
                new(
                    SearchTerm.Ordinal,
                    FieldValue.OfInteger(unit.EpisodeNumber),
                    TvIds.EpisodeComponentId)
            };

            identifierTier.Add(Query(
                series,
                TvIds.UnitSearchKindId,
                $"{QueryTitle(series)} {RenderOrdinalPair(unit.SeasonNumber, unit.EpisodeNumber)}",
                origin,
                [
                    .. arguments,
                    new SearchArgument(
                        SearchTerm.ExternalIdentifier,
                        FieldValue.OfExternalIdentifier(ExternalId.Of(
                            TvIds.TvdbScheme,
                            series.TvdbId.ToString(CultureInfo.InvariantCulture))))
                ]));

            // Tier two drops the identifier and adds the strings this entry is actually posted under,
            // including - for an entry that uses it - the alias coordinate, which is a different string
            // from the canonical one and is the whole reason the alias space exists.
            var aliases = new List<string>(Aliases(series));

            if (series.UsesAliasNumbering
                && unit.AliasSeasonNumber is { } aliasSeason
                && unit.AliasEpisodeNumber is { } aliasEpisode)
            {
                aliases.Add($"{QueryTitle(series)} {RenderOrdinalPair(aliasSeason, aliasEpisode)}");
            }

            if (unit.AbsoluteNumber is { } absolute)
            {
                aliases.Add($"{QueryTitle(series)} {absolute.ToString("000", CultureInfo.InvariantCulture)}");
            }

            textTier.Add(Query(
                series,
                TvIds.UnitSearchKindId,
                $"{QueryTitle(series)} {RenderOrdinalPair(unit.SeasonNumber, unit.EpisodeNumber)}",
                origin,
                arguments,
                aliases));
        }

        return new ReleaseQueryPlan([new ReleaseQueryTier(identifierTier), new ReleaseQueryTier(textTier)]);
    }

    private static ReleaseQueryPlan PlanWholeRun(
        TvSeriesRecord series,
        IReadOnlyList<TvEpisodeRecord> units,
        SearchOrigin origin)
    {
        var runs = units.Select(unit => unit.SeasonNumber).Distinct().OrderBy(run => run).ToList();
        var queries = new List<ReleaseQuery>(runs.Count);

        foreach (var run in runs)
        {
            queries.Add(Query(
                series,
                TvIds.SeasonPackSearchKindId,
                $"{QueryTitle(series)} S{run.ToString("00", CultureInfo.InvariantCulture)}",
                origin,
                [new SearchArgument(
                    SearchTerm.Ordinal,
                    FieldValue.OfInteger(run),
                    TvIds.SeasonComponentId)],
                [
                    .. Aliases(series),
                    $"{QueryTitle(series)} Season {run.ToString(CultureInfo.InvariantCulture)}",
                    $"{QueryTitle(series)} S{run.ToString("00", CultureInfo.InvariantCulture)} Complete"
                ]));
        }

        return new ReleaseQueryPlan([new ReleaseQueryTier(queries)]);
    }

    private static ReleaseQueryPlan PlanCalendar(
        TvSeriesRecord series,
        IReadOnlyList<TvEpisodeRecord> units,
        SearchOrigin origin)
    {
        var queries = new List<ReleaseQuery>();

        foreach (var unit in units)
        {
            if (unit.AirDate is not { } airDate)
            {
                continue;
            }

            var iso = airDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // The date is rendered into the free text as well as passed as an argument. Almost no indexer
            // accepts a date parameter for this media kind, which is why the search kind declares no
            // required term and why the two spellings are both offered as aliases.
            queries.Add(Query(
                series,
                TvIds.DailySearchKindId,
                $"{QueryTitle(series)} {iso}",
                origin,
                [new SearchArgument(SearchTerm.Date, FieldValue.OfDate(airDate))],
                [
                    .. Aliases(series),
                    $"{QueryTitle(series)} {airDate.ToString("yyyy MM dd", CultureInfo.InvariantCulture)}",
                    $"{QueryTitle(series)} {airDate.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)}"
                ]));
        }

        return new ReleaseQueryPlan([new ReleaseQueryTier(queries)]);
    }

    private static ReleaseQueryPlan PlanWholeEntry(TvSeriesRecord series, SearchOrigin origin)
        => new(
        [
            new ReleaseQueryTier(
            [
                Query(
                    series,
                    TvIds.SeriesSearchKindId,
                    QueryTitle(series),
                    origin,
                    [new SearchArgument(SearchTerm.WorkTitle, FieldValue.OfText(series.Title))],
                    [.. Aliases(series), $"{QueryTitle(series)} Complete"])
            ])
        ]);

    private static ReleaseQuery Query(
        TvSeriesRecord series,
        string searchKindId,
        string freeText,
        SearchOrigin origin,
        IReadOnlyList<SearchArgument> arguments,
        IReadOnlyList<string>? aliases = null) => new()
        {
            MediaKind = TvIds.MediaKind,
            SearchKindId = searchKindId,
            FreeText = freeText,
            Aliases = aliases ?? Aliases(series),
            Arguments = arguments,
            Categories = TelevisionCategories,
            Limit = origin is SearchOrigin.Interactive or SearchOrigin.UserInvoked ? 100 : 200,
            Origin = origin
        };

    private static string QueryTitle(TvSeriesRecord series)
        => TvTitleNormalizer.ToQueryText(series.Title);

    private static string RenderOrdinalPair(int outer, int inner)
        => $"S{outer.ToString("00", CultureInfo.InvariantCulture)}E{inner.ToString("00", CultureInfo.InvariantCulture)}";

    private static IReadOnlyList<string> Aliases(TvSeriesRecord series)
        => [.. series.AlternativeTitles.Select(TvTitleNormalizer.ToQueryText)];

    private static IReadOnlyList<CategoryId> TelevisionCategories { get; } =
    [
        CategoryId.FromInt(5000),
        CategoryId.FromInt(5020),
        CategoryId.FromInt(5030),
        CategoryId.FromInt(5040),
        CategoryId.FromInt(5045),
        CategoryId.FromInt(5050),
        CategoryId.FromInt(5070)
    ];

    private TvSeriesRecord? ResolveSeries(IReadOnlyList<TvEpisodeRecord> units)
    {
        var seriesIds = units.Select(unit => unit.SeriesId).Distinct().ToList();

        // An acquisition request covers one library entry. Units from two entries is a caller defect, not a
        // query this extension can plan.
        return seriesIds.Count == 1
            && _catalog.TryGetSeries(seriesIds[0], out var series)
            && series is not null
                ? series
                : null;
    }
}
