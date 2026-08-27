using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Client.Configuration;
using Arronix.Client.Serialization;
using Arronix.Client.Services;
using FluentAssertions;

namespace Arronix.Client.Tests.Services;

/// <summary>What the generic client puts on the wire for item and catalog operations.</summary>
[TestFixture]
internal sealed class ArronixApiClientTests
{
    private static readonly MediaKindId Movies = MediaKindId.FromString("movies");
    private static readonly MediaItemRef Movie = new(Movies, MediaLevelId.FromString("movie"), MediaItemId.FromInt64(42));
    private static readonly MediaItemRef Collection = new(Movies, MediaLevelId.FromString("collection"), MediaItemId.FromInt64(42));

    [Test]
    public void ItemAddressesKeepTheLevelWhenTwoLevelsUseTheSameNumericIdentifier()
    {
        using var scope = new FluentAssertions.Execution.AssertionScope();

        ApiPaths.Item(Movies.Value, Movie).Should().Be("api/v1/kinds/movies/items/movie%3A42");
        ApiPaths.Item(Movies.Value, Collection).Should().Be("api/v1/kinds/movies/items/collection%3A42");
        ApiPaths.ItemChildren(Movies.Value, Collection, []).Should()
            .Be("api/v1/kinds/movies/items/collection%3A42/children");
    }

    [Test]
    public void ADeepLinkReferenceRoundTripsAndMalformedReferencesAreRefused()
    {
        ApiPaths.TryParseItemReference(Movies, "collection:42", out var parsed).Should().BeTrue();

        using var scope = new FluentAssertions.Execution.AssertionScope();

        parsed.Should().Be(Collection);
        ApiPaths.ItemReference(parsed).Should().Be("collection:42");
        ApiPaths.TryParseItemReference(Movies, "movies:collection:42", out parsed).Should().BeTrue();
        parsed.Should().Be(Collection);
        ApiPaths.TryParseItemReference(Movies, "42", out _).Should().BeFalse();
        ApiPaths.TryParseItemReference(Movies, "collection:0", out _).Should().BeFalse();
        ApiPaths.TryParseItemReference(Movies, "collection:42:extra", out _).Should().BeFalse();
        ApiPaths.TryParseItemReference(Movies, "television:collection:42", out _).Should().BeFalse();
        ApiPaths.TryParseItemReference(Movies, " :42", out _).Should().BeFalse();
        var foreignItem = () => ApiPaths.Item(
            Movies.Value,
            new MediaItemRef(MediaKindId.FromString("television"), Movie.Level, Movie.Id));
        foreignItem.Should().Throw<ArgumentException>();
    }

    [Test]
    public void BrowseQueriesUseRepeatedEqualsDelimitedFiltersAndEscapeWholeValues()
    {
        var request = new ItemBrowseRequest
        {
            Parent = Collection,
            Filters = new Dictionary<string, string>
            {
                ["z:field"] = "a=b & / :",
                ["a field"] = "one=two",
            },
        };

        ApiPaths.LevelItems(Movies.Value, "episode", request.ToQuery()).Should().Be(
            "api/v1/kinds/movies/levels/episode/items?parent=collection%3A42&page=1&size=60"
            + "&filter=a%20field%3Done%3Dtwo&filter=z%3Afield%3Da%3Db%20%26%20%2F%20%3A");
    }

    [Test]
    public async Task CatalogSearchUsesTheGenericWireContractsAndEscapesItsInputs()
    {
        using var fixture = Fixture(static _ => Json(HttpStatusCode.OK, new CatalogItemPage([], 2, 25, 0)));

        var page = await fixture.Client.SearchCatalogAsync(
            Movies,
            "tmdb",
            "space & slash/ = colon:",
            ExternalId.Of("other", "a=b&c/d"),
            page: 2,
            pageSize: 25);

        using var _ = new FluentAssertions.Execution.AssertionScope();

        page.Should().BeEquivalentTo(new CatalogItemPage([], 2, 25, 0));
        fixture.Handler.Requests.Should().ContainSingle().Which.PathAndQuery.Should().Be(
            "/api/v1/kinds/movies/catalog/search?scheme=tmdb&q=space%20%26%20slash%2F%20%3D%20colon%3A"
            + "&id=other%3Aa%3Db%26c%2Fd&page=2&size=25");
    }

    [TestCase(HttpStatusCode.Created, true)]
    [TestCase(HttpStatusCode.OK, false)]
    public async Task CatalogAddAcceptsOnlyCreatedAndIdempotentResponsesWithoutTreatingLocationAsIdentity(
        HttpStatusCode status,
        bool created)
    {
        var view = CatalogView();
        using var fixture = Fixture(_ =>
        {
            var response = Json(status, view);
            response.Headers.Location = new Uri("https://untrusted.example/items/not-the-body");
            return response;
        });

        var added = await fixture.Client.AddCatalogItemAsync(Movies, ExternalId.Of("tmdb", "603"));

        using var _ = new FluentAssertions.Execution.AssertionScope();

        added.Item.Should().BeEquivalentTo(view);
        added.Created.Should().Be(created);
        fixture.Handler.Requests.Should().ContainSingle().Which.Should().Match<Request>(request =>
            request.Method == HttpMethod.Post
            && request.PathAndQuery == "/api/v1/kinds/movies/catalog/items"
            && request.Body == "{\"catalogId\":\"tmdb:603\"}");
    }

    [Test]
    public async Task CatalogAddReadsAFullReferenceFromLiteralServerJson()
    {
        using var fixture = Fixture(static _ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(
                """
                {"item":{"ref":{"kind":"movies","level":"movie","id":42},"title":"The Matrix","fields":{}},"catalogId":{"scheme":"tmdb","value":"603"},"inLibrary":true}
                """,
                Encoding.UTF8,
                "application/json"),
        });

        var added = await fixture.Client.AddCatalogItemAsync(Movies, ExternalId.Of("tmdb", "603"));

        using var _ = new FluentAssertions.Execution.AssertionScope();
        added.Item.Item.Ref.Should().Be(Movie);
        added.Item.CatalogId.Should().Be(ExternalId.Of("tmdb", "603"));
        added.Created.Should().BeTrue();
    }

    [Test]
    public async Task ActionRequestsWriteMediaItemIdentifiersAsNumbers()
    {
        using var fixture = Fixture(static _ => Json(HttpStatusCode.OK, new ActionResult(true, null, null, null)));

        await fixture.Client.InvokeActionAsync(
            Movies,
            "set-monitoring",
            new ActionRequest { Items = [Collection] });

        var request = fixture.Handler.Requests.Should().ContainSingle().Which;
        using var body = JsonDocument.Parse(request.Body!);
        var reference = body.RootElement.GetProperty("items")[0];

        using var _ = new FluentAssertions.Execution.AssertionScope();
        reference.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Number);
        reference.GetProperty("id").GetInt64().Should().Be(42);
    }

    [Test]
    public async Task ProviderDiscoveryReadsLiteralServerEnumAndIdentifierShapes()
    {
        using var fixture = Fixture(request => request.RequestUri!.AbsolutePath.EndsWith("/definitions", StringComparison.Ordinal)
            ? LiteralJson(
                """
                [{"id":7,"provider":"tmdb:movies","family":"cataloger","name":"TMDb","enabled":true,"settings":{},"state":"active"}]
                """)
            : LiteralJson(
                """
                [{"provider":"tmdb:movies","family":"cataloger","descriptor":{"localId":"movies","name":"TMDb","settings":[]},"pairedMediaKind":"movies","catalogScheme":"tmdb"}]
                """));

        var providers = await fixture.Client.GetProvidersAsync(ProviderFamily.Cataloger, Movies);
        var definitions = await fixture.Client.GetProviderDefinitionsAsync();

        using var _ = new FluentAssertions.Execution.AssertionScope();
        providers.Should().ContainSingle().Which.Family.Should().Be(ProviderFamily.Cataloger);
        providers[0].PairedMediaKind.Should().Be(Movies);
        definitions.Should().ContainSingle().Which.Family.Should().Be(ProviderFamily.Cataloger);
        definitions[0].State.Should().Be(DefinitionState.Active);
    }

    [TestCase(HttpStatusCode.Accepted)]
    [TestCase(HttpStatusCode.NoContent)]
    public async Task CatalogAddRefusesUnexpectedSuccessfulStatuses(HttpStatusCode status)
    {
        using var fixture = Fixture(_ => new HttpResponseMessage(status));

        var act = () => fixture.Client.AddCatalogItemAsync(Movies, ExternalId.Of("tmdb", "603"));

        var failure = await act.Should().ThrowAsync<ApiRequestException>();
        failure.Which.StatusCode.Should().Be(status);
    }

    [Test]
    public async Task CatalogRefreshUsesTheCompleteReferenceAndReturnsTheTypedBody()
    {
        var view = CatalogView();
        using var fixture = Fixture(_ => Json(HttpStatusCode.OK, view));

        var refreshed = await fixture.Client.RefreshCatalogItemAsync(Movies, Collection);

        using var _ = new FluentAssertions.Execution.AssertionScope();

        refreshed.Should().BeEquivalentTo(view);
        fixture.Handler.Requests.Should().ContainSingle().Which.PathAndQuery.Should()
            .Be("/api/v1/kinds/movies/catalog/items/collection%3A42/refresh");
    }

    [Test]
    public async Task CatalogResponsesKeepTheExistingTypedErrorBehavior()
    {
        using var fixture = Fixture(static _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("the catalog refused this", Encoding.UTF8, "text/plain"),
        });

        var act = () => fixture.Client.RefreshCatalogItemAsync(Movies, Movie);

        var failure = await act.Should().ThrowAsync<ApiRequestException>();
        failure.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CallerCancellationPropagatesUnchanged()
    {
        using var fixture = Fixture(static async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation must have stopped the request.");
        });
        using var cancellation = new CancellationTokenSource();

        var pending = fixture.Client.SearchCatalogAsync(Movies, "tmdb", "matrix", null, 1, 60, cancellation.Token);
        cancellation.Cancel();

        Func<Task> act = () => pending;
        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Handler.Requests.Should().ContainSingle();
        fixture.Connectivity.State.Should().Be(HostState.Unknown);
    }

    [Test]
    public async Task CatalogAddPassesCallerCancellationThroughUnchanged()
    {
        using var fixture = Fixture(static async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation must have stopped the request.");
        });
        using var cancellation = new CancellationTokenSource();

        var pending = fixture.Client.AddCatalogItemAsync(Movies, ExternalId.Of("tmdb", "603"), cancellation.Token);
        cancellation.Cancel();

        Func<Task> act = async () => await pending;
        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Handler.Requests.Should().ContainSingle();
        fixture.Connectivity.State.Should().Be(HostState.Unknown);
    }

    private static CatalogItemView CatalogView() => new()
    {
        Item = new ItemView
        {
            Ref = Movie,
            Title = "The Matrix",
            Fields = new Dictionary<string, FieldValue>(),
        },
        CatalogId = ExternalId.Of("tmdb", "603"),
        InLibrary = true,
    };

    private static HttpResponseMessage Json<T>(HttpStatusCode status, T value) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(value, ApiJsonOptions.Default),
            Encoding.UTF8,
            "application/json"),
    };

    private static HttpResponseMessage LiteralJson(string value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(value, Encoding.UTF8, "application/json"),
    };

    private static ClientFixture Fixture(Func<HttpRequestMessage, HttpResponseMessage> answer)
        => Fixture((request, _) => Task.FromResult(answer(request)));

    private static ClientFixture Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
    {
        var handler = new RecordingHandler(answer);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://host.invalid/") };
        var connectivity = new HostConnectivity(http, new ClientOptions { ServerAddress = http.BaseAddress });
        return new ClientFixture(http, connectivity, handler, new ArronixApiClient(http, connectivity));
    }

    private sealed class ClientFixture : IDisposable
    {
        private readonly HttpClient _http;

        public ClientFixture(
            HttpClient http,
            HostConnectivity connectivity,
            RecordingHandler handler,
            ArronixApiClient client)
        {
            _http = http;
            Connectivity = connectivity;
            Handler = handler;
            Client = client;
        }

        public ArronixApiClient Client { get; }

        public RecordingHandler Handler { get; }

        public HostConnectivity Connectivity { get; }

        public void Dispose()
        {
            Connectivity.Dispose();
            _http.Dispose();
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            Requests.Add(new Request(request.Method, request.RequestUri!.PathAndQuery, body, cancellationToken));
            return await answer(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record Request(HttpMethod Method, string PathAndQuery, string? Body, CancellationToken CancellationToken);
}
