using System;
using System.Text.Json;
using Arronix.Abstractions.Identity;
using Arronix.Media.Movies;
using Arronix.Provider.Tmdb.Movies;
using Arronix.Provider.Tmdb.Settings;
using Arronix.Provider.Tmdb.Transport;
using FluentAssertions;
using NUnit.Framework;

namespace Arronix.Provider.Tmdb.Tests.Movies;

/// <summary>
/// Proves that mapping TMDb's shape onto <c>Movie</c> is not this provider's blocker: given a key, the
/// mapper produces a fully valid, fully shaped movie every time. Only <see cref="MovieMaterializationBoundary"/>
/// withholds the key itself, pending G04.
/// </summary>
[TestFixture]
public sealed class TmdbMovieMapperTests
{
    private static readonly TmdbSettingsValues Settings = new(
        "test-api-key",
        new Uri("https://api.themoviedb.org/3/"),
        new Uri("https://image.tmdb.org/t/p/original/"),
        "US");

    [Test]
    public void ToMovie_produces_a_fully_shaped_movie_from_a_complete_payload()
    {
        var dto = Deserialize("""
            {
                "id": 603,
                "title": "The Matrix",
                "original_title": "The Matrix",
                "overview": "A computer hacker learns the truth.",
                "release_date": "1999-03-30",
                "runtime": 136,
                "genres": [{"id": 28, "name": "Action"}, {"id": 878, "name": "Science Fiction"}],
                "original_language": "en",
                "popularity": 82.5,
                "vote_average": 8.2,
                "vote_count": 24000,
                "poster_path": "/poster.jpg",
                "backdrop_path": "/backdrop.jpg",
                "homepage": "https://www.warnerbros.com/movies/matrix",
                "production_companies": [{"id": 79, "name": "Village Roadshow Pictures"}],
                "external_ids": {"imdb_id": "tt0133093"},
                "release_dates": {
                    "results": [
                        {
                            "iso_3166_1": "US",
                            "release_dates": [
                                {"certification": "R", "release_date": "1999-03-31T00:00:00.000Z", "type": 3},
                                {"certification": "", "release_date": "1999-09-21T00:00:00.000Z", "type": 5}
                            ]
                        }
                    ]
                }
            }
            """);

        var evaluatedOn = new DateOnly(2024, 6, 1);
        var movie = TmdbMovieMapper.ToMovie(MediaItemId.FromInt64(42), dto, Settings, evaluatedOn);

        movie.Key.Should().Be(MediaItemId.FromInt64(42));
        movie.Title.Should().Be("The Matrix");
        movie.OriginalTitle.Should().Be("The Matrix");
        movie.Year.Should().Be(1999);
        movie.Overview.Should().Be("A computer hacker learns the truth.");
        movie.Runtime.Should().Be(TimeSpan.FromMinutes(136));
        movie.Genres.Should().BeEquivalentTo(["Action", "Science Fiction"]);
        movie.Organization.Should().Be("Village Roadshow Pictures");
        movie.Website.Should().Be(new Uri("https://www.warnerbros.com/movies/matrix"));
        movie.Popularity.Should().Be(82.5);
        movie.Certification.Should().NotBeNull();
        movie.Certification!.Code.Should().Be("R");
        movie.Certification.Region.Should().Be("US");
        movie.Ratings.Should().ContainSingle();
        movie.Ratings[0].Source.Should().Be("tmdb");
        movie.ExternalIds.TryGet("tmdb", out var tmdbId).Should().BeTrue();
        tmdbId.Value.Should().Be("603");
        movie.ExternalIds.TryGet("imdb", out var imdbId).Should().BeTrue();
        imdbId.Value.Should().Be("tt0133093");
        movie.Artwork.TryGet("poster", out var poster).Should().BeTrue();
        poster!.Address.Should().Be(new Uri("https://image.tmdb.org/t/p/original/poster.jpg"));
        movie.Artwork.TryGet("fanart", out var fanart).Should().BeTrue();
        fanart!.Address.Should().Be(new Uri("https://image.tmdb.org/t/p/original/backdrop.jpg"));

        movie.Lifecycle.InCinemas.Should().Be(new DateOnly(1999, 3, 31));
        movie.Lifecycle.Physical.Should().Be(new DateOnly(1999, 9, 21));
        movie.Lifecycle.Digital.Should().BeNull();
        movie.Lifecycle.EvaluatedOn.Should().Be(evaluatedOn);
        movie.Status.Should().Be(MovieReleaseStage.Released);
    }

    [Test]
    public void ToMovie_falls_back_to_a_placeholder_title_when_TMDb_has_none()
    {
        var dto = Deserialize("""{ "id": 12345 }""");
        var movie = TmdbMovieMapper.ToMovie(MediaItemId.FromInt64(1), dto, Settings, new DateOnly(2024, 1, 1));

        movie.Title.Should().Be("TMDb 12345");
        movie.Genres.Should().BeEmpty();
        movie.Artwork.Images.Should().BeEmpty();
        movie.Certification.Should().BeNull();
        movie.Ratings.Should().BeEmpty();
        movie.ExternalIds.TryGet("tmdb", out _).Should().BeTrue();
        movie.ExternalIds.TryGet("imdb", out _).Should().BeFalse();
    }

    [Test]
    public void ToMovie_ignores_release_dates_outside_the_configured_region()
    {
        var dto = Deserialize("""
            {
                "id": 1,
                "title": "Region Test",
                "release_dates": {
                    "results": [
                        {
                            "iso_3166_1": "GB",
                            "release_dates": [{"certification": "15", "release_date": "2020-01-01T00:00:00.000Z", "type": 3}]
                        }
                    ]
                }
            }
            """);

        var movie = TmdbMovieMapper.ToMovie(MediaItemId.FromInt64(2), dto, Settings, new DateOnly(2024, 1, 1));

        movie.Certification.Should().BeNull();
        movie.Lifecycle.InCinemas.Should().BeNull();
    }

    private static TmdbMovieDetailsDto Deserialize(string json) =>
        JsonSerializer.Deserialize<TmdbMovieDetailsDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
