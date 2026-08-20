using System.IO;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Scoping;


namespace Arronix.Plugins.Tests.Scoping;

/// <summary>
/// Attribution, throttling and confinement of an extension's outbound calls.
/// </summary>
[TestFixture]
public sealed class ScopedHttpGatewayTests
{
    private static readonly PluginId Plugin = PluginId.FromString("test.network");

    private static (ScopedHttpGateway Gateway, RecordingGateway Inner) Create(
        IReadOnlyList<string>? allowed = null,
        IReadOnlyList<string>? denied = null)
    {
        var inner = new RecordingGateway();
        return (new ScopedHttpGateway(inner, Plugin, allowed, denied), inner);
    }

    [Test]
    public async Task TheThrottlingPartitionIsFilledInWhenTheCallerLeftItUnset()
    {
        var (gateway, inner) = Create();

        await gateway.ExecuteAsync(new OutboundHttpRequest(new Uri("https://indexer.example/api")));

        inner.Last!.RateLimitKey.Should().Be($"{Plugin}|indexer.example");
    }

    [Test]
    public async Task ACallerThatPartitionedItsOwnThrottlingIsLeftAlone()
    {
        var (gateway, inner) = Create();
        var request = new OutboundHttpRequest(new Uri("https://indexer.example/api")) { RateLimitKey = "mine" };

        await gateway.ExecuteAsync(request);

        inner.Last!.RateLimitKey.Should().Be(
            "mine",
            "an extension that partitions deliberately knows better than the decorator does");
    }

    [Test]
    public async Task TheExtensionIsNamedInTheUserAgent()
    {
        var (gateway, inner) = Create();

        await gateway.ExecuteAsync(new OutboundHttpRequest(new Uri("https://indexer.example/api")));

        inner.Last!.Headers.UserAgent.Should().Be($"(+{Plugin})");
    }

    [Test]
    public async Task AnExistingUserAgentIsAppendedToRatherThanReplaced()
    {
        var (gateway, inner) = Create();
        var request = new OutboundHttpRequest(new Uri("https://indexer.example/api"));
        request.Headers.UserAgent = "Example/1.0";

        await gateway.ExecuteAsync(request);

        inner.Last!.Headers.UserAgent.Should().Be($"Example/1.0 (+{Plugin})");
    }

    [Test]
    public async Task TheAttributionIsNotAppendedTwice()
    {
        var (gateway, inner) = Create();
        var request = new OutboundHttpRequest(new Uri("https://indexer.example/api"));

        await gateway.ExecuteAsync(request);
        await gateway.ExecuteAsync(request);

        inner.Last!.Headers.UserAgent.Should().Be($"(+{Plugin})");
    }

    [Test]
    public void AnEmptyAllowListPermitsAnythingNotDenied()
    {
        var (gateway, _) = Create();

        gateway.IsHostPermitted("anywhere.example").Should().BeTrue();
    }

    [Test]
    public void ADenialWinsOverAnAllowance()
    {
        var (gateway, _) = Create(["indexer.example"], ["indexer.example"]);

        gateway.IsHostPermitted("indexer.example").Should().BeFalse();
    }

    [TestCase("indexer.example", true)]
    [TestCase("INDEXER.EXAMPLE", true)]
    [TestCase("other.example", false)]
    [TestCase("evil-indexer.example", false)]
    public void AnAllowListAdmitsOnlyWhatItNames(string host, bool expected)
        => Create(["indexer.example"]).Gateway.IsHostPermitted(host).Should().Be(expected);

    [TestCase("example.com", true)]
    [TestCase("api.example.com", true)]
    [TestCase("deep.api.example.com", true)]
    [TestCase("notexample.com", false)]
    public void ADotPrefixedPatternCoversADomainAndEverythingBeneathIt(string host, bool expected)
        => Create([".example.com"]).Gateway.IsHostPermitted(host).Should().Be(expected);

    [Test]
    public async Task ARefusedDestinationFailsAsTheGatewaysOwnFailure()
    {
        var (gateway, inner) = Create(["indexer.example"]);

        var call = async () => await gateway.ExecuteAsync(new OutboundHttpRequest(new Uri("https://other.example/")));

        await call.Should().ThrowAsync<HttpGatewayException>();
        inner.Last.Should().BeNull("a refused call never reaches the network");
    }

    [Test]
    public void ANullRequestIsRefused()
    {
        var (gateway, _) = Create();

        var call = async () => await gateway.ExecuteAsync(null!);

        call.Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public void AMissingInnerGatewayIsRefusedAtConstruction()
    {
        var construct = () => new ScopedHttpGateway(null!, Plugin);

        construct.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Keeps the last request it was handed, which is the whole of what the decorator changes.
    /// </summary>
    private sealed class RecordingGateway : IHttpGateway
    {
        public OutboundHttpRequest? Last { get; private set; }

        public Task<OutboundHttpResponse> ExecuteAsync(
            OutboundHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            Last = request;
            return Task.FromResult<OutboundHttpResponse>(null!);
        }

        public Task<OutboundHttpResponse<TResource>> ExecuteAsync<TResource>(
            OutboundHttpRequest request,
            CancellationToken cancellationToken = default)
        {
            Last = request;
            return Task.FromResult<OutboundHttpResponse<TResource>>(null!);
        }

        public Task<OutboundHttpResponse> DownloadAsync(
            OutboundHttpRequest request,
            Stream destination,
            CancellationToken cancellationToken = default)
        {
            Last = request;
            return Task.FromResult<OutboundHttpResponse>(null!);
        }
    }
}
