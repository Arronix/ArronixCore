using System;
using System.Text.Json;
using Arronix.Media.Movies;
using Arronix.Provider.Tmdb.Movies;
using Arronix.Provider.Tmdb.Settings;
using Arronix.Provider.Tmdb.Transport;
using FluentAssertions;
using NUnit.Framework;

namespace Arronix.Provider.Tmdb.Tests.Movies;

/// <summary>Verifies conversion from TMDb payloads to exact <see cref="Movie"/> values.</summary>
[TestFixture]
public sealed class TmdbMovieMapperTests
{
    private static readonly TmdbSettingsValues Settings = new(
        "test-read-access-token",
        new Uri("https://api.themoviedb.org/3/"),
        new Uri("https://image.tmdb.org/t/p/original/"),
        "US");

    [Test]
    public void ToMovie_maps_a_complete_payload_leaving_certification_and_original_language_unset()
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
        var movie = TmdbMovieMapper.ToMovie(dto, Settings, evaluatedOn);

        movie.Title.Should().Be("The Matrix");
        movie.OriginalTitle.Should().Be("The Matrix");
        movie.Year.Should().Be(1999);
        movie.Overview.Should().Be("A computer hacker learns the truth.");
        movie.Runtime.Should().Be(TimeSpan.FromMinutes(136));
        movie.Genres.Should().BeEquivalentTo(["Action", "Science Fiction"]);
        movie.Organization.Should().Be("Village Roadshow Pictures");
        movie.Website.Should().Be(new Uri("https://www.warnerbros.com/movies/matrix"));
        movie.Popularity.Should().Be(82.5);

        // TMDb is not the certifying authority, and a language code is not a complete Language value.
        movie.Certification.Should().BeNull();
        movie.OriginalLanguage.Should().BeNull();

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
    public void ToMovie_throws_when_TMDb_supplies_neither_title_nor_original_title()
    {
        var dto = Deserialize("""{ "id": 12345 }""");

        FluentActions.Invoking(() => TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1)))
            .Should().Throw<TmdbResponseFormatException>()
            .WithMessage("*movie/12345*")
            .Where(exception => exception.Message.Contains("title", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public void ToMovie_falls_back_to_the_original_title_when_only_title_is_missing()
    {
        var dto = Deserialize("""{ "id": 12345, "original_title": "Il Titolo Originale" }""");
        var movie = TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1));

        movie.Title.Should().Be("Il Titolo Originale");
        movie.Genres.Should().BeEmpty();
        movie.Artwork.Images.Should().BeEmpty();
        movie.Certification.Should().BeNull();
        movie.Ratings.Should().BeEmpty();
        movie.ExternalIds.TryGet("tmdb", out _).Should().BeTrue();
        movie.ExternalIds.TryGet("imdb", out _).Should().BeFalse();
    }

    [TestCase("file:///tmp/movie.html")]
    [TestCase("ftp://example.test/movie")]
    [TestCase("javascript:alert('x')")]
    public void ToMovie_ignores_a_homepage_that_is_not_an_http_or_https_url(string homepage)
    {
        var dto = Deserialize($$"""{ "id": 603, "title": "The Matrix", "homepage": "{{homepage}}" }""");

        var movie = TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1));

        movie.Website.Should().BeNull();
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

        var movie = TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1));

        movie.Certification.Should().BeNull();
        movie.Lifecycle.InCinemas.Should().BeNull();
    }

    [Test]
    public void ToMovie_does_not_misattribute_a_top_level_release_date_to_the_configured_region()
    {
        // A top-level release_date plus a release_dates block that only covers a different region: the
        // configured region ("US") has no row at all. InCinemas must stay null, not silently borrow the
        // top-level date (which belongs to no stated region) or the GB row (which belongs to a region
        // nobody configured).
        var dto = Deserialize("""
            {
                "id": 3,
                "title": "Misattribution Test",
                "release_date": "2020-06-15",
                "release_dates": {
                    "results": [
                        {
                            "iso_3166_1": "GB",
                            "release_dates": [{"certification": "15", "release_date": "2020-06-10T00:00:00.000Z", "type": 3}]
                        }
                    ]
                }
            }
            """);

        var movie = TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1));

        movie.Lifecycle.InCinemas.Should().BeNull();
        movie.Lifecycle.Physical.Should().BeNull();
        movie.Lifecycle.Digital.Should().BeNull();

        // The top-level date is still a legitimate global fact for Year, which is not region-scoped.
        movie.Year.Should().Be(2020);
    }

    [Test]
    public void ToMovie_artwork_uri_shares_the_exact_authority_of_the_configured_image_base_url()
    {
        var dto = Deserialize("""{ "id": 603, "title": "The Matrix", "poster_path": "/poster.jpg" }""");
        var movie = TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1));

        movie.Artwork.TryGet("poster", out var poster).Should().BeTrue();
        var authority = poster!.Address.GetLeftPart(UriPartial.Authority);
        authority.Should().Be(Settings.ImageBaseUrl.GetLeftPart(UriPartial.Authority));
        poster.Address.Scheme.Should().Be(Settings.ImageBaseUrl.Scheme);
        poster.Address.Host.Should().Be(Settings.ImageBaseUrl.Host);
        poster.Address.Port.Should().Be(Settings.ImageBaseUrl.Port);
        poster.Address.Should().Be(new Uri("https://image.tmdb.org/t/p/original/poster.jpg"));
    }

    [Test]
    public void ToMovie_treats_a_missing_poster_and_backdrop_path_as_absence_not_a_malformed_value()
    {
        // No poster_path/backdrop_path field at all: null, genuinely omitted by TMDb. This must not throw,
        // and must not be confused with the explicit-empty-string case below, which is a value TMDb did
        // supply and is malformed.
        var dto = Deserialize("""{ "id": 603, "title": "The Matrix" }""");
        var movie = TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1));

        movie.Artwork.Images.Should().BeEmpty();
    }

    [Test]
    public void ToMovie_throws_when_poster_path_is_an_explicit_empty_string_rather_than_absent()
    {
        // An empty string is still a value TMDb explicitly supplied, distinct from the field being omitted
        // (null) entirely. It is not a canonical artwork path and must not be silently folded into
        // "absent" before the validator ever sees it.
        var dto = Deserialize("""{ "id": 603, "title": "The Matrix", "poster_path": "" }""");

        FluentActions
            .Invoking(() => TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1)))
            .Should().Throw<TmdbResponseFormatException>()
            .WithMessage("*movie/603*")
            .Where(exception => exception.Message.Contains("poster_path", StringComparison.Ordinal));
    }

    [Test]
    public void ToMovie_throws_when_backdrop_path_is_an_explicit_empty_string_rather_than_absent()
    {
        var dto = Deserialize("""{ "id": 603, "title": "The Matrix", "backdrop_path": "" }""");

        FluentActions
            .Invoking(() => TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1)))
            .Should().Throw<TmdbResponseFormatException>()
            .WithMessage("*movie/603*")
            .Where(exception => exception.Message.Contains("backdrop_path", StringComparison.Ordinal));
    }

    [TestCase("https://evil.example/tracker.jpg", TestName = "{m}(absolute_url_changes_authority)")]
    [TestCase("//evil.example/tracker.jpg", TestName = "{m}(protocol_relative_changes_authority)")]
    [TestCase("/../../etc/passwd", TestName = "{m}(dot_segment_escape)")]
    [TestCase("/..", TestName = "{m}(bare_dot_dot_segment)")]
    [TestCase("/poster.jpg?evil=1", TestName = "{m}(query_injection)")]
    [TestCase("/poster.jpg#frag", TestName = "{m}(fragment_injection)")]
    [TestCase("/sub/poster.jpg", TestName = "{m}(extra_path_segment)")]
    [TestCase("poster.jpg", TestName = "{m}(missing_leading_slash)")]
    public void ToMovie_throws_when_poster_path_is_malformed_instead_of_silently_dropping_or_resolving_it(
        string malformedPosterPath)
    {
        var dto = Deserialize($$"""{ "id": 603, "title": "The Matrix", "poster_path": "{{malformedPosterPath}}" }""");

        FluentActions
            .Invoking(() => TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1)))
            .Should().Throw<TmdbResponseFormatException>()
            .WithMessage("*movie/603*")
            .Where(exception => exception.Message.Contains("poster_path", StringComparison.Ordinal));
    }

    [Test]
    public void ToMovie_throws_when_backdrop_path_is_malformed()
    {
        var dto = Deserialize("""{ "id": 603, "title": "The Matrix", "backdrop_path": "https://evil.example/tracker.jpg" }""");

        FluentActions
            .Invoking(() => TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1)))
            .Should().Throw<TmdbResponseFormatException>()
            .Where(exception => exception.Message.Contains("backdrop_path", StringComparison.Ordinal));
    }

    [Test]
    public void ToMovie_maps_a_normal_poster_and_backdrop_path_correctly_alongside_the_rejection_cases()
    {
        var dto = Deserialize("""
            { "id": 603, "title": "The Matrix", "poster_path": "/qJ2tW6WMUDux911r6m7haRef0WH.jpg", "backdrop_path": "/h0xasketmA5CvSPq49CGxUxfnI.jpg" }
            """);

        var movie = TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1));

        movie.Artwork.TryGet("poster", out var poster).Should().BeTrue();
        poster!.Address.Should().Be(new Uri("https://image.tmdb.org/t/p/original/qJ2tW6WMUDux911r6m7haRef0WH.jpg"));
        movie.Artwork.TryGet("fanart", out var fanart).Should().BeTrue();
        fanart!.Address.Should().Be(new Uri("https://image.tmdb.org/t/p/original/h0xasketmA5CvSPq49CGxUxfnI.jpg"));
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void ToMovie_throws_when_the_movie_id_itself_is_malformed(int malformedId)
    {
        var dto = Deserialize($$"""{ "id": {{malformedId}}, "title": "The Matrix" }""");

        FluentActions
            .Invoking(() => TmdbMovieMapper.ToMovie(dto, Settings, new DateOnly(2024, 1, 1)))
            .Should().Throw<TmdbResponseFormatException>()
            .Where(exception => exception.Message.Contains("malformed id", StringComparison.Ordinal));
    }

    private static TmdbMovieDetailsDto Deserialize(string json) =>
        JsonSerializer.Deserialize<TmdbMovieDetailsDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
}
