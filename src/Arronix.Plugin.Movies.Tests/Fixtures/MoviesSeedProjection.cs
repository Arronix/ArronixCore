#pragma warning disable ARX0013 // Shape contracts are experimental; these fixtures exercise the typed surface.
#pragma warning disable ARX0020 // Media contracts are experimental; these fixtures exercise the typed surface.

using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;

namespace Arronix.Plugin.Movies.Tests.Fixtures;

/// <summary>
/// Turns a seeded catalog record into the typed entity the kind is declared as.
/// </summary>
/// <remarks>
/// <para>
/// This exists to make one claim checkable: that everything the catalog actually holds has a home on
/// <see cref="Movie"/>. Under the string surface the same mapping was a table of paths into field
/// identifiers that nothing resolved until load; here it is ordinary code the compiler checks, which is
/// exactly what the mapping becomes for real when the cataloger is a plugin that may reference this
/// assembly.
/// </para>
/// <para>
/// What it also makes visible is the four places the catalog record carries something the entity
/// deliberately does not: a sort title and a clean title, which are host transforms of the title; a
/// last-sync timestamp, which is host bookkeeping about every kind's every item; and a bare video-host
/// identifier, which the entity carries as a whole address instead.
/// </para>
/// </remarks>
internal static class MoviesSeedProjection
{
    /// <summary>The regulator whose rating the seeded records carry.</summary>
    private const string CertificationRegion = "US";

    /// <summary>Projects one seeded record onto the typed entity.</summary>
    /// <param name="record">The catalog record.</param>
    /// <param name="collection">The collection it belongs to, when the catalog holds one.</param>
    /// <returns>The entity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is <see langword="null"/>.</exception>
    internal static Movie ToMovie(MovieRecord record, MovieCollectionRecord? collection = null) =>
        record is null
            ? throw new ArgumentNullException(nameof(record))
            : new Movie
            {
                Key = MediaItemId.FromInt64(record.Id),
                ExternalIds = ExternalIdSet.From(IdentifiersOf(record)),
                Title = record.Title,
                OriginalTitle = record.OriginalTitle,
                OriginalLanguage = record.OriginalLanguage,
                AlternateTitles = record.AlternativeTitles,
                Translations =
                [
                    .. record.Translations.Select(static row =>
                        new Translation(row.Language, row.Title, row.Overview)),
                ],
                Year = record.Year,
                SecondaryYear = record.SecondaryYear,
                InCinemas = record.InCinemas,
                PhysicalRelease = record.PhysicalRelease,
                DigitalRelease = record.DigitalRelease,
                ReleaseDate = record.ReleaseDate,
                Status = record.Status,
                Overview = record.Overview,
                Runtime = record.RuntimeMinutes > 0 ? TimeSpan.FromMinutes(record.RuntimeMinutes) : null,
                Studio = record.Studio,
                Certification = record.Certification is { } rating
                    ? new Certification(CertificationRegion, rating)
                    : null,
                Genres = record.Genres,
                Keywords = record.Keywords,
                Website = record.Website,
                Trailer = record.YouTubeTrailerId is { Length: > 0 } video
                    ? new Uri("https://www.youtube.com/watch?v=" + video)
                    : null,
                Images = ArtworkSet.From(ArtworkOf(record)),
                Popularity = record.Popularity,
                Ratings = [.. RatingsOf(record.Ratings)],
                Collection = collection is null ? null : ToCollection(collection),
            };

    /// <summary>Projects one seeded collection record onto the typed group.</summary>
    /// <param name="record">The collection record.</param>
    /// <returns>The group.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is <see langword="null"/>.</exception>
    internal static Collection ToCollection(MovieCollectionRecord record) =>
        record is null
            ? throw new ArgumentNullException(nameof(record))
            : new Collection
            {
                Key = MediaItemId.FromInt64(record.TmdbId),
                Title = record.Title,
                Overview = record.Overview,
                Images = ArtworkSet.From(CollectionArtworkOf(record)),
                MemberCount = MoviesSeedData.Movies.Count(movie => movie.CollectionTmdbId == record.TmdbId),
                ExternalIds = ExternalIdSet.Of(
                    new Abstractions.Shape.ExternalId(
                        "tmdb-collection",
                        record.TmdbId.ToString(CultureInfo.InvariantCulture))),
            };

    private static IEnumerable<Abstractions.Shape.ExternalId> IdentifiersOf(MovieRecord record)
    {
        yield return new Abstractions.Shape.ExternalId(
            "tmdb",
            record.TmdbId.ToString(CultureInfo.InvariantCulture));

        if (record.ImdbId is { Length: > 0 } imdb)
        {
            yield return new Abstractions.Shape.ExternalId("imdb", imdb);
        }
    }

    private static IEnumerable<ArtworkImage> ArtworkOf(MovieRecord record)
    {
        if (record.Poster is { } poster)
        {
            yield return new ArtworkImage("poster", poster);
        }

        if (record.Fanart is { } fanart)
        {
            yield return new ArtworkImage("fanart", fanart);
        }

        if (record.Banner is { } banner)
        {
            yield return new ArtworkImage("banner", banner);
        }

        if (record.ClearLogo is { } logo)
        {
            yield return new ArtworkImage("clearLogo", logo);
        }
    }

    private static IEnumerable<ArtworkImage> CollectionArtworkOf(MovieCollectionRecord record)
    {
        if (record.Poster is { } poster)
        {
            yield return new ArtworkImage("poster", poster);
        }

        if (record.Fanart is { } fanart)
        {
            yield return new ArtworkImage("fanart", fanart);
        }
    }

    /// <summary>
    /// Five vendor-named pairs become five elements of one list, and the two facts the vendor names used to
    /// encode — the interval and whose voice it is — become properties a consumer can read.
    /// </summary>
    private static IEnumerable<Rating> RatingsOf(MovieRatings ratings)
    {
        if (ratings.Tmdb is { } tmdb)
        {
            yield return new Rating("tmdb", tmdb, RatingScale.OutOfTen, RatingVoice.Audience, ratings.TmdbVotes);
        }

        if (ratings.Imdb is { } imdb)
        {
            yield return new Rating("imdb", imdb, RatingScale.OutOfTen, RatingVoice.Audience, ratings.ImdbVotes);
        }

        if (ratings.Trakt is { } trakt)
        {
            yield return new Rating("trakt", trakt, RatingScale.OutOfTen, RatingVoice.Audience);
        }

        if (ratings.RottenTomatoes is { } tomatoes)
        {
            yield return new Rating("rottenTomatoes", tomatoes, RatingScale.Percent, RatingVoice.Critic);
        }

        if (ratings.Metacritic is { } metacritic)
        {
            yield return new Rating("metacritic", metacritic, RatingScale.Percent, RatingVoice.Critic);
        }
    }
}
