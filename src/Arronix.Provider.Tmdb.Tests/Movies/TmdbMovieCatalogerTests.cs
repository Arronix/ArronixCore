using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Provider.Tmdb.Movies;
using Arronix.Provider.Tmdb.Tests.Support;
using Arronix.Provider.Tmdb.Transport;
using FluentAssertions;
using NUnit.Framework;

namespace Arronix.Provider.Tmdb.Tests.Movies;

[TestFixture]
public sealed class TmdbMovieCatalogerTests
{
    [Test]
    public async Task SearchAsync_returns_an_empty_list_when_TMDb_matches_nothing()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"page":1,"results":[],"total_pages":1,"total_results":0}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        var matches = await cataloger.SearchAsync(
            TestInvocation.ForCataloger(), new CatalogQuery("no such movie"), CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Test]
    public async Task SearchAsync_returns_the_exact_movie_shape_with_TMDb_catalog_identity()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"page":1,"results":[{"id":603,"title":"The Matrix"}],"total_pages":1,"total_results":1}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        var matches = await cataloger.SearchAsync(
            TestInvocation.ForCataloger(), new CatalogQuery("matrix"), CancellationToken.None);

        matches.Should().ContainSingle();
        matches[0].Title.Should().Be("The Matrix");
        matches[0].ExternalIds.TryGet("tmdb", out var id).Should().BeTrue();
        id.Value.Should().Be("603");
    }

    [Test]
    public async Task SearchAsync_with_an_id_resolves_it_directly_and_sends_no_text_search_request()
    {
        var (context, handler) = TestHarness.CreateContextWithHandler(_ => JsonResponse(HttpStatusCode.OK, """
            {"id":603,"title":"The Matrix","release_date":"1999-03-30"}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        var matches = await cataloger.SearchAsync(
            TestInvocation.ForCataloger(),
            new CatalogQuery("this text must be ignored", ExternalId.Of("tmdb", "603")),
            CancellationToken.None);

        matches.Should().ContainSingle().Which.Title.Should().Be("The Matrix");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Contain("movie/603");
        handler.Requests[0].RequestUri!.AbsolutePath.Should().NotContain("search");
    }

    [Test]
    public async Task SearchAsync_with_an_unresolvable_id_returns_empty_without_a_text_search_fallback()
    {
        var (context, handler) = TestHarness.CreateContextWithHandler(_ => JsonResponse(HttpStatusCode.NotFound, """
            {"status_code":34,"status_message":"The resource you requested could not be found."}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        var matches = await cataloger.SearchAsync(
            TestInvocation.ForCataloger(),
            new CatalogQuery("this text must be ignored", ExternalId.Of("tmdb", "999999999")),
            CancellationToken.None);

        matches.Should().BeEmpty();
        handler.Requests.Should().ContainSingle("a 404 on the id lookup must not fall back to a text search");
        handler.Requests[0].RequestUri!.AbsolutePath.Should().NotContain("search");
    }

    [Test]
    public async Task SearchAsync_with_a_non_tmdb_scheme_id_returns_empty_without_any_network_call()
    {
        var context = TestHarness.CreateContext(_ => throw new InvalidOperationException("Should not call TMDb."));
        var cataloger = new TmdbMovieCataloger(context);

        var matches = await cataloger.SearchAsync(
            TestInvocation.ForCataloger(),
            new CatalogQuery("this text must be ignored", ExternalId.Of("imdb", "603")),
            CancellationToken.None);

        matches.Should().BeEmpty();
    }

    [Test]
    public async Task GetAsync_returns_null_for_a_TMDb_404()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.NotFound, """
            {"status_code":34,"status_message":"The resource you requested could not be found."}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        var movie = await cataloger.GetAsync(TestInvocation.ForCataloger(), ExternalId.Of("tmdb", "999999999"), CancellationToken.None);

        movie.Should().BeNull();
    }

    [Test]
    public async Task GetAsync_returns_null_for_a_non_numeric_identifier_without_calling_TMDb()
    {
        var context = TestHarness.CreateContext(_ => throw new InvalidOperationException("Should not call TMDb."));
        var cataloger = new TmdbMovieCataloger(context);

        var movie = await cataloger.GetAsync(TestInvocation.ForCataloger(), ExternalId.Of("tmdb", "not-a-number"), CancellationToken.None);

        movie.Should().BeNull();
    }

    [TestCase("0", TestName = "{m}(zero)")]
    [TestCase("-5", TestName = "{m}(negative)")]
    [TestCase("+5", TestName = "{m}(explicit_plus_sign)")]
    [TestCase(" 603", TestName = "{m}(leading_space)")]
    [TestCase("603 ", TestName = "{m}(trailing_space)")]
    [TestCase("0603", TestName = "{m}(leading_zero)")]
    [TestCase("00", TestName = "{m}(all_zeros)")]
    [TestCase("6.03", TestName = "{m}(decimal_point)")]
    [TestCase("6,03", TestName = "{m}(group_separator)")]
    [TestCase("99999999999999999999", TestName = "{m}(overflows_int)")]
    [TestCase("2147483648", TestName = "{m}(one_past_int_max_value)")]
    public async Task GetAsync_returns_null_for_a_non_canonical_id_without_calling_TMDb(string value)
    {
        var context = TestHarness.CreateContext(_ => throw new InvalidOperationException("Should not call TMDb."));
        var cataloger = new TmdbMovieCataloger(context);

        var movie = await cataloger.GetAsync(TestInvocation.ForCataloger(), ExternalId.Of("tmdb", value), CancellationToken.None);

        movie.Should().BeNull();
    }

    [Test]
    public async Task GetAsync_accepts_int_MaxValue_as_the_canonical_upper_bound()
    {
        var (context, handler) = TestHarness.CreateContextWithHandler(_ => JsonResponse(HttpStatusCode.NotFound, """
            {"status_code":34,"status_message":"The resource you requested could not be found."}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        var movie = await cataloger.GetAsync(
            TestInvocation.ForCataloger(),
            ExternalId.Of("tmdb", int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            CancellationToken.None);

        // int.MaxValue is itself canonical and must reach the network (and here TMDb simply doesn't have
        // it, hence the 404); only a value that fails to round-trip or exceeds int.MaxValue is rejected
        // before the network. Asserting the request count is what actually distinguishes this from
        // pre-network rejection — both would otherwise return null.
        movie.Should().BeNull();
        handler.Requests.Should().ContainSingle();
    }

    [Test]
    public async Task GetAsync_returns_null_for_a_non_tmdb_scheme_without_calling_TMDb()
    {
        var context = TestHarness.CreateContext(_ => throw new InvalidOperationException("Should not call TMDb."));
        var cataloger = new TmdbMovieCataloger(context);

        // The value is a perfectly valid TMDb-shaped number; only the scheme is wrong. Owning "tmdb" means
        // declining every other catalog's namespace, not merely failing to parse it.
        var movie = await cataloger.GetAsync(TestInvocation.ForCataloger(), ExternalId.Of("imdb", "603"), CancellationToken.None);

        movie.Should().BeNull();
    }

    [Test]
    public async Task GetAsync_returns_the_exact_movie_shape_when_TMDb_has_the_movie()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"id":603,"title":"The Matrix","release_date":"1999-03-30"}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        var movie = await cataloger.GetAsync(
            TestInvocation.ForCataloger(), ExternalId.Of("tmdb", "603"), CancellationToken.None);

        movie.Should().NotBeNull();
        movie!.Title.Should().Be("The Matrix");
        movie.ExternalIds.TryGet("tmdb", out var id).Should().BeTrue();
        id.Value.Should().Be("603");
    }

    [Test]
    public async Task ChangedSinceAsync_aggregates_every_page_within_one_window()
    {
        // since (Jan 1) through the clock's now (Jan 10) is a single 10-day span, so this stays a
        // single-window test of pagination; ChangedSinceAsync_windows_a_long_range_... below is the
        // windowing test.
        var (context, handler) = TestHarness.CreateContextWithHandler(
            request =>
            {
                var onPageTwo = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal);
                var body = onPageTwo
                    ? """{"results":[{"id":20}],"page":2,"total_pages":2}"""
                    : """{"results":[{"id":10}],"page":1,"total_pages":2}""";
                return JsonResponse(HttpStatusCode.OK, body);
            },
            now: new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.Zero));
        var cataloger = new TmdbMovieCataloger(context);

        var changed = await cataloger.ChangedSinceAsync(
            TestInvocation.ForCataloger(), new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        changed.Should().HaveCount(2);
        changed.Should().Contain(id => id.Scheme == "tmdb" && id.Value == "10");
        changed.Should().Contain(id => id.Scheme == "tmdb" && id.Value == "20");
        handler.Requests.Should().HaveCount(2, "one window, paged twice");
        handler.Requests.Should().OnlyContain(
            request => request.RequestUri!.Query.Contains("start_date=2024-01-01")
                && request.RequestUri!.Query.Contains("end_date=2024-01-10"));
    }

    [Test]
    public async Task ChangedSinceAsync_windows_a_long_range_into_at_most_14_day_queries_and_dedups_across_them()
    {
        // since (Jan 1) through now (Jan 20) is 20 days: two windows, [Jan1,Jan14] and [Jan15,Jan20].
        // Both windows report id 10; only the second also reports id 20, proving cross-window dedup.
        var (context, handler) = TestHarness.CreateContextWithHandler(
            request =>
            {
                var query = request.RequestUri!.Query;
                var body = query.Contains("start_date=2024-01-01", StringComparison.Ordinal)
                    ? """{"results":[{"id":10}],"page":1,"total_pages":1}"""
                    : """{"results":[{"id":10},{"id":20}],"page":1,"total_pages":1}""";
                return JsonResponse(HttpStatusCode.OK, body);
            },
            now: new DateTimeOffset(2024, 1, 20, 0, 0, 0, TimeSpan.Zero));
        var cataloger = new TmdbMovieCataloger(context);

        var changed = await cataloger.ChangedSinceAsync(
            TestInvocation.ForCataloger(), new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        changed.Should().HaveCount(2, "id 10 appears in both windows and must be de-duplicated");
        changed.Should().Contain(id => id.Scheme == "tmdb" && id.Value == "10");
        changed.Should().Contain(id => id.Scheme == "tmdb" && id.Value == "20");

        var queries = handler.Requests.Select(request => request.RequestUri!.Query).ToArray();
        queries.Should().HaveCount(2);
        queries.Should().Contain(query => query.Contains("start_date=2024-01-01") && query.Contains("end_date=2024-01-14"));
        queries.Should().Contain(query => query.Contains("start_date=2024-01-15") && query.Contains("end_date=2024-01-20"));
    }

    [Test]
    public async Task ChangedSinceAsync_returns_empty_when_TMDb_reports_no_changes()
    {
        var context = TestHarness.CreateContext(
            _ => JsonResponse(HttpStatusCode.OK, """{"results":[],"page":1,"total_pages":1}"""),
            now: new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero));
        var cataloger = new TmdbMovieCataloger(context);

        var changed = await cataloger.ChangedSinceAsync(
            TestInvocation.ForCataloger(), new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        changed.Should().BeEmpty();
    }

    [TestCase(0, TestName = "{m}(zero)")]
    [TestCase(-5, TestName = "{m}(negative)")]
    public async Task ChangedSinceAsync_throws_when_TMDb_reports_a_malformed_changed_movie_id(int malformedId)
    {
        var context = TestHarness.CreateContext(
            _ => JsonResponse(HttpStatusCode.OK, $$"""{"results":[{"id":{{malformedId}}}],"page":1,"total_pages":1}"""),
            now: new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero));
        var cataloger = new TmdbMovieCataloger(context);

        var exception = await FluentActions
            .Awaiting(() => cataloger.ChangedSinceAsync(
                TestInvocation.ForCataloger(), new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None))
            .Should().ThrowAsync<TmdbResponseFormatException>();

        exception.Which.Message.Should().Contain("malformed");
    }

    [Test]
    public async Task ChangedSinceAsync_makes_no_request_when_since_is_after_now()
    {
        var context = TestHarness.CreateContext(
            _ => throw new InvalidOperationException("Should not call TMDb."),
            now: new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cataloger = new TmdbMovieCataloger(context);

        var changed = await cataloger.ChangedSinceAsync(
            TestInvocation.ForCataloger(), new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        changed.Should().BeEmpty();
    }

    [Test]
    public void ReadExternalIds_recognizes_markers_without_any_network_call()
    {
        var context = TestHarness.CreateContext(_ => throw new InvalidOperationException("Should not call TMDb."));
        var cataloger = new TmdbMovieCataloger(context);

        var readings = cataloger.ReadExternalIds("Interstellar {tmdb-157336}");

        readings.Should().ContainSingle();
        readings[0].Id.Value.Should().Be("157336");
    }

    [Test]
    public async Task TestAsync_reports_the_configured_token_as_invalid()
    {
        // The JSON body's wording ("Invalid API key") is TMDb's own literal error text, not this
        // provider's naming: it is preserved verbatim rather than "corrected" to match our renamed setting.
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.Unauthorized, """
            {"status_code":7,"status_message":"Invalid API key: you must be granted a valid key."}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        var outcome = await cataloger.TestAsync(TestInvocation.ForCataloger(), CancellationToken.None);

        outcome.IsValid.Should().BeFalse();
    }

    [Test]
    public async Task SearchAsync_never_sends_a_request_when_the_configured_base_url_carries_a_query_secret()
    {
        const string secret = "leaked-tracking-token-c11f9a02";
        var context = TestHarness.CreateContext(_ => throw new InvalidOperationException("Should not call TMDb."));
        var cataloger = new TmdbMovieCataloger(context);
        var invocation = TestInvocation.ForCatalogerWithBaseUrl($"https://api.example.test/3?ref={secret}");

        // The invalid base URL is rejected while reading settings, before TmdbMovieClient exists to build
        // a request from it: there is no request for the secret to appear in, constructed or otherwise.
        var exception = await FluentActions
            .Invoking(() => cataloger.SearchAsync(invocation, new CatalogQuery("matrix"), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        exception.Which.Message.Should().NotContain(secret);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
