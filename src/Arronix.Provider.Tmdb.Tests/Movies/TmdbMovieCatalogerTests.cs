using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Provider.Tmdb.Movies;
using Arronix.Provider.Tmdb.Tests.Support;
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
    public async Task SearchAsync_stops_at_the_materialization_boundary_when_TMDb_matches_something()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"page":1,"results":[{"id":603,"title":"The Matrix"}],"total_pages":1,"total_results":1}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        await FluentActions
            .Awaiting(() => cataloger.SearchAsync(TestInvocation.ForCataloger(), new CatalogQuery("matrix"), CancellationToken.None))
            .Should().ThrowAsync<MovieMaterializationNotSupportedException>()
            .WithMessage("*MediaItemId is host-minted*");
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

    [Test]
    public async Task GetAsync_stops_at_the_materialization_boundary_when_TMDb_has_the_movie()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"id":603,"title":"The Matrix","release_date":"1999-03-30"}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        await FluentActions
            .Awaiting(() => cataloger.GetAsync(TestInvocation.ForCataloger(), ExternalId.Of("tmdb", "603"), CancellationToken.None))
            .Should().ThrowAsync<MovieMaterializationNotSupportedException>();
    }

    [Test]
    public async Task ChangedSinceAsync_aggregates_every_page()
    {
        var context = TestHarness.CreateContext(request =>
        {
            var onPageTwo = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal);
            var body = onPageTwo
                ? """{"results":[{"id":20}],"page":2,"total_pages":2}"""
                : """{"results":[{"id":10}],"page":1,"total_pages":2}""";
            return JsonResponse(HttpStatusCode.OK, body);
        });
        var cataloger = new TmdbMovieCataloger(context);

        var changed = await cataloger.ChangedSinceAsync(
            TestInvocation.ForCataloger(), new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        changed.Should().HaveCount(2);
        changed.Should().Contain(id => id.Scheme == "tmdb" && id.Value == "10");
        changed.Should().Contain(id => id.Scheme == "tmdb" && id.Value == "20");
    }

    [Test]
    public async Task ChangedSinceAsync_returns_empty_when_TMDb_reports_no_changes()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"results":[],"page":1,"total_pages":1}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        var changed = await cataloger.ChangedSinceAsync(
            TestInvocation.ForCataloger(), new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

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
    public async Task TestAsync_reports_the_configured_key_as_invalid()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.Unauthorized, """
            {"status_code":7,"status_message":"Invalid API key: you must be granted a valid key."}
            """));
        var cataloger = new TmdbMovieCataloger(context);

        var outcome = await cataloger.TestAsync(TestInvocation.ForCataloger(), CancellationToken.None);

        outcome.IsValid.Should().BeFalse();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
