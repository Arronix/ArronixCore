using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Wire;
using Arronix.Client.Configuration;
using Arronix.Client.Contracts;
using Arronix.Client.Serialization;
using Arronix.Client.Services;
using Arronix.Client.Tests.Contracts;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Services;

/// <summary>
/// Which events make a connected tab reload the installation, and which must not.
/// </summary>
/// <remarks>
/// A tab that learns of a withdrawal late goes on serving a contract this host does not admit, and a tab
/// that reloads on unrelated traffic is polling. Both are failures, in opposite directions. What a reload
/// then does, and in what order, belongs to the reloader every caller shares.
/// </remarks>
[TestFixture]
internal sealed class ContractStateWatcherTests
{
    /// <summary>Only an extension-state event reloads, and disposing stops even that.</summary>
    /// <remarks>
    /// Every count is taken after an ordinary reload, which queues behind whatever the handler already
    /// started on the reloader's own gate. That makes the count a barrier rather than a race: no waiting,
    /// no sleeping, and an event that wrongly triggered a reload would have completed before it.
    /// </remarks>
    [Test]
    public async Task OnlyAnExtensionStateChangeReloadsTheInstallation()
    {
        var host = new CountingHost();
        using var http = host.Connect();
        var store = new InMemoryContractStore().Open();

        var reloader = new ContractReloader(
            new MediaContractLoader(http, store),
            new ContractStoreJanitor(store));

        var options = new ClientOptions { ServerAddress = new Uri("https://host.invalid/") };
        await using var events = new EventStream(options, new HostConnectivity(http, options));
        var watcher = new ContractStateWatcher(reloader, events);

        foreach (var kind in new[]
                 {
                     EventKind.ItemChanged,
                     EventKind.JobProgress,
                     EventKind.QueueChanged,
                     EventKind.HealthChanged,
                     EventKind.ProviderStatusChanged,
                 })
        {
            events.Publish(Envelope(kind));
        }

        await reloader.ReloadAsync();
        host.ManifestReads.Should().Be(1, "nothing but an extension changing state can change what is installed");

        events.Publish(Envelope(EventKind.PluginStateChanged));

        await reloader.ReloadAsync();
        host.ManifestReads.Should().Be(3, "the state change drove one reload, and this call is the other");

        watcher.Dispose();
        events.Publish(Envelope(EventKind.PluginStateChanged));

        await reloader.ReloadAsync();

        using var assertions = new AssertionScope();

        host.ManifestReads.Should().Be(4, "a disposed watcher is unsubscribed, so only this call read");
        host.ByteRequests.Should().Be(0, "an installation offering nothing has nothing to fetch");
        reloader.LastFailure.Should().BeNull();
    }

    private static EventEnvelope Envelope(EventKind kind)
        => new() { Kind = kind, At = DateTimeOffset.UnixEpoch };

    /// <summary>A host offering nothing, so a read costs one manifest and no bytes.</summary>
    private sealed class CountingHost
    {
        private static readonly ClientContractManifest Empty = new(
            MediaContractLoader.ClientContractIdentity,
            new string('B', 64),
            [],
            []);

        public int ManifestReads { get; private set; }

        public int ByteRequests { get; private set; }

        public HttpClient Connect()
            => new(new StubHandler(Answer)) { BaseAddress = new Uri("https://host.invalid/") };

        private HttpResponseMessage Answer(string path)
        {
            if (!path.EndsWith("client-contracts", StringComparison.Ordinal))
            {
                ByteRequests++;
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            ManifestReads++;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(Empty, ApiJsonOptions.Default),
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    private sealed class StubHandler(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(answer(request.RequestUri!.AbsolutePath));
    }
}
