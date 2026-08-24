using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Media.Movies;
using Arronix.Provider.Tmdb.Identity;
using Arronix.Provider.Tmdb.Settings;
using Arronix.Provider.Tmdb.Transport;

namespace Arronix.Provider.Tmdb.Movies;

/// <summary>
/// Maps a TMDb movie-details payload onto the movie domain's exact typed shape.
/// </summary>
/// <remarks>
/// <para>
/// Everything a <c>Movie</c> needs other than its host-minted <see cref="MediaItemId"/> is produced here,
/// and is fully exercised by tests: the mapping is not the reason this provider stops short of a complete
/// <c>ICataloger&lt;Movie&gt;</c>/<c>ICurator&lt;Movie&gt;</c> implementation. <see cref="TmdbMovieCataloger"/>
/// and <see cref="TmdbMovieCurator"/> hold the one open question — what durable key to assign a movie TMDb
/// has never been asked about before — and document it at <see cref="MovieMaterializationBoundary"/> rather
/// than calling this method with a fabricated one.
/// </para>
/// <para>
/// Release-date rows are read from whichever country in TMDb's <c>release_dates</c> block matches the
/// configured region; a movie with no matching-region rows carries an empty lifecycle rather than a
/// misattributed one borrowed from another territory.
/// </para>
/// </remarks>
internal static class TmdbMovieMapper
{
    private const int TheatricalLimited = 2;
    private const int Theatrical = 3;
    private const int Digital = 4;
    private const int Physical = 5;

    /// <summary>Maps a TMDb movie-details payload to the exact typed <c>Movie</c> shape.</summary>
    /// <param name="key">The durable identity to assign. Never minted by this mapper.</param>
    /// <param name="dto">The TMDb payload.</param>
    /// <param name="settings">The configured definition's settings.</param>
    /// <param name="evaluatedOn">The date the lifecycle stage is evaluated against.</param>
    /// <returns>The fully shaped movie.</returns>
    public static Movie ToMovie(MediaItemId key, TmdbMovieDetailsDto dto, TmdbSettingsValues settings, DateOnly evaluatedOn)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(settings);

        var title = string.IsNullOrWhiteSpace(dto.Title) ? dto.OriginalTitle : dto.Title;

        return new Movie
        {
            Key = key,
            ExternalIds = MapExternalIds(dto),
            Title = string.IsNullOrWhiteSpace(title) ? $"TMDb {dto.Id}" : title,
            OriginalTitle = string.IsNullOrWhiteSpace(dto.OriginalTitle) ? null : dto.OriginalTitle,
            OriginalLanguage = MapLanguage(dto.OriginalLanguage),
            Year = MapYear(dto.ReleaseDate),
            Lifecycle = MapLifecycle(dto, settings.Region, evaluatedOn),
            Overview = string.IsNullOrWhiteSpace(dto.Overview) ? null : dto.Overview,
            Runtime = dto.Runtime is > 0 ? TimeSpan.FromMinutes(dto.Runtime.Value) : null,
            Organization = dto.ProductionCompanies?.FirstOrDefault(company => !string.IsNullOrWhiteSpace(company.Name))?.Name,
            Certification = MapCertification(dto, settings.Region),
            Genres = dto.Genres?.Where(genre => !string.IsNullOrWhiteSpace(genre.Name)).Select(genre => genre.Name!).ToArray()
                ?? [],
            Website = TryParseUri(dto.Homepage),
            Artwork = MapArtwork(dto, settings.ImageBaseUrl),
            Popularity = dto.Popularity,
            Ratings = MapRatings(dto),
        };
    }

    private static ExternalIdSet MapExternalIds(TmdbMovieDetailsDto dto)
    {
        var ids = new List<ExternalId> { ExternalId.Of(TmdbIdentity.Scheme, dto.Id) };

        if (!string.IsNullOrWhiteSpace(dto.ExternalIds?.ImdbId))
        {
            ids.Add(ExternalId.Of("imdb", dto.ExternalIds.ImdbId));
        }

        return ExternalIdSet.From(ids);
    }

    private static Language? MapLanguage(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : new Language(code, code);

    private static int? MapYear(string? releaseDate) =>
        TryParseDate(releaseDate, out var date) ? date.Year : null;

    private static MovieReleaseTimeline MapLifecycle(TmdbMovieDetailsDto dto, string region, DateOnly evaluatedOn)
    {
        var country = dto.ReleaseDates?.Results?
            .FirstOrDefault(entry => string.Equals(entry.CountryCode, region, StringComparison.OrdinalIgnoreCase));

        DateOnly? inCinemas = null;
        DateOnly? physical = null;
        DateOnly? digital = null;

        foreach (var entry in country?.ReleaseDates ?? [])
        {
            if (!TryParseDate(entry.ReleaseDate, out var date))
            {
                continue;
            }

            switch (entry.Type)
            {
                case TheatricalLimited or Theatrical:
                    inCinemas = Earliest(inCinemas, date);
                    break;
                case Digital:
                    digital = Earliest(digital, date);
                    break;
                case Physical:
                    physical = Earliest(physical, date);
                    break;
            }
        }

        if (inCinemas is null && physical is null && digital is null && TryParseDate(dto.ReleaseDate, out var primary))
        {
            inCinemas = primary;
        }

        return new MovieReleaseTimeline
        {
            InCinemas = inCinemas,
            Physical = physical,
            Digital = digital,
            EvaluatedOn = evaluatedOn,
        };
    }

    private static ContentCertification? MapCertification(TmdbMovieDetailsDto dto, string region)
    {
        var country = dto.ReleaseDates?.Results?
            .FirstOrDefault(entry => string.Equals(entry.CountryCode, region, StringComparison.OrdinalIgnoreCase));

        var certification = country?.ReleaseDates?
            .Select(entry => entry.Certification)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return string.IsNullOrWhiteSpace(certification)
            ? null
            : new ContentCertification(region, "TMDb", certification);
    }

    private static ArtworkSet MapArtwork(TmdbMovieDetailsDto dto, Uri imageBaseUrl)
    {
        var images = new List<ArtworkImage>();

        if (dto.PosterPath is { Length: > 0 } poster)
        {
            images.Add(new ArtworkImage("poster", CombineImageUri(imageBaseUrl, poster)));
        }

        if (dto.BackdropPath is { Length: > 0 } backdrop)
        {
            images.Add(new ArtworkImage("fanart", CombineImageUri(imageBaseUrl, backdrop)));
        }

        return ArtworkSet.From(images);
    }

    private static Uri CombineImageUri(Uri imageBaseUrl, string relativePath) =>
        new(imageBaseUrl, relativePath.TrimStart('/'));

    private static IReadOnlyList<Rating> MapRatings(TmdbMovieDetailsDto dto) =>
        dto.VoteAverage is { } average && RatingScale.OutOfTen.Contains((decimal)average)
            ? [new Rating("tmdb", (decimal)average, RatingScale.OutOfTen, RatingVoice.Audience, dto.VoteCount)]
            : [];

    private static bool TryParseDate(string? text, out DateOnly date)
    {
        // TMDb spells a bare date ("1999-03-30") at top level but a full timestamp
        // ("1999-03-31T00:00:00.000Z") inside release_dates entries; both are accepted.
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var timestamp))
        {
            date = DateOnly.FromDateTime(timestamp);
            return true;
        }

        date = default;
        return false;
    }

    private static DateOnly Earliest(DateOnly? current, DateOnly candidate) =>
        current is { } value && value < candidate ? value : candidate;

    private static Uri? TryParseUri(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;
}
