using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Metadata;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

// The http, plugin, shape, providers and definition contracts are experimental.
#pragma warning disable ARX0008
#pragma warning disable ARX0013
#pragma warning disable ARX0014
#pragma warning disable ARX0015
#pragma warning disable ARX0019

namespace Arronix.Host.Tests.Engines;

/// <summary>
/// The cataloger seam end to end, with the HTTP call behind a recording gateway fake — the property the
/// design pays for: declared mapping logic fully testable without a socket.
/// </summary>
[TestFixture]
internal sealed class MetadataEngineCatalogerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 47, 0, TimeSpan.Zero);

    private static ProviderInvocation Invocation(params (string Key, string Value)[] settings) => new(
        new ProviderDefinition
        {
            Id = 1,
            Provider = ProviderId.Create(PluginId.FromString("fixture"), "catalog"),
            Family = ProviderFamily.Cataloger,
            Name = "Fixture catalog",
            Settings = settings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        },
        new NullSessionStore(),
        "test-correlation");

    private static DeclarativeCatalogMapper Mapper(FakeGateway gateway) => new(
        ProviderId.Create(PluginId.FromString("fixture"), "catalog"),
        NamingEngineTestSupport.Shape(),
        MetadataEngineMappingTests.Declaration(),
        gateway,
        new FakeTimeProvider(Now));

    [Test]
    public void CapabilitiesDeriveFromTheDeclaration()
        => Mapper(new FakeGateway("{}")).Capabilities
            .Should().Be(CatalogerCapabilities.Search | CatalogerCapabilities.DeltaSync);

    [Test]
    public async Task TestValidatesTheEndpointWithoutTouchingTheNetwork()
    {
        var gateway = new FakeGateway("{}");

        (await Mapper(gateway).TestAsync(Invocation())).IsValid.Should().BeTrue();
        (await Mapper(gateway).TestAsync(Invocation(("baseUrl", "not a url")))).IsValid.Should().BeFalse();
        gateway.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task GetResolvesTheRequestWhosePlaceholderSpellsTheScheme()
    {
        var gateway = new FakeGateway("""{"id":603,"title":"The Fixture","year":2020,"extId":"816692"}""");
        var graph = await Mapper(gateway).GetAsync(
            Invocation(), ExternalId.Of("cat", "603"), SelectionPolicy.None);

        gateway.Requests.Should().ContainSingle()
            .Which.Url.ToString().Should().Be("https://catalog.example/api/thing/603");

        var node = graph!.Nodes.Single();
        node.Title.Should().Be("The Fixture");
        node.Id.Should().Be(ExternalId.Of("cat", "603"));

        // The ext-id converter ran the declared prefix-pad rule.
        node.Fields["extId"].External!.Value.Value.Should().Be("tt0816692");
    }

    [Test]
    public async Task GetByTheSecondSchemeTakesTheOtherRouteAndNormalizesTheValue()
    {
        var gateway = new FakeGateway("""{"id":603,"title":"The Fixture"}""");
        await Mapper(gateway).GetAsync(Invocation(), ExternalId.Of("ext", "816692"), SelectionPolicy.None);

        gateway.Requests.Should().ContainSingle()
            .Which.Url.ToString().Should().Be("https://catalog.example/api/thing/ext/tt0816692");
    }

    [Test]
    public async Task AMissingSubjectIsNullNotAnError()
    {
        var gateway = new FakeGateway("{}", HttpStatusCode.NotFound);

        (await Mapper(gateway).GetAsync(Invocation(), ExternalId.Of("cat", "1"), SelectionPolicy.None))
            .Should().BeNull();
    }

    [Test]
    public async Task SearchSplitsATrailingYearIntoItsOwnParameter()
    {
        var gateway = new FakeGateway("""[{"id":1,"title":"Arrival","year":2016}]""");
        var hits = await Mapper(gateway).SearchAsync(
            Invocation(),
            new MetadataQuery(NamingEngineTestSupport.Kind, null, "Arrival 2016", null));

        gateway.Requests.Should().ContainSingle()
            .Which.Url.Query.Should().Be("?q=Arrival&year=2016");

        hits.Should().ContainSingle();
        hits[0].Title.Should().Be("Arrival");
        hits[0].Year.Should().Be(2016);
    }

    [Test]
    public async Task SearchTextSpellsSpacesAsPluses()
    {
        var gateway = new FakeGateway("[]");
        await Mapper(gateway).SearchAsync(
            Invocation(),
            new MetadataQuery(NamingEngineTestSupport.Kind, null, "the fixture show", null));

        gateway.Requests.Single().Url.Query.Should().Be("?q=the+fixture+show");
    }

    [Test]
    public async Task ARecognizedIdentifierResolvesInsteadOfSearching()
    {
        var gateway = new FakeGateway("""{"id":603,"title":"The Fixture","year":2020}""");
        var hits = await Mapper(gateway).SearchAsync(
            Invocation(),
            new MetadataQuery(NamingEngineTestSupport.Kind, null, "ext:816692", null));

        gateway.Requests.Single().Url.AbsolutePath.Should().EndWith("/thing/ext/tt0816692");
        hits.Should().ContainSingle().Which.Title.Should().Be("The Fixture");
    }

    [Test]
    public async Task ChangedSinceBacksOffAndFloorsToTheDeclaredBoundary()
    {
        var gateway = new FakeGateway("[603,604]");
        var ids = await Mapper(gateway).ChangedSinceAsync(
            Invocation(), new DateTimeOffset(2026, 8, 17, 12, 10, 0, TimeSpan.Zero));

        // 12:10 − 15 minutes = 11:55, floored to the hour = 11:00.
        gateway.Requests.Single().Url.Query.Should().Be("?since=2026-08-17T11%3A00%3A00Z");
        ids.Select(id => id.Value).Should().Equal("603", "604");
    }

    private sealed class FakeGateway : IHttpGateway
    {
        private readonly string _content;
        private readonly HttpStatusCode _status;

        public FakeGateway(string content, HttpStatusCode status = HttpStatusCode.OK)
        {
            _content = content;
            _status = status;
        }

        public List<OutboundHttpRequest> Requests { get; } = [];

        public Task<OutboundHttpResponse> ExecuteAsync(
            OutboundHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return Task.FromResult(new OutboundHttpResponse(
                request, new HttpHeaderCollection(), _status, Encoding.UTF8.GetBytes(_content)));
        }

        public Task<OutboundHttpResponse<TResource>> ExecuteAsync<TResource>(
            OutboundHttpRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The mapper decodes documents itself.");

        public Task<OutboundHttpResponse> DownloadAsync(
            OutboundHttpRequest request,
            Stream destination,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The mapper never downloads.");
    }

    private sealed class NullSessionStore : IProviderSessionStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task SetAsync(
            string key,
            string? value,
            TimeSpan? lifetime = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
