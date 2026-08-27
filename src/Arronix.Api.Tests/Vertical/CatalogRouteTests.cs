using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Host.Providers;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Arronix.Api.Tests.Vertical;

/// <summary>
/// What the catalog routes answer: which refusals are the caller's fault, which are the installation's,
/// which are the catalog's, and what an add actually leaves behind.
/// </summary>
[TestFixture]
internal sealed class CatalogRouteTests
{
    private const string Search = "/api/v1/kinds/movies/catalog/search";
    private const string Items = "/api/v1/kinds/movies/catalog/items";

    private CatalogServer _server = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void StartServer()
    {
        _server = new CatalogServer();
        _client = _server.CreateClient();
    }

    [TearDown]
    public void StopServer()
    {
        _client.Dispose();
        _server.Dispose();
    }

    /// <summary>
    /// A scheme the platform cannot route by is refused before anything is dispatched.
    /// </summary>
    /// <remarks>
    /// The separator case is the one that matters most: an identifier is written and read back as
    /// <c>scheme:value</c>, so a scheme containing one would not survive the round trip it is written for.
    /// </remarks>
    [TestCase("TMDb", TestName = "ASchemeWithACapitalIsRefused")]
    [TestCase("tm db", TestName = "ASchemeWithWhiteSpaceIsRefused")]
    [TestCase("tm:db", TestName = "ASchemeCarryingTheSeparatorIsRefused")]
    public async Task ANonCanonicalSchemeIsABadRequest(string scheme)
    {
        var response = await _client.GetAsync(new Uri($"{Search}?scheme={Uri.EscapeDataString(scheme)}&q=matrix", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>An identifier that is not <c>scheme:value</c> never reaches a catalog.</summary>
    [TestCase("TMDb:603", TestName = "AnIdentifierWithACapitalSchemeIsRefused")]
    [TestCase("tm db:603", TestName = "AnIdentifierWithAWhiteSpaceSchemeIsRefused")]
    [TestCase("603", TestName = "AnIdentifierWithNoSchemeIsRefused")]
    [TestCase("tmdb:", TestName = "AnIdentifierWithNoValueIsRefused")]
    [TestCase(":603", TestName = "AnIdentifierWithAnEmptySchemeIsRefused")]
    public async Task AMalformedCatalogIdentifierIsABadRequest(string catalogId)
    {
        var response = await _client.PostAsJsonAsync(Items, new { catalogId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// An identity the host could never have issued is a malformed address, not a missing item.
    /// </summary>
    /// <remarks>
    /// Identities are issued from one upward. Reading zero or a negative number as an item that happens not
    /// to exist would tell a caller their reference was well formed and the item was gone.
    /// </remarks>
    [TestCase("movie:0", TestName = "AnItemReferenceOfZeroIsRefused")]
    [TestCase("movie:-1", TestName = "ANegativeItemReferenceIsRefused")]
    public async Task AnItemReferenceTheHostCouldNotHaveIssuedIsABadRequest(string reference)
    {
        _server.Configure();

        var response = await _client.PostAsync(
            new Uri($"/api/v1/kinds/movies/catalog/items/{Uri.EscapeDataString(reference)}/refresh", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Bounds this server will not serve are refused, not quietly replaced with ones it will.</summary>
    [TestCase("page=0", TestName = "APageBelowOneIsRefused")]
    [TestCase("page=nonsense", TestName = "APageThatIsNotANumberIsRefused")]
    [TestCase("size=0", TestName = "APageSizeBelowOneIsRefused")]
    [TestCase("size=100000", TestName = "APageSizeBeyondWhatTheServerServesIsRefused")]
    public async Task BoundsTheServerWillNotServeAreABadRequest(string bounds)
    {
        _server.Configure();

        var response = await _client.GetAsync(new Uri($"{Search}?scheme=tmdb&q=matrix&{bounds}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// No configured cataloger owns the scheme, which is something an operator fixes, not the caller.
    /// </summary>
    [Test]
    public async Task ASchemeNoConfiguredCatalogerOwnsIsUnavailableRatherThanBad()
    {
        var response = await _client.GetAsync(new Uri($"{Search}?scheme=tmdb&q=matrix", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>A catalog that fails is not a malformed request.</summary>
    [Test]
    public async Task ACatalogerThatThrowsIsABadGateway()
    {
        _server.Configure();
        _server.Gateway.Throw = true;

        var response = await _client.GetAsync(new Uri($"{Search}?scheme=tmdb&q=matrix", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    /// <summary>Nor is a catalog that simply has nothing to say about a record it used to hold.</summary>
    [Test]
    public async Task ARefreshNoCatalogAnswersIsABadGateway()
    {
        _server.Configure();
        var added = await AddAsync("tmdb:603");
        var reference = Reference(added);

        _server.Gateway.Missing = true;

        var response = await _client.PostAsync(
            new Uri($"/api/v1/kinds/movies/catalog/items/{reference}/refresh", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    /// <summary>
    /// The first add creates; repeating it does not, and the two describe the same item.
    /// </summary>
    [Test]
    public async Task TheFirstAddIsCreatedAndARepeatIsNot()
    {
        _server.Configure();

        var first = await _client.PostAsJsonAsync(Items, new { catalogId = "tmdb:603" });
        var firstBody = await Read(first);

        var again = await _client.PostAsJsonAsync(Items, new { catalogId = "tmdb:603" });
        var againBody = await Read(again);

        using var assertions = new AssertionScope();

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        first.Headers.Location.Should().NotBeNull("a created item says where it now lives");
        again.StatusCode.Should().Be(HttpStatusCode.OK, "a repeat created nothing");

        Reference(againBody).Should().Be(
            Reference(firstBody),
            "and the same catalog identifier is the same item");

        firstBody.RootElement.GetProperty("inLibrary").GetBoolean().Should().BeTrue();
    }

    /// <summary>What was added is what the ordinary item routes publish.</summary>
    [Test]
    public async Task AnAddedMovieIsPublishedByTheOrdinaryItemRoutes()
    {
        _server.Configure();
        var added = await AddAsync("tmdb:603");
        var reference = Reference(added);

        using var page = await Read(await _client.GetAsync(
            new Uri("/api/v1/kinds/movies/levels/movie/items", UriKind.Relative)));

        using var one = await Read(await _client.GetAsync(
            new Uri($"/api/v1/kinds/movies/items/{reference}", UriKind.Relative)));

        var items = page.RootElement.GetProperty("items");

        using var assertions = new AssertionScope();

        items.GetArrayLength().Should().Be(1, "one add is one library item");
        items[0].GetProperty("item").GetProperty("title").GetString().Should().Be("The Matrix");
        one.RootElement.GetProperty("item").GetProperty("title").GetString().Should().Be("The Matrix");
        one.RootElement.GetProperty("item").GetProperty("fields").GetProperty("year")
            .GetProperty("number").GetInt64().Should().Be(1999, "the typed facts came back");
    }

    private async Task<JsonDocument> AddAsync(string catalogId)
        => await Read(await _client.PostAsJsonAsync(Items, new { catalogId }));

    private static async Task<JsonDocument> Read(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    /// <summary>The <c>level:id</c> form the item routes address an item by.</summary>
    private static string Reference(JsonDocument body)
    {
        var reference = body.RootElement.GetProperty("item").GetProperty("ref");
        return $"{reference.GetProperty("level").GetString()}:{reference.GetProperty("id").GetInt64()}";
    }

    /// <summary>The server with a cataloger installed beside movies, and a transport it does not reach.</summary>
    private sealed class CatalogServer : WebApplicationFactory<Program>
    {
        private readonly string _state = Path.Combine(
            Path.GetTempPath(),
            "arronix-api-catalog",
            Guid.NewGuid().ToString("N"));

        internal ScriptedGateway Gateway { get; } = new();

        /// <summary>Configures the installed cataloger, as an operator would.</summary>
        internal void Configure()
        {
            var providers = Services.GetRequiredService<ProviderRegistry>();
            var cataloger = providers.All.Single(registration =>
                registration.Family == ProviderFamily.Cataloger
                && registration.Plugin == PluginId.FromString("tmdb"));

            Services.GetRequiredService<ProviderDefinitionStore>().AddAsync(new ProviderDefinition
            {
                Id = 0,
                Provider = cataloger.Id,
                Family = ProviderFamily.Cataloger,
                Name = "Catalog",
                Settings = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["readAccessToken"] = "route-proof-token",
                },
                MediaKinds = [MediaKindId.FromString("movies")],
            }).GetAwaiter().GetResult();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var installed = Path.Combine(AppContext.BaseDirectory, "CatalogPlugins");

            builder.UseSetting("Arronix:Plugins:RootFolder", installed);
            builder.UseSetting("Arronix:Host:ExtensionFolder", installed);
            builder.UseSetting("Arronix:Plugins:StateFolder", Path.Combine(_state, "state"));
            builder.UseSetting("Arronix:Store:DataSource", Path.Combine(_state, "arronix.db"));
            builder.UseSetting("Arronix:Library:RootFolders:0", Path.Combine(_state, "library"));
            builder.UseSetting("Arronix:Api:PublishApiDescription", "false");

            builder.ConfigureServices(services => services.AddSingleton<IHttpGateway>(Gateway));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing && Directory.Exists(_state))
            {
                Directory.Delete(_state, recursive: true);
            }
        }
    }

    /// <summary>A transport that answers from a script, or refuses the way a real one refuses.</summary>
    private sealed class ScriptedGateway : IHttpGateway
    {
        private const string Film = """
            { "id": 603, "title": "The Matrix", "release_date": "1999-03-30", "runtime": 136 }
            """;

        private const string Page = """
            { "page": 1, "results": [{ "id": 603, "title": "The Matrix" }], "total_pages": 1, "total_results": 1 }
            """;

        internal bool Throw { get; set; }

        internal bool Missing { get; set; }

        public Task<OutboundHttpResponse> ExecuteAsync(
            OutboundHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Throw)
            {
                throw new HttpRequestException("The catalog is unreachable.");
            }

            if (Missing)
            {
                return Task.FromResult(new OutboundHttpResponse(
                    request,
                    new HttpHeaderCollection(),
                    HttpStatusCode.NotFound,
                    Encoding.UTF8.GetBytes("{}")));
            }

            var body = request.Url.AbsolutePath.Contains("/search/", StringComparison.Ordinal) ? Page : Film;

            return Task.FromResult(new OutboundHttpResponse(
                request,
                new HttpHeaderCollection(),
                HttpStatusCode.OK,
                Encoding.UTF8.GetBytes(body)));
        }

        public Task<OutboundHttpResponse<TResource>> ExecuteAsync<TResource>(
            OutboundHttpRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OutboundHttpResponse> DownloadAsync(
            OutboundHttpRequest request,
            Stream destination,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
