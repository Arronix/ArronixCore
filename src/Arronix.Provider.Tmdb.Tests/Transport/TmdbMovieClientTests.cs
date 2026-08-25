using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Provider.Tmdb.Settings;
using Arronix.Provider.Tmdb.Tests.Support;
using Arronix.Provider.Tmdb.Transport;
using FluentAssertions;
using NUnit.Framework;

namespace Arronix.Provider.Tmdb.Tests.Transport;

[TestFixture]
public sealed class TmdbMovieClientTests
{
    private const string Token = "test-read-access-token-super-secret";

    private static readonly TmdbSettingsValues Settings = new(
        Token,
        new Uri("https://api.themoviedb.org/3/"),
        new Uri("https://image.tmdb.org/t/p/original/"),
        "US");

    [Test]
    public async Task SearchMoviesAsync_maps_a_successful_response()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"page":1,"results":[{"id":603,"title":"The Matrix"}],"total_pages":1,"total_results":1}
            """));

        var client = new TmdbMovieClient(context.RequireHttp(), Settings);
        var result = await client.SearchMoviesAsync("matrix", page: 1, CancellationToken.None);

        result.Results.Should().ContainSingle();
        result.Results![0].Id.Should().Be(603);
        result.Results[0].Title.Should().Be("The Matrix");
        result.TotalResults.Should().Be(1);
    }

    [Test]
    public async Task SearchMoviesAsync_maps_an_empty_result_page()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"page":1,"results":[],"total_pages":1,"total_results":0}
            """));

        var client = new TmdbMovieClient(context.RequireHttp(), Settings);
        var result = await client.SearchMoviesAsync("no such movie", page: 1, CancellationToken.None);

        result.Results.Should().BeEmpty();
    }

    [Test]
    public async Task GetMovieDetailsAsync_returns_null_for_a_TMDb_404()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.NotFound, """
            {"status_code":34,"status_message":"The resource you requested could not be found."}
            """));

        var client = new TmdbMovieClient(context.RequireHttp(), Settings);
        var details = await client.GetMovieDetailsAsync(999999999, CancellationToken.None);

        details.Should().BeNull();
    }

    [Test]
    public async Task GetMovieDetailsAsync_maps_a_successful_response()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {
                "id": 603,
                "title": "The Matrix",
                "release_date": "1999-03-30",
                "runtime": 136,
                "genres": [{"id": 28, "name": "Action"}],
                "external_ids": {"imdb_id": "tt0133093"}
            }
            """));

        var client = new TmdbMovieClient(context.RequireHttp(), Settings);
        var details = await client.GetMovieDetailsAsync(603, CancellationToken.None);

        details.Should().NotBeNull();
        details!.Title.Should().Be("The Matrix");
        details.ExternalIds!.ImdbId.Should().Be("tt0133093");
    }

    [Test]
    public async Task GetMovieDetailsAsync_throws_a_format_exception_for_malformed_json()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, "{ not json"));
        var client = new TmdbMovieClient(context.RequireHttp(), Settings);

        await FluentActions.Awaiting(() => client.GetMovieDetailsAsync(603, CancellationToken.None))
            .Should().ThrowAsync<TmdbResponseFormatException>()
            .WithMessage("*not well-formed JSON*");
    }

    [Test]
    public async Task GetMovieDetailsAsync_throws_an_api_exception_for_a_failed_response()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.Unauthorized, """
            {"status_code":7,"status_message":"Invalid API key: you must be granted a valid key."}
            """));
        var client = new TmdbMovieClient(context.RequireHttp(), Settings);

        (await FluentActions.Awaiting(() => client.GetMovieDetailsAsync(603, CancellationToken.None))
            .Should().ThrowAsync<TmdbApiException>())
            .Where(exception =>
                exception.StatusCode == HttpStatusCode.Unauthorized
                && exception.Message.Contains("Invalid API key", StringComparison.Ordinal));
    }

    [Test]
    public async Task SearchMoviesAsync_propagates_cancellation()
    {
        var context = TestHarness.CreateContext((_, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}"));
        });

        var client = new TmdbMovieClient(context.RequireHttp(), Settings);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await FluentActions.Awaiting(() => client.SearchMoviesAsync("matrix", page: 1, canceled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task GetChangedMoviesAsync_carries_paging_facts_through()
    {
        var (context, handler) = TestHarness.CreateContextWithHandler(_ => JsonResponse(HttpStatusCode.OK, """
            {"results":[{"id":1},{"id":2}],"page":1,"total_pages":3}
            """));

        var client = new TmdbMovieClient(context.RequireHttp(), Settings);
        var page = await client.GetChangedMoviesAsync(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 14), page: 1, CancellationToken.None);

        page.Results.Should().HaveCount(2);
        page.Page.Should().Be(1);
        page.TotalPages.Should().Be(3);

        var query = handler.Requests.Single().RequestUri!.Query;
        query.Should().Contain("start_date=2024-01-01");
        query.Should().Contain("end_date=2024-01-14");
    }

    [Test]
    public async Task Requests_send_the_token_as_an_authorization_bearer_header_never_in_the_url()
    {
        var (context, handler) = TestHarness.CreateContextWithHandler(_ => JsonResponse(HttpStatusCode.OK, """
            {"page":1,"results":[],"total_pages":1,"total_results":0}
            """));

        var client = new TmdbMovieClient(context.RequireHttp(), Settings);
        await client.SearchMoviesAsync("matrix", page: 1, CancellationToken.None);

        var request = handler.Requests.Single();
        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be(Token);
        request.RequestUri!.ToString().Should().NotContain(Token);
        request.RequestUri.Query.Should().NotContain("api_key");
    }

    [Test]
    public async Task A_failed_response_message_never_contains_the_configured_token()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.Unauthorized, """
            {"status_code":7,"status_message":"Invalid API key: you must be granted a valid key."}
            """));
        var client = new TmdbMovieClient(context.RequireHttp(), Settings);

        var exception = await FluentActions.Awaiting(() => client.GetMovieDetailsAsync(603, CancellationToken.None))
            .Should().ThrowAsync<TmdbApiException>();

        exception.Which.Message.Should().NotContain(Token);
        exception.Which.ToString().Should().NotContain(Token);
    }

    [Test]
    public async Task A_malformed_response_message_never_contains_the_configured_token()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, "{ not json"));
        var client = new TmdbMovieClient(context.RequireHttp(), Settings);

        var exception = await FluentActions.Awaiting(() => client.GetMovieDetailsAsync(603, CancellationToken.None))
            .Should().ThrowAsync<TmdbResponseFormatException>();

        exception.Which.Message.Should().NotContain(Token);
    }

    [Test]
    public async Task TestAuthenticationAsync_succeeds_for_a_valid_token()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"success":true,"status_code":1,"status_message":"OK"}
            """));

        var client = new TmdbMovieClient(context.RequireHttp(), Settings);
        var outcome = await client.TestAuthenticationAsync(CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task TestAuthenticationAsync_fails_for_an_invalid_token()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.Unauthorized, """
            {"status_code":7,"status_message":"Invalid API key: you must be granted a valid key."}
            """));

        var client = new TmdbMovieClient(context.RequireHttp(), Settings);
        var outcome = await client.TestAuthenticationAsync(CancellationToken.None);

        outcome.IsValid.Should().BeFalse();
        outcome.Failures.Should().ContainSingle(failure => failure.Message.Contains("Invalid API key", StringComparison.Ordinal));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
