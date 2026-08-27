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
/// then does, and in what order, belongs to the one view every caller shares.
/// </remarks>
[TestFixture]
internal sealed class ContractStateWatcherTests
{
    /// <summary>Only an extension-state event reloads, and disposing stops even that.</summary>
    /// <remarks>
    /// Every count is taken after an ordinary reload, which queues behind whatever the handler already
    /// started on the one lease. That makes the count a barrier rather than a race: no waiting, no
    /// sleeping, and an event that wrongly triggered a reload would have completed before it.
    /// </remarks>
    [Test]
    public async Task OnlyAnExtensionStateChangeReloadsTheInstallation()
    {
        var host = new CountingHost();
        using var http = host.Connect();
        var view = View(http, new InMemoryContractStore());

        var options = new ClientOptions { ServerAddress = new Uri("https://host.invalid/") };
        await using var events = new EventStream(options, new HostConnectivity(http, options));
        var watcher = new ContractStateWatcher(view, events);

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

        await view.ReloadAsync();
        host.ManifestReads.Should().Be(1, "nothing but an extension changing state can change what is installed");

        events.Publish(Envelope(EventKind.PluginStateChanged));

        await view.ReloadAsync();
        host.ManifestReads.Should().Be(3, "the state change drove one reload, and this call is the other");

        watcher.Dispose();
        events.Publish(Envelope(EventKind.PluginStateChanged));

        await view.ReloadAsync();

        using var assertions = new AssertionScope();

        host.ManifestReads.Should().Be(4, "a disposed watcher is unsubscribed, so only this call read");
        host.ByteRequests.Should().Be(0, "an installation offering nothing has nothing to fetch");
        view.Snapshot.Failures.Should().BeEmpty();
    }

    /// <summary>A fatal failure from an event-driven reload is observed, not dropped with the task.</summary>
    /// <remarks>
    /// An event handler has no caller to return anything to. The transaction records every failure it
    /// contains, so what a discarded task would take with it is the one class this client contains nowhere:
    /// an exhausted heap, a corrupted memory failure, a structured native failure.
    /// </remarks>
    [Test]
    public async Task AFatalFailureFromAnEventDrivenReloadIsObservedRatherThanDropped()
    {
        var host = new CountingHost();
        using var http = host.Connect();

        var browser = new InMemoryContractStore();
        var view = View(http, browser);

        var options = new ClientOptions { ServerAddress = new Uri("https://host.invalid/") };
        await using var events = new EventStream(options, new HostConnectivity(http, options));
        using var watcher = new ContractStateWatcher(view, events);

        browser.FailNextListing(new OutOfMemoryException("this browser ran out of memory"));

        var observing = new ObservingContext();
        var restore = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(observing);

        try
        {
            // The handler captures whichever context is current when it starts, so this is the one an
            // async void boundary reports to and a discarded task never reaches.
            events.Publish(Envelope(EventKind.PluginStateChanged));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(restore);
        }

        await observing.Finished.WaitAsync(TimeSpan.FromSeconds(5));

        using var assertions = new AssertionScope();

        host.ManifestReads.Should().Be(1, "the event drove one reload, so this test is not vacuous");
        observing.Captured.Should().BeOfType<OutOfMemoryException>()
            .Which.Message.Should().Be("this browser ran out of memory");
    }

    private static ContractView View(HttpClient http, InMemoryContractStore browser)
    {
        var store = browser.Open();

        return new ContractView(
            new ContractReloader(
                new MediaContractLoader(http, store),
                new ContractStoreJanitor(store),
                store));
    }

    private static EventEnvelope Envelope(EventKind kind)
        => new() { Kind = kind, At = DateTimeOffset.UnixEpoch };

    /// <summary>Keeps what an <see langword="async"/> <see langword="void"/> boundary reported.</summary>
    /// <remarks>
    /// A boundary with a context reports through it and signals completion after; one that discarded its
    /// task reports nothing and never starts an operation here at all.
    /// </remarks>
    private sealed class ObservingContext : SynchronizationContext
    {
        private readonly TaskCompletionSource _finished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Finished => _finished.Task;

        public Exception? Captured { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                d(state);
            }
            catch (Exception failure)
            {
                Captured ??= failure;
            }
        }

        public override void OperationCompleted() => _finished.TrySetResult();
    }

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

        public HttpClient Connect() => ContractHost.Answering(Answer);

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
}
