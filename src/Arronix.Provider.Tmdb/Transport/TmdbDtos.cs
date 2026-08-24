using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Arronix.Provider.Tmdb.Transport;

/// <summary>A TMDb paged list response, shared by search and discovery endpoints.</summary>
internal sealed record TmdbPagedResult<TResult>(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("results")] IReadOnlyList<TResult>? Results,
    [property: JsonPropertyName("total_pages")] int TotalPages,
    [property: JsonPropertyName("total_results")] int TotalResults);

/// <summary>One row of a TMDb search or discovery result page.</summary>
internal sealed record TmdbMovieSummaryDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("original_title")] string? OriginalTitle,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("release_date")] string? ReleaseDate,
    [property: JsonPropertyName("genre_ids")] IReadOnlyList<int>? GenreIds,
    [property: JsonPropertyName("original_language")] string? OriginalLanguage,
    [property: JsonPropertyName("popularity")] double? Popularity,
    [property: JsonPropertyName("vote_average")] double? VoteAverage,
    [property: JsonPropertyName("vote_count")] long? VoteCount,
    [property: JsonPropertyName("poster_path")] string? PosterPath,
    [property: JsonPropertyName("backdrop_path")] string? BackdropPath);

/// <summary>One TMDb genre.</summary>
internal sealed record TmdbGenreDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>The external identifiers TMDb cross-references for a movie.</summary>
internal sealed record TmdbExternalIdsDto(
    [property: JsonPropertyName("imdb_id")] string? ImdbId);

/// <summary>One dated release entry within one country's release-dates block.</summary>
internal sealed record TmdbReleaseDateEntryDto(
    [property: JsonPropertyName("certification")] string? Certification,
    [property: JsonPropertyName("release_date")] string? ReleaseDate,
    [property: JsonPropertyName("type")] int Type);

/// <summary>One country's release-dates block.</summary>
internal sealed record TmdbReleaseDatesCountryDto(
    [property: JsonPropertyName("iso_3166_1")] string? CountryCode,
    [property: JsonPropertyName("release_dates")] IReadOnlyList<TmdbReleaseDateEntryDto>? ReleaseDates);

/// <summary>The <c>append_to_response=release_dates</c> envelope.</summary>
internal sealed record TmdbReleaseDatesDto(
    [property: JsonPropertyName("results")] IReadOnlyList<TmdbReleaseDatesCountryDto>? Results);

/// <summary>One studio or distributor credited on a movie.</summary>
internal sealed record TmdbProductionCompanyDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>The full TMDb movie-details payload, including appended release dates and external ids.</summary>
internal sealed record TmdbMovieDetailsDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("original_title")] string? OriginalTitle,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("release_date")] string? ReleaseDate,
    [property: JsonPropertyName("runtime")] int? Runtime,
    [property: JsonPropertyName("genres")] IReadOnlyList<TmdbGenreDto>? Genres,
    [property: JsonPropertyName("original_language")] string? OriginalLanguage,
    [property: JsonPropertyName("popularity")] double? Popularity,
    [property: JsonPropertyName("vote_average")] double? VoteAverage,
    [property: JsonPropertyName("vote_count")] long? VoteCount,
    [property: JsonPropertyName("poster_path")] string? PosterPath,
    [property: JsonPropertyName("backdrop_path")] string? BackdropPath,
    [property: JsonPropertyName("homepage")] string? Homepage,
    [property: JsonPropertyName("production_companies")] IReadOnlyList<TmdbProductionCompanyDto>? ProductionCompanies,
    [property: JsonPropertyName("external_ids")] TmdbExternalIdsDto? ExternalIds,
    [property: JsonPropertyName("release_dates")] TmdbReleaseDatesDto? ReleaseDates);

/// <summary>One changed-movie row from the TMDb changes feed.</summary>
internal sealed record TmdbChangeEntryDto(
    [property: JsonPropertyName("id")] int Id);

/// <summary>The TMDb <c>/movie/changes</c> response.</summary>
internal sealed record TmdbChangesResponseDto(
    [property: JsonPropertyName("results")] IReadOnlyList<TmdbChangeEntryDto>? Results,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("total_pages")] int TotalPages);

/// <summary>TMDb's own error envelope, returned alongside error HTTP statuses.</summary>
internal sealed record TmdbErrorDto(
    [property: JsonPropertyName("status_code")] int? StatusCode,
    [property: JsonPropertyName("status_message")] string? StatusMessage);
