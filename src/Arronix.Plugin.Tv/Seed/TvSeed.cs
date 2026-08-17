#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Tv.Seed;

/// <summary>
/// One library entry as this extension's catalog holds it.
/// </summary>
public sealed record TvSeriesRecord
{
    /// <summary>Catalog-local surrogate identifier.</summary>
    public required int Id { get; init; }

    /// <summary>TheTVDB identifier.</summary>
    public required int TvdbId { get; init; }

    /// <summary>IMDb identifier, including its <c>tt</c> prefix.</summary>
    public string? ImdbId { get; init; }

    /// <summary>Display title.</summary>
    public required string Title { get; init; }

    /// <summary>Collation key.</summary>
    public required string SortTitle { get; init; }

    /// <summary>Titles a release may legitimately use instead of <see cref="Title"/>.</summary>
    public IReadOnlyList<string> AlternativeTitles { get; init; } = [];

    /// <summary>Year of first transmission.</summary>
    public required int Year { get; init; }

    /// <summary>One of <see cref="TvStatuses"/>.</summary>
    public required string Status { get; init; }

    /// <summary>
    /// One of <see cref="TvAddressingSchemes"/>. Carried per entry, not per extension.
    /// </summary>
    public required string AddressingScheme { get; init; }

    /// <summary>Broadcaster.</summary>
    public string? Network { get; init; }

    /// <summary>Synopsis.</summary>
    public string? Overview { get; init; }

    /// <summary>Typical unit duration in minutes.</summary>
    public int RuntimeMinutes { get; init; }

    /// <summary>Age rating.</summary>
    public string? Certification { get; init; }

    /// <summary>Genres.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Cover artwork address.</summary>
    public Uri? Poster { get; init; }

    /// <summary>Date of first transmission.</summary>
    public DateOnly? FirstAired { get; init; }

    /// <summary>
    /// Whether releases for this entry are addressed in the provenance-sensitive alias space.
    /// </summary>
    /// <remarks>
    /// Consumption of the alias space is gated on this flag <b>and</b> on the text having come from a
    /// release name. That conjunction is why the space is declared provenance-sensitive rather than simply
    /// non-canonical.
    /// </remarks>
    public bool UsesAliasNumbering { get; init; }
}

/// <summary>
/// One unit as this extension's catalog holds it, carrying every coordinate it knows.
/// </summary>
/// <remarks>
/// Six numbering members, three of them nullable and one a confidence flag. That is the surveyed row
/// reproduced faithfully — and it is exactly why the shape model exposes a bag of readings rather than a
/// fixed pair of columns: a schema with a column per scheme cannot be extended by an extension, and this
/// extension needs five.
/// </remarks>
public sealed record TvEpisodeRecord
{
    /// <summary>Catalog-local surrogate identifier.</summary>
    public required int Id { get; init; }

    /// <summary>The library entry this unit belongs to.</summary>
    public required int SeriesId { get; init; }

    /// <summary>Canonical outer ordinal.</summary>
    public required int SeasonNumber { get; init; }

    /// <summary>Canonical inner ordinal.</summary>
    public required int EpisodeNumber { get; init; }

    /// <summary>Flat ordinal over the whole run, when the catalog assigns one.</summary>
    public int? AbsoluteNumber { get; init; }

    /// <summary>Alias-space outer ordinal.</summary>
    public int? AliasSeasonNumber { get; init; }

    /// <summary>Alias-space inner ordinal.</summary>
    public int? AliasEpisodeNumber { get; init; }

    /// <summary>Alias-space flat ordinal.</summary>
    public int? AliasAbsoluteNumber { get; init; }

    /// <summary>
    /// True when the alias reading was extrapolated rather than mapped, and is therefore not trustworthy.
    /// </summary>
    public bool AliasIsUnverified { get; init; }

    /// <summary>Display title.</summary>
    public required string Title { get; init; }

    /// <summary>Synopsis.</summary>
    public string? Overview { get; init; }

    /// <summary>Date of first transmission.</summary>
    public DateOnly? AirDate { get; init; }

    /// <summary>Duration in minutes.</summary>
    public int RuntimeMinutes { get; init; }

    /// <summary>Still image address.</summary>
    public Uri? Still { get; init; }

    /// <summary>True when the outer ordinal is a declared sequence exception.</summary>
    public bool IsOutOfRun => SeasonNumber == TvIds.SpecialsOrdinal;
}

/// <summary>
/// The extension's in-memory catalog and every lookup the resolution paths need over it.
/// </summary>
public sealed class TvCatalog
{
    private readonly IReadOnlyList<TvSeriesRecord> _series;
    private readonly IReadOnlyList<TvEpisodeRecord> _episodes;
    private readonly Dictionary<long, TvSeriesRecord> _seriesById;
    private readonly Dictionary<int, TvSeriesRecord> _seriesByTvdb;
    private readonly Dictionary<string, TvSeriesRecord> _seriesByImdb;
    private readonly Dictionary<string, List<TvSeriesRecord>> _seriesByNormalizedTitle;
    private readonly Dictionary<long, TvEpisodeRecord> _episodesById;
    private readonly Dictionary<long, List<TvEpisodeRecord>> _episodesBySeries;
    private readonly Dictionary<(int Series, int Season, int Episode), TvEpisodeRecord> _byAired;
    private readonly Dictionary<(int Series, int Absolute), TvEpisodeRecord> _byAbsolute;
    private readonly Dictionary<(int Series, DateOnly Date), List<TvEpisodeRecord>> _byAirDate;
    private readonly Dictionary<(int Series, int Season, int Episode), TvEpisodeRecord> _byAlias;
    private readonly Dictionary<(int Series, int Absolute), TvEpisodeRecord> _byAliasAbsolute;

    private TvCatalog(IReadOnlyList<TvSeriesRecord> series, IReadOnlyList<TvEpisodeRecord> episodes)
    {
        _series = series;
        _episodes = episodes;
        _seriesById = series.ToDictionary(entry => (long)entry.Id);
        _seriesByTvdb = series.ToDictionary(entry => entry.TvdbId);
        _seriesByImdb = series
            .Where(entry => !string.IsNullOrEmpty(entry.ImdbId))
            .ToDictionary(entry => entry.ImdbId!, StringComparer.OrdinalIgnoreCase);
        _seriesByNormalizedTitle = [];
        _episodesById = episodes.ToDictionary(episode => (long)episode.Id);
        _episodesBySeries = [];
        _byAired = [];
        _byAbsolute = [];
        _byAirDate = [];
        _byAlias = [];
        _byAliasAbsolute = [];

        foreach (var entry in series)
        {
            IndexTitle(entry.Title, entry);

            foreach (var alternative in entry.AlternativeTitles)
            {
                IndexTitle(alternative, entry);
            }
        }

        foreach (var episode in episodes)
        {
            if (!_episodesBySeries.TryGetValue(episode.SeriesId, out var bucket))
            {
                bucket = [];
                _episodesBySeries[episode.SeriesId] = bucket;
            }

            bucket.Add(episode);

            _byAired[(episode.SeriesId, episode.SeasonNumber, episode.EpisodeNumber)] = episode;

            if (episode.AbsoluteNumber is { } absolute)
            {
                _byAbsolute[(episode.SeriesId, absolute)] = episode;
            }

            if (episode.AirDate is { } airDate)
            {
                if (!_byAirDate.TryGetValue((episode.SeriesId, airDate), out var dated))
                {
                    dated = [];
                    _byAirDate[(episode.SeriesId, airDate)] = dated;
                }

                dated.Add(episode);
            }

            if (episode.AliasSeasonNumber is { } aliasSeason && episode.AliasEpisodeNumber is { } aliasEpisode)
            {
                _byAlias[(episode.SeriesId, aliasSeason, aliasEpisode)] = episode;
            }

            if (episode.AliasAbsoluteNumber is { } aliasAbsolute)
            {
                _byAliasAbsolute[(episode.SeriesId, aliasAbsolute)] = episode;
            }
        }

        foreach (var bucket in _episodesBySeries.Values)
        {
            bucket.Sort(static (left, right) => left.SeasonNumber != right.SeasonNumber
                ? left.SeasonNumber.CompareTo(right.SeasonNumber)
                : left.EpisodeNumber.CompareTo(right.EpisodeNumber));
        }
    }

    /// <summary>Every library entry.</summary>
    public IReadOnlyList<TvSeriesRecord> Series => _series;

    /// <summary>Every unit.</summary>
    public IReadOnlyList<TvEpisodeRecord> Episodes => _episodes;

    /// <summary>Builds the seeded catalog.</summary>
    /// <returns>
    /// A catalog holding one entry per addressing scheme, so all three resolution paths and the
    /// provenance-sensitive alias space are reachable without a metadata pipeline.
    /// </returns>
    public static TvCatalog CreateSeeded()
    {
        var series = new List<TvSeriesRecord>
        {
            new()
            {
                Id = 1,
                TvdbId = 280_619,
                ImdbId = "tt3230854",
                Title = "The Expanse",
                SortTitle = "expanse",
                AlternativeTitles = ["Expanse"],
                Year = 2015,
                Status = TvStatuses.Ended,
                AddressingScheme = TvAddressingSchemes.Ordinal,
                Network = "Syfy",
                Overview = "A detective, a ship's officer and a diplomat pull on the same thread.",
                RuntimeMinutes = 45,
                Certification = "TV-14",
                Genres = ["Science Fiction", "Drama", "Mystery"],
                Poster = new Uri("https://image.example/series/280619.jpg"),
                FirstAired = new DateOnly(2015, 12, 14)
            },
            new()
            {
                Id = 2,
                TvdbId = 71_256,
                ImdbId = "tt0115147",
                Title = "The Nightly Review",
                SortTitle = "nightly review",
                AlternativeTitles = ["Nightly Review"],
                Year = 1996,
                Status = TvStatuses.Continuing,

                // Addressed by the calendar space. The canonical ordinal pair still exists - the catalog
                // assigns one - but no release title will ever carry it.
                AddressingScheme = TvAddressingSchemes.Calendar,
                Network = "Comedy Central",
                Overview = "A nightly program addressed by the date it went out.",
                RuntimeMinutes = 22,
                Certification = "TV-14",
                Genres = ["Comedy", "Talk Show"],
                Poster = new Uri("https://image.example/series/71256.jpg"),
                FirstAired = new DateOnly(1996, 7, 21)
            },
            new()
            {
                Id = 3,
                TvdbId = 76_885,
                ImdbId = "tt0213338",
                Title = "Cowboy Bebop",
                SortTitle = "cowboy bebop",
                AlternativeTitles = ["Kauboi Bibappu"],
                Year = 1998,
                Status = TvStatuses.Ended,

                // Addressed by the flat ordinal, and the release community maintains its own mapping on top
                // of the catalog's - which is what makes the alias space both non-canonical and only
                // partially trustworthy.
                AddressingScheme = TvAddressingSchemes.Flat,
                Network = "TV Tokyo",
                Overview = "Bounty hunters, a dog, and a great deal of unpaid rent.",
                RuntimeMinutes = 24,
                Certification = "TV-14",
                Genres = ["Animation", "Science Fiction", "Action"],
                Poster = new Uri("https://image.example/series/76885.jpg"),
                FirstAired = new DateOnly(1998, 4, 3),
                UsesAliasNumbering = true
            }
        };

        var episodes = new List<TvEpisodeRecord>();
        episodes.AddRange(BuildExpanse());
        episodes.AddRange(BuildNightlyReview());
        episodes.AddRange(BuildCowboyBebop());

        return new TvCatalog(series, episodes);
    }

    /// <summary>Builds a reference the host can carry for a library entry.</summary>
    /// <param name="series">The entry.</param>
    /// <returns>The reference.</returns>
    public static MediaItemRef ReferenceTo(TvSeriesRecord series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return new MediaItemRef(TvIds.MediaKind, TvIds.SeriesLevel, MediaItemId.FromInt64(series.Id));
    }

    /// <summary>Builds a reference the host can carry for a unit.</summary>
    /// <param name="episode">The unit.</param>
    /// <returns>The reference.</returns>
    public static MediaItemRef ReferenceTo(TvEpisodeRecord episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return new MediaItemRef(TvIds.MediaKind, TvIds.EpisodeLevel, MediaItemId.FromInt64(episode.Id));
    }

    /// <summary>
    /// Builds the full bag of coordinate readings a unit carries.
    /// </summary>
    /// <param name="episode">The unit.</param>
    /// <returns>Every reading the catalog knows, each with its own confidence.</returns>
    /// <remarks>
    /// One unit, up to five readings. The canonical one is verified because it came from the catalog; the
    /// flat one is derived from it; the alias ones are verified when mapped and unverified when
    /// extrapolated. The confidence is a property of the reading, not of the space.
    /// </remarks>
    public static CoordinateSet CoordinatesOf(TvEpisodeRecord episode)
    {
        ArgumentNullException.ThrowIfNull(episode);

        var readings = new List<CoordinateReading>(5)
        {
            new(
                TvIds.AiredSpaceId,
                Coordinate.OfOrdinals(OrdinalPath.Of(episode.SeasonNumber, episode.EpisodeNumber)),
                CoordinateConfidence.Verified)
        };

        if (episode.AbsoluteNumber is { } absolute)
        {
            readings.Add(new CoordinateReading(
                TvIds.AbsoluteSpaceId,
                Coordinate.OfOrdinals(OrdinalPath.Of(absolute)),
                CoordinateConfidence.Verified));
        }

        if (episode.AirDate is { } airDate)
        {
            readings.Add(new CoordinateReading(
                TvIds.AirDateSpaceId,
                Coordinate.OfDate(airDate),
                CoordinateConfidence.Verified));
        }

        var aliasConfidence = episode.AliasIsUnverified
            ? CoordinateConfidence.Unverified
            : CoordinateConfidence.Derived;

        if (episode.AliasSeasonNumber is { } aliasSeason && episode.AliasEpisodeNumber is { } aliasEpisode)
        {
            readings.Add(new CoordinateReading(
                TvIds.SceneSpaceId,
                Coordinate.OfOrdinals(OrdinalPath.Of(aliasSeason, aliasEpisode)),
                aliasConfidence));
        }

        if (episode.AliasAbsoluteNumber is { } aliasAbsolute)
        {
            readings.Add(new CoordinateReading(
                TvIds.SceneAbsoluteSpaceId,
                Coordinate.OfOrdinals(OrdinalPath.Of(aliasAbsolute)),
                aliasConfidence));
        }

        return CoordinateSet.Of([.. readings]);
    }

    /// <summary>Looks a library entry up by its catalog identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="series">The entry, when one exists.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool TryGetSeries(long id, out TvSeriesRecord? series) => _seriesById.TryGetValue(id, out series);

    /// <summary>Looks a unit up by its catalog identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="episode">The unit, when one exists.</param>
    /// <returns><see langword="true"/> when the unit exists.</returns>
    public bool TryGetEpisode(long id, out TvEpisodeRecord? episode)
        => _episodesById.TryGetValue(id, out episode);

    /// <summary>Looks a library entry up by an external identifier.</summary>
    /// <param name="externalId">A <c>tvdb</c> or <c>imdb</c> identifier.</param>
    /// <param name="series">The entry, when one matches.</param>
    /// <returns><see langword="true"/> when the identifier resolves.</returns>
    public bool TryGetSeriesByExternalId(ExternalId externalId, out TvSeriesRecord? series)
    {
        series = null;

        if (string.Equals(externalId.Scheme, TvIds.TvdbScheme, StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(externalId.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tvdbId)
                && _seriesByTvdb.TryGetValue(tvdbId, out series);
        }

        return string.Equals(externalId.Scheme, TvIds.ImdbScheme, StringComparison.OrdinalIgnoreCase)
            && _seriesByImdb.TryGetValue(externalId.Value, out series);
    }

    /// <summary>Every library entry whose title or alternative titles normalize to the given text.</summary>
    /// <param name="title">A raw title, typically taken from a release name.</param>
    /// <returns>The matching entries, possibly empty.</returns>
    public IReadOnlyList<TvSeriesRecord> FindSeriesByTitle(string title)
    {
        var normalized = TvTitleNormalizer.Normalize(title);

        return normalized.Length != 0 && _seriesByNormalizedTitle.TryGetValue(normalized, out var matches)
            ? matches
            : [];
    }

    /// <summary>Every unit under one library entry, ordered by canonical coordinate.</summary>
    /// <param name="seriesId">The entry identifier.</param>
    /// <returns>The units.</returns>
    public IReadOnlyList<TvEpisodeRecord> EpisodesOf(long seriesId)
        => _episodesBySeries.TryGetValue(seriesId, out var bucket) ? bucket : [];

    /// <summary>Every unit under one library entry with the given outer ordinal.</summary>
    /// <param name="seriesId">The entry identifier.</param>
    /// <param name="season">The outer ordinal.</param>
    /// <returns>The units, ordered by inner ordinal.</returns>
    public IReadOnlyList<TvEpisodeRecord> EpisodesInSeason(int seriesId, int season)
        => [.. EpisodesOf(seriesId).Where(episode => episode.SeasonNumber == season)];

    /// <summary>Resolves a canonical ordinal pair.</summary>
    /// <param name="seriesId">The entry identifier.</param>
    /// <param name="season">The outer ordinal.</param>
    /// <param name="episode">The inner ordinal.</param>
    /// <param name="found">The unit, when one exists.</param>
    /// <returns><see langword="true"/> when the coordinate resolves.</returns>
    public bool TryGetByAired(int seriesId, int season, int episode, out TvEpisodeRecord? found)
        => _byAired.TryGetValue((seriesId, season, episode), out found);

    /// <summary>Resolves a flat ordinal.</summary>
    /// <param name="seriesId">The entry identifier.</param>
    /// <param name="absolute">The flat ordinal.</param>
    /// <param name="found">The unit, when one exists.</param>
    /// <returns><see langword="true"/> when the coordinate resolves.</returns>
    public bool TryGetByAbsolute(int seriesId, int absolute, out TvEpisodeRecord? found)
        => _byAbsolute.TryGetValue((seriesId, absolute), out found);

    /// <summary>Resolves a calendar coordinate.</summary>
    /// <param name="seriesId">The entry identifier.</param>
    /// <param name="date">The transmission date.</param>
    /// <returns>Every unit transmitted on that date. More than one is legal and does happen.</returns>
    public IReadOnlyList<TvEpisodeRecord> FindByAirDate(int seriesId, DateOnly date)
        => _byAirDate.TryGetValue((seriesId, date), out var dated) ? dated : [];

    /// <summary>Resolves an alias-space ordinal pair.</summary>
    /// <param name="seriesId">The entry identifier.</param>
    /// <param name="season">The alias outer ordinal.</param>
    /// <param name="episode">The alias inner ordinal.</param>
    /// <param name="found">The unit, when one exists.</param>
    /// <returns><see langword="true"/> when the coordinate resolves.</returns>
    public bool TryGetByAlias(int seriesId, int season, int episode, out TvEpisodeRecord? found)
        => _byAlias.TryGetValue((seriesId, season, episode), out found);

    /// <summary>Resolves an alias-space flat ordinal.</summary>
    /// <param name="seriesId">The entry identifier.</param>
    /// <param name="absolute">The alias flat ordinal.</param>
    /// <param name="found">The unit, when one exists.</param>
    /// <returns><see langword="true"/> when the coordinate resolves.</returns>
    public bool TryGetByAliasAbsolute(int seriesId, int absolute, out TvEpisodeRecord? found)
        => _byAliasAbsolute.TryGetValue((seriesId, absolute), out found);

    private static IEnumerable<TvEpisodeRecord> BuildExpanse()
    {
        (int Season, int Episode, string Title, int Month, int Day, int Year)[] rows =
        [
            (1, 1, "Dulcinea", 12, 14, 2015),
            (1, 2, "The Big Empty", 12, 15, 2015),
            (1, 3, "Remember the Cant", 12, 22, 2015),
            (1, 4, "CQB", 12, 29, 2015),
            (1, 5, "Back to the Butcher", 1, 5, 2016),
            (1, 6, "Rock Bottom", 1, 12, 2016),
            (2, 1, "Safe", 2, 1, 2017),
            (2, 2, "Doors & Corners", 2, 1, 2017),
            (2, 3, "Static", 2, 8, 2017)
        ];

        var id = 1001;
        var absolute = 1;

        foreach (var (season, episode, title, month, day, year) in rows)
        {
            yield return new TvEpisodeRecord
            {
                Id = id++,
                SeriesId = 1,
                SeasonNumber = season,
                EpisodeNumber = episode,
                AbsoluteNumber = absolute++,
                Title = title,
                AirDate = new DateOnly(year, month, day),
                RuntimeMinutes = 45,
                Overview = "A unit of an entry addressed by the canonical ordinal pair."
            };
        }

        // The reserved out-of-run value. It has a coordinate, it is addressable, and it must not be counted
        // as missing - which is exactly what the declared sequence exception says.
        yield return new TvEpisodeRecord
        {
            Id = 1100,
            SeriesId = 1,
            SeasonNumber = (int)TvIds.SpecialsOrdinal,
            EpisodeNumber = 1,
            Title = "Behind the Scenes",
            AirDate = new DateOnly(2016, 2, 2),
            RuntimeMinutes = 12,
            Overview = "Outside the numbered run, and deliberately excluded from completeness."
        };
    }

    private static IEnumerable<TvEpisodeRecord> BuildNightlyReview()
    {
        (int Episode, string Title, int Year, int Month, int Day)[] rows =
        [
            (1, "Monday", 2024, 1, 15),
            (2, "Tuesday", 2024, 1, 16),
            (3, "Wednesday", 2024, 1, 17),
            (4, "Thursday", 2024, 1, 18)
        ];

        var id = 2001;

        foreach (var (episode, title, year, month, day) in rows)
        {
            // The canonical pair still exists - the catalog numbers the run by broadcast year - but every
            // release will address these by date, which is precisely the case a single addressing scheme
            // per media kind cannot represent.
            yield return new TvEpisodeRecord
            {
                Id = id++,
                SeriesId = 2,
                SeasonNumber = 2024,
                EpisodeNumber = episode,
                Title = title,
                AirDate = new DateOnly(year, month, day),
                RuntimeMinutes = 22,
                Overview = "A unit of an entry addressed by the calendar space."
            };
        }
    }

    private static IEnumerable<TvEpisodeRecord> BuildCowboyBebop()
    {
        (int Episode, string Title, int Month, int Day)[] rows =
        [
            (1, "Asteroid Blues", 4, 3),
            (2, "Stray Dog Strut", 4, 10),
            (3, "Honky Tonk Women", 4, 17),
            (4, "Gateway Shuffle", 4, 24),
            (5, "Ballad of Fallen Angels", 5, 1),
            (6, "Sympathy for the Devil", 5, 8)
        ];

        var id = 3001;

        foreach (var (episode, title, month, day) in rows)
        {
            // The first four are mapped: the release community's flat ordinal is offset by one from the
            // catalog's because the community counts a prologue the catalog does not. The last two are
            // extrapolated from that offset and flagged as such - the mapping is genuinely partial, and the
            // system tracking its own confidence is the behavior the coordinate model has to preserve.
            var mapped = episode <= 4;

            yield return new TvEpisodeRecord
            {
                Id = id++,
                SeriesId = 3,
                SeasonNumber = 1,
                EpisodeNumber = episode,
                AbsoluteNumber = episode,
                AliasSeasonNumber = 1,
                AliasEpisodeNumber = episode + 1,
                AliasAbsoluteNumber = episode + 1,
                AliasIsUnverified = !mapped,
                Title = title,
                AirDate = new DateOnly(1998, month, day),
                RuntimeMinutes = 24,
                Overview = "A unit of an entry addressed by the flat ordinal."
            };
        }
    }

    private void IndexTitle(string? title, TvSeriesRecord series)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var normalized = TvTitleNormalizer.Normalize(title);

        if (normalized.Length == 0)
        {
            return;
        }

        if (!_seriesByNormalizedTitle.TryGetValue(normalized, out var bucket))
        {
            bucket = [];
            _seriesByNormalizedTitle[normalized] = bucket;
        }

        if (!bucket.Contains(series))
        {
            bucket.Add(series);
        }
    }
}
