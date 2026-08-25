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
public sealed class TmdbMovieCuratorTests
{
    [Test]
    public async Task FetchAsync_returns_an_empty_list_when_TMDb_offers_nothing()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"page":1,"results":[],"total_pages":1,"total_results":0}
            """));
        var curator = new TmdbMovieCurator(context);

        var fetch = await curator.FetchAsync(TestInvocation.ForCurator(), CancellationToken.None);

        fetch.Items.Should().BeEmpty();
        fetch.AnyFailure.Should().BeFalse();
    }

    [Test]
    public async Task FetchAsync_returns_TMDb_catalog_references_without_inventing_curator_entry_ids()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"page":1,"results":[{"id":603,"title":"The Matrix"}],"total_pages":1,"total_results":1}
            """));
        var curator = new TmdbMovieCurator(context);

        var fetch = await curator.FetchAsync(TestInvocation.ForCurator(), CancellationToken.None);

        fetch.Items.Should().ContainSingle().Which.Should().Be(
            new CuratedReference(ExternalId.Of("tmdb", "603")));
        fetch.Items[0].EntryId.Should().BeNull(
            "TMDb's popular list exposes no identity distinct from the referenced catalog item");
        fetch.AnyFailure.Should().BeFalse();
    }

    [Test]
    public async Task FetchAsync_surfaces_a_failed_response_as_a_TMDb_api_exception()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.InternalServerError, """
            {"status_code":11,"status_message":"Internal error"}
            """));
        var curator = new TmdbMovieCurator(context);

        await FluentActions
            .Awaiting(() => curator.FetchAsync(TestInvocation.ForCurator(), CancellationToken.None))
            .Should().ThrowAsync<Arronix.Provider.Tmdb.Transport.TmdbApiException>();
    }

    [Test]
    public async Task TestAsync_succeeds_for_a_valid_token()
    {
        var context = TestHarness.CreateContext(_ => JsonResponse(HttpStatusCode.OK, """
            {"success":true,"status_code":1,"status_message":"OK"}
            """));
        var curator = new TmdbMovieCurator(context);

        var outcome = await curator.TestAsync(TestInvocation.ForCurator(), CancellationToken.None);

        outcome.IsValid.Should().BeTrue();
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
