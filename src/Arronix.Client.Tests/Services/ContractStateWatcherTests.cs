using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Wire;
using Arronix.Client.Configuration;
using Arronix.Client.Contracts;
using Arronix.Client.Serialization;
using Arronix.Client.Services;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.JSInterop;

namespace Arronix.Client.Tests.Services;

/// <summary>
/// What makes a connected tab re-read the installation, and what must not.
/// </summary>
/// <remarks>
/// A browser cannot unload an assembly, so a tab that learns of a withdrawal late goes on serving a
/// contract this host does not admit. Re-reading has to follow the one event that can change what is
/// installed — and only that one, because re-reading on item, job, queue, health or provider traffic would
/// be a poll wearing a subscription's clothes.
/// </remarks>
[TestFixture]
internal sealed class ContractStateWatcherTests
{
    /// <summary>Only an extension-state event drives a re-read, and disposing stops it.</summary>
    /// <remarks>
    /// Every count is taken after an ordinary <c>LoadAsync</c>, which queues behind whatever the handler
    /// already started on the loader's own gate. That makes the count a barrier rather than a race: no
    /// waiting, no sleeping, and an event that wrongly triggered a read would have completed before it.
    /// </remarks>
    [Test]
    public async Task OnlyAnExtensionStateChangeMakesTheLoaderReadTheInstallationAgain()
    {
        var host = new CountingHost();
        using var http = host.Connect();
        var loader = new MediaContractLoader(http, new ContractStore(new RefusingJsRuntime()));

        var options = new ClientOptions { ServerAddress = new Uri("https://host.invalid/") };
        await using var events = new EventStream(options, new HostConnectivity(http, options));
        var watcher = new ContractStateWatcher(loader, events);

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

        await loader.LoadAsync();
        host.ManifestReads.Should().Be(1, "nothing but an extension changing state can change what is installed");

        events.Publish(Envelope(EventKind.PluginStateChanged));

        await loader.LoadAsync();
        host.ManifestReads.Should().Be(3, "the state change drove one read, and this call is the other");

        watcher.Dispose();
        events.Publish(Envelope(EventKind.PluginStateChanged));

        await loader.LoadAsync();

        using var assertions = new AssertionScope();

        host.ManifestReads.Should().Be(4, "a disposed watcher is unsubscribed, so only this call read");
        host.ByteRequests.Should().Be(0, "an installation offering nothing has nothing to fetch");
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

    private sealed class RefusingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException("This test process has no browser.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
            => throw new NotSupportedException("This test process has no browser.");
    }
}
