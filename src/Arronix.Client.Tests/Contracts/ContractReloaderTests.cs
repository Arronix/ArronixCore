using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Client.Configuration;
using Arronix.Client.Contracts;
using Arronix.Client.Serialization;
using Arronix.Client.Services;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// Reading an installation and shedding what it no longer names, as one transaction.
/// </summary>
/// <remarks>
/// The loader serializes its own reads and nothing else, so a sweep computed from an older read could
/// finish after a newer one and evict exactly the addresses the newer installation had just fetched. Two
/// entry paths reach this — an operator pressing reload and a tab reacting to an extension changing
/// state — and a gate private to either would leave the race between them.
/// </remarks>
[TestFixture]
internal sealed class ContractReloaderTests
{
    private static readonly PluginId First = PluginId.FromString("reload.first");
    private static readonly PluginId Second = PluginId.FromString("reload.second");

    /// <summary>
    /// An operator's reload and an extension-state event are serialized, so no older sweep lands last.
    /// </summary>
    /// <remarks>
    /// The first sweep is held at its listing while the second transaction is started. Serialized, the
    /// second cannot even read until the first has swept, and the store ends holding exactly what the
    /// newest installation names. Unserialized, the second load writes its bytes while the first sweep is
    /// still holding the older installation's live set, and resuming it discards them.
    /// </remarks>
    [Test]
    public async Task AnOperatorReloadAndAStateChangeCannotOverlap()
    {
        var older = Payload("Fixture.Client.Reload.Older");
        var newer = Payload("Fixture.Client.Reload.Newer");

        var host = new Installation(
            Manifest("1111111111111111111111111111111111111111111111111111111111111111", First, older),
            older,
            newer);

        using var http = host.Connect();
        var browser = new InMemoryContractStore();
        var store = browser.Open();

        var loader = new MediaContractLoader(http, store);
        var reloader = new ContractReloader(loader, new ContractStoreJanitor(store));

        var options = new ClientOptions { ServerAddress = new Uri("https://host.invalid/") };
        await using var events = new EventStream(options, new HostConnectivity(http, options));
        using var watcher = new ContractStateWatcher(reloader, events);

        var completed = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reloader.Completed += (_, _) =>
        {
            if (Interlocked.Increment(ref completed) == 2)
            {
                both.TrySetResult();
            }
        };

        // Entry path one: the operator's button. Its sweep stalls at the listing.
        var release = browser.HoldNextListing(out var sweeping);
        var operatorReload = reloader.ReloadAsync();
        await sweeping.WaitAsync(TimeSpan.FromSeconds(5));

        browser.Keys.Should().Equal(
            new[] { older.Published.ContentHash },
            "the first read fetched over the network and wrote what it verified");

        // Entry path two: the host moves on and says so, while that sweep is still held.
        host.Publishes(Manifest("2222222222222222222222222222222222222222222222222222222222222222", Second, newer));
        events.Publish(new EventEnvelope { Kind = EventKind.PluginStateChanged, At = DateTimeOffset.UnixEpoch });

        release.SetResult();

        await operatorReload;
        await both.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var assertions = new AssertionScope();

        browser.Keys.Should().Equal(
            new[] { newer.Published.ContentHash },
            "the newest installation's bytes must survive a sweep computed from the older one");
        loader.Report!.InstallationHash.Should().Be(
            "2222222222222222222222222222222222222222222222222222222222222222");
        reloader.LastFailure.Should().BeNull();
    }

    /// <summary>
    /// A reload announces before the next one may read, and one refusing subscriber denies no other.
    /// </summary>
    /// <remarks>
    /// Announcing after releasing the lease would let the next reload read, sweep and record over the one
    /// still telling its subscribers, so consumers would learn of two installations out of order.
    /// </remarks>
    [Test]
    public async Task ANextReloadCannotReadUntilThisOneHasFinishedAnnouncing()
    {
        var payload = Payload("Fixture.Client.Reload.Ordered");
        var host = new Installation(
            Manifest("5555555555555555555555555555555555555555555555555555555555555555", First, payload),
            payload);

        using var http = host.Connect();
        var store = new InMemoryContractStore().Open();
        var reloader = new ContractReloader(new MediaContractLoader(http, store), new ContractStoreJanitor(store));

        var told = new List<string>();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var announcing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readsWhenLastToldRan = -1;

        reloader.Completed += (_, _) =>
        {
            told.Add("first");

            if (announcing.TrySetResult())
            {
                held.Task.GetAwaiter().GetResult();
            }

            throw new InvalidOperationException("the first subscriber refused");
        };

        reloader.Completed += (_, _) =>
        {
            told.Add("second");
            readsWhenLastToldRan = readsWhenLastToldRan < 0 ? host.ManifestReads : readsWhenLastToldRan;
        };

        // Held inside its own announcement, on a pool thread so the block is not this test's.
        var first = Task.Run(() => reloader.ReloadAsync());
        await announcing.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = Task.Run(() => reloader.ReloadAsync());
        held.SetResult();

        await first;
        await second;

        using var assertions = new AssertionScope();

        readsWhenLastToldRan.Should().Be(
            1,
            "the reload that started while this one was announcing must not have read yet");
        told.Should().Equal("first", "second", "first", "second");
        host.ManifestReads.Should().Be(2, "both reloads ran, so the first assertion is not vacuous");
        reloader.LastFailure.Should().Contain(
            "the first subscriber refused",
            "a refusal is stated and does not deny the subscriber after it");
    }

    /// <summary>A subscriber that refuses the signal does not deny it to the next, and is stated.</summary>
    /// <remarks>
    /// Raising the delegate whole stops at the first refusal, so a consumer registered after a broken one
    /// would never learn what the reload produced — and the fault would land on a task nothing awaits.
    /// </remarks>
    [Test]
    public async Task ARefusingSubscriberDoesNotDenyTheSignalToTheNext()
    {
        var payload = Payload("Fixture.Client.Reload.Announced");
        var host = new Installation(
            Manifest("3333333333333333333333333333333333333333333333333333333333333333", First, payload),
            payload);

        using var http = host.Connect();
        var browser = new InMemoryContractStore("DEAD00000000000000000000000000000000000000000000000000000000DEAD");
        var store = browser.Open();

        var reloader = new ContractReloader(
            new MediaContractLoader(http, store),
            new ContractStoreJanitor(store));

        string[]? observed = null;

        reloader.Completed += (_, _) => throw new InvalidOperationException("the first subscriber refused");
        reloader.Completed += (_, _) => observed = [.. browser.Keys];

        await reloader.ReloadAsync();

        using var assertions = new AssertionScope();

        observed.Should().Equal(
            new[] { payload.Published.ContentHash },
            "the second subscriber still sees the store the sweep left behind");
        reloader.LastFailure.Should().Contain(
            "the first subscriber refused",
            "a refusal is stated rather than lost on a task nothing awaits");
    }

    /// <summary>An observer that refuses the load stops neither the sweep nor the signal.</summary>
    [Test]
    public async Task AnObserverRefusingTheLoadStopsNeitherTheSweepNorTheSignal()
    {
        var payload = Payload("Fixture.Client.Reload.Refused");
        var host = new Installation(
            Manifest("4444444444444444444444444444444444444444444444444444444444444444", First, payload),
            payload);

        using var http = host.Connect();
        var browser = new InMemoryContractStore("DEAD11111111111111111111111111111111111111111111111111111111DEAD");
        var store = browser.Open();

        var loader = new MediaContractLoader(http, store);
        var reloader = new ContractReloader(loader, new ContractStoreJanitor(store));

        var announced = false;
        reloader.Completed += (_, _) => announced = true;
        loader.ReportChanged += Refuse;

        await reloader.ReloadAsync();

        using var assertions = new AssertionScope();

        browser.Keys.Should().Equal(
            new[] { payload.Published.ContentHash },
            "an observer's defect is not this page's eviction to lose");
        announced.Should().BeTrue();
        reloader.LastFailure.Should().Contain("a consumer refused the report");

        loader.ReportChanged -= Refuse;

        static void Refuse(object? sender, EventArgs e)
            => throw new InvalidOperationException("a consumer refused the report");
    }

    private static ClientContractManifest Manifest(string installationHash, PluginId package, Compiled payload)
        => new(
            MediaContractLoader.ClientContractIdentity,
            installationHash,
            [
                new ClientContractPackage(
                    package, "1.0.0", package.Value, [payload.Published], [package], new string('C', 64)),
            ],
            []);

    private static Compiled Payload(string assemblyName)
    {
        var image = CompiledContract.Image(assemblyName, Misbehaviour.None);

        ContractMetadataReader
            .TryRead(image, MediaContractLoader.ContractAssemblyName, out var metadata, out var unreadable)
            .Should().BeTrue(unreadable);

        return new Compiled(
            image,
            new ClientContractAssembly(
                assemblyName,
                assemblyName + ".dll",
                metadata!.Identity,
                Convert.ToHexString(SHA256.HashData(image)),
                metadata.ModuleVersionId,
                image.Length,
                metadata.Declarations));
    }

    private sealed record Compiled(byte[] Image, ClientContractAssembly Published);

    /// <summary>A host whose installation moves between one client's reads.</summary>
    private sealed class Installation(ClientContractManifest manifest, params Compiled[] payloads)
    {
        private ClientContractManifest _manifest = manifest;

        public int ManifestReads { get; private set; }

        public void Publishes(ClientContractManifest next) => _manifest = next;

        public HttpClient Connect()
            => new(new StubHandler(Answer)) { BaseAddress = new Uri("https://host.invalid/") };

        private HttpResponseMessage Answer(string path)
        {
            if (path.EndsWith("client-contracts", StringComparison.Ordinal))
            {
                ManifestReads++;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(_manifest, ApiJsonOptions.Default),
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            var payload = payloads.FirstOrDefault(candidate =>
                path.Contains(candidate.Published.AssemblyName, StringComparison.Ordinal));

            return payload is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload.Image) };
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
