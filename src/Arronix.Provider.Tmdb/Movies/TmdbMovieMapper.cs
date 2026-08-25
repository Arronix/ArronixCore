using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Media.Movies;
using Arronix.Provider.Tmdb.Identity;
using Arronix.Provider.Tmdb.Settings;
using Arronix.Provider.Tmdb.Transport;

namespace Arronix.Provider.Tmdb.Movies;

/// <summary>Maps TMDb movie payloads onto the typed movie domain.</summary>
/// <remarks>
/// Certification remains unset because TMDb is not the issuing authority. Original language remains unset
/// until a language resolver can turn TMDb's code into the domain's complete <c>Language</c> value. Regional
/// lifecycle dates come only from the configured country's release-date rows; the top-level date supplies
/// the year only. Movie identifiers and artwork paths are validated before they enter the domain model.
/// </remarks>
internal static partial class TmdbMovieMapper
{
    private const int TheatricalLimited = 2;
    private const int Theatrical = 3;
    private const int Digital = 4;
    private const int Physical = 5;

    /// <summary>Maps a TMDb movie-details payload to the movie domain's typed shape.</summary>
    /// <param name="dto">The TMDb payload.</param>
    /// <param name="settings">The configured definition's settings.</param>
    /// <param name="evaluatedOn">The date the lifecycle stage is evaluated against.</param>
    /// <returns>The mapped movie.</returns>
    /// <exception cref="TmdbResponseFormatException">
    /// The payload has no usable title, a malformed identifier, or a malformed artwork path.
    /// </exception>
    public static Movie ToMovie(TmdbMovieDetailsDto dto, TmdbSettingsValues settings, DateOnly evaluatedOn)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(settings);

        if (!TmdbIdentity.IsCanonicalId(dto.Id))
        {
            throw new TmdbResponseFormatException($"movie/{dto.Id}", "had a malformed id.");
        }

        var title = string.IsNullOrWhiteSpace(dto.Title) ? dto.OriginalTitle : dto.Title;

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new TmdbResponseFormatException($"movie/{dto.Id}", "had neither a title nor an original_title.");
        }

        return new Movie
        {
            ExternalIds = MapExternalIds(dto),
            Title = title,
            OriginalTitle = string.IsNullOrWhiteSpace(dto.OriginalTitle) ? null : dto.OriginalTitle,
            Year = MapYear(dto.ReleaseDate),
            Lifecycle = MapLifecycle(dto, settings.Region, evaluatedOn),
            Overview = string.IsNullOrWhiteSpace(dto.Overview) ? null : dto.Overview,
            Runtime = dto.Runtime is > 0 ? TimeSpan.FromMinutes(dto.Runtime.Value) : null,
            Organization = dto.ProductionCompanies?.FirstOrDefault(company => !string.IsNullOrWhiteSpace(company.Name))?.Name,
            Genres = dto.Genres?.Where(genre => !string.IsNullOrWhiteSpace(genre.Name)).Select(genre => genre.Name!).ToArray()
                ?? [],
            Website = TryParseWebUri(dto.Homepage),
            Artwork = MapArtwork(dto, settings.ImageBaseUrl),
            Popularity = dto.Popularity,
            Ratings = MapRatings(dto),
        };
    }

    /// <summary>Maps the facts available on one TMDb search result onto a valid movie value.</summary>
    public static Movie ToMovie(TmdbMovieSummaryDto dto, TmdbSettingsValues settings, DateOnly evaluatedOn)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(settings);

        if (!TmdbIdentity.IsCanonicalId(dto.Id))
        {
            throw new TmdbResponseFormatException("search/movie", "contained a malformed movie id.");
        }

        var title = string.IsNullOrWhiteSpace(dto.Title) ? dto.OriginalTitle : dto.Title;

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new TmdbResponseFormatException(
                "search/movie", $"contained movie {dto.Id} with neither a title nor an original_title.");
        }

        return new Movie
        {
            ExternalIds = ExternalIdSet.From([ExternalId.Of(TmdbIdentity.Scheme, dto.Id)]),
            Title = title,
            OriginalTitle = string.IsNullOrWhiteSpace(dto.OriginalTitle) ? null : dto.OriginalTitle,
            Year = MapYear(dto.ReleaseDate),
            Lifecycle = new MovieReleaseTimeline { EvaluatedOn = evaluatedOn },
            Overview = string.IsNullOrWhiteSpace(dto.Overview) ? null : dto.Overview,
            Artwork = MapArtwork(dto.PosterPath, dto.BackdropPath, settings.ImageBaseUrl, $"search/movie ({dto.Id})"),
            Popularity = dto.Popularity,
            Ratings = MapRatings(dto.VoteAverage, dto.VoteCount),
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

        return new MovieReleaseTimeline
        {
            InCinemas = inCinemas,
            Physical = physical,
            Digital = digital,
            EvaluatedOn = evaluatedOn,
        };
    }

    private static ArtworkSet MapArtwork(TmdbMovieDetailsDto dto, Uri imageBaseUrl) =>
        MapArtwork(dto.PosterPath, dto.BackdropPath, imageBaseUrl, $"movie/{dto.Id}");

    private static ArtworkSet MapArtwork(
        string? posterPath,
        string? backdropPath,
        Uri imageBaseUrl,
        string operation)
    {
        // Null means absent. An explicit empty value is malformed and must reach the validator.
        var images = new List<ArtworkImage>();

        if (posterPath is { } poster)
        {
            images.Add(new ArtworkImage("poster", CombineImageUri(imageBaseUrl, poster, operation, "poster_path")));
        }

        if (backdropPath is { } backdrop)
        {
            images.Add(new ArtworkImage("fanart", CombineImageUri(imageBaseUrl, backdrop, operation, "backdrop_path")));
        }

        return ArtworkSet.From(images);
    }

    /// <summary>Builds an artwork URI without allowing the response path to replace the configured authority.</summary>
    private static Uri CombineImageUri(Uri imageBaseUrl, string path, string operation, string fact)
    {
        if (!ArtworkPathPattern().IsMatch(path))
        {
            throw new TmdbResponseFormatException(operation, $"had a malformed {fact}.");
        }

        return new UriBuilder(imageBaseUrl) { Path = imageBaseUrl.AbsolutePath + path[1..] }.Uri;
    }

    /// <summary>Matches one root-relative TMDb artwork filename, with no extra path or URI components.</summary>
    [GeneratedRegex(@"^/[A-Za-z0-9_-]+\.[A-Za-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ArtworkPathPattern();

    private static IReadOnlyList<Rating> MapRatings(TmdbMovieDetailsDto dto) =>
        MapRatings(dto.VoteAverage, dto.VoteCount);

    private static IReadOnlyList<Rating> MapRatings(double? voteAverage, long? voteCount) =>
        voteAverage is { } average && RatingScale.OutOfTen.Contains((decimal)average)
            ? [new Rating("tmdb", (decimal)average, RatingScale.OutOfTen, RatingVoice.Audience, voteCount)]
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

    private static Uri? TryParseWebUri(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var isWeb = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

        return isWeb ? uri : null;
    }
}
