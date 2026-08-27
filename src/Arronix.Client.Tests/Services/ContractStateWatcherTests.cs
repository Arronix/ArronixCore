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
/// What makes a connected tab re-read the installation, what that re-read does, and what must not trigger it.
/// </summary>
/// <remarks>
/// A tab that learns of a withdrawal late goes on serving a contract this host does not admit, and a tab
/// that re-reads on unrelated traffic is polling. Both are failures, in opposite directions.
/// </remarks>
[TestFixture]
internal sealed class ContractStateWatcherTests
{
    private const string Dead = "CCCC000000000000000000000000000000000000000000000000000000000003";

    /// <summary>Only an extension-state event drives a re-read, it sheds bytes, and disposing stops it.</summary>
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
        var store = new InMemoryContractStore(Dead);
        var loader = new MediaContractLoader(http, store.Open());

        var options = new ClientOptions { ServerAddress = new Uri("https://host.invalid/") };
        await using var events = new EventStream(options, new HostConnectivity(http, options));
        var watcher = new ContractStateWatcher(loader, new ContractStoreJanitor(store.Open()), events);

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

        using (new AssertionScope())
        {
            host.ManifestReads.Should().Be(1, "nothing but an extension changing state can change what is installed");
            store.Keys.Should().Equal(new[] { Dead }, "no re-check ran, so nothing was swept");
        }

        events.Publish(Envelope(EventKind.PluginStateChanged));

        await loader.LoadAsync();

        using (new AssertionScope())
        {
            host.ManifestReads.Should().Be(3, "the state change drove one read, and this call is the other");
            store.Keys.Should().BeEmpty(
                "the one path that runs with nobody present must still shed an address this host does not name");
            watcher.LastFailure.Should().BeNull();
        }

        watcher.Dispose();
        events.Publish(Envelope(EventKind.PluginStateChanged));

        await loader.LoadAsync();

        using var assertions = new AssertionScope();

        host.ManifestReads.Should().Be(4, "a disposed watcher is unsubscribed, so only this call read");
        host.ByteRequests.Should().Be(0, "an installation offering nothing has nothing to fetch");
    }

    /// <summary>
    /// A re-check that fails is contained and stated, never contained quietly.
    /// </summary>
    /// <remarks>
    /// What reaches the watcher's boundary is a defect, because the loader names every outcome it can
    /// describe. Here a consumer's own handler throws; nothing awaits an event handler, so a failure
    /// swallowed there would have nowhere to be observed at all.
    /// </remarks>
    [Test]
    public async Task AFailedRecheckIsContainedAndStated()
    {
        var host = new CountingHost();
        using var http = host.Connect();
        var store = new InMemoryContractStore();
        var loader = new MediaContractLoader(http, store.Open());

        var options = new ClientOptions { ServerAddress = new Uri("https://host.invalid/") };
        await using var events = new EventStream(options, new HostConnectivity(http, options));
        using var watcher = new ContractStateWatcher(loader, new ContractStoreJanitor(store.Open()), events);

        var stated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Rechecked += (_, _) => stated.TrySetResult();
        loader.ReportChanged += Refuse;

        events.Publish(Envelope(EventKind.PluginStateChanged));

        await stated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var assertions = new AssertionScope();

        watcher.LastFailure.Should().Contain("a consumer refused the report");
        loader.Report.Should().NotBeNull("the load itself succeeded; only the notification failed");

        loader.ReportChanged -= Refuse;

        static void Refuse(object? sender, EventArgs e)
            => throw new InvalidOperationException("a consumer refused the report");
    }

    /// <summary>
    /// A consumer told the re-check is done sees the store the re-check left behind.
    /// </summary>
    /// <remarks>
    /// The loader announces its own load, which happens before the sweep. A page refreshing on that alone
    /// would read the evicted address as still held, and no later notification would correct it.
    /// </remarks>
    [Test]
    public async Task AnAutomaticReloadSweepsBeforeItSaysItIsDone()
    {
        var host = new CountingHost();
        using var http = host.Connect();
        var store = new InMemoryContractStore(Dead);
        var loader = new MediaContractLoader(http, store.Open());

        var options = new ClientOptions { ServerAddress = new Uri("https://host.invalid/") };
        await using var events = new EventStream(options, new HostConnectivity(http, options));
        using var watcher = new ContractStateWatcher(loader, new ContractStoreJanitor(store.Open()), events);

        // What a consumer would render, read at each notification it is offered.
        var onLoad = new List<string[]>();
        var onRecheck = new List<string[]>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        loader.ReportChanged += (_, _) => onLoad.Add([.. store.Keys]);
        watcher.Rechecked += (_, _) =>
        {
            onRecheck.Add([.. store.Keys]);
            finished.TrySetResult();
        };

        events.Publish(Envelope(EventKind.PluginStateChanged));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var assertions = new AssertionScope();

        onLoad.Should().ContainSingle().Which.Should().Equal(
            new[] { Dead },
            "the load finishes before the sweep, so this notification cannot be the last word");
        onRecheck.Should().ContainSingle().Which.Should().BeEmpty(
            "a consumer told the re-check is done must not still be showing an evicted address");
        watcher.LastFailure.Should().BeNull();
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
