using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Client.Contracts;
using Arronix.Client.Serialization;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// Every change to what a page holds of an installation, as one serialized transaction.
/// </summary>
/// <remarks>
/// A read outside this lease could have its bytes evicted by a sweep computed from an older one, and an
/// emptied store beside a read would leave a page describing a moment that never existed. Each transaction
/// is numbered and sealed over the store it left behind, so what a consumer shows is one moment.
/// </remarks>
[TestFixture]
internal sealed class ContractReloaderTests
{
    private const string Stale = "CCCC000000000000000000000000000000000000000000000000000000000003";

    /// <summary>An address no store may be asked about, which is what makes a sweep fail at one.</summary>
    private const string Unusable = "   ";

    private static readonly PluginId First = PluginId.FromString("reload.first");
    private static readonly PluginId Second = PluginId.FromString("reload.second");

    /// <summary>Two transactions cannot overlap, so no older sweep lands last.</summary>
    /// <remarks>
    /// The first sweep is held at its listing while a second transaction is started, and the second is not
    /// merely started but observed to have entered: an async method runs synchronously until its first
    /// incomplete await, so a lease that admitted it would already have read this host by the time the call
    /// returned its task. Reading nothing is what proves it was held. Unserialized, that read fetches and
    /// writes bytes the first sweep is still holding an older live set against, and resuming it discards
    /// them.
    /// </remarks>
    [Test]
    public async Task TwoTransactionsCannotOverlap()
    {
        var older = Payload("Fixture.Client.Reload.Older");
        var newer = Payload("Fixture.Client.Reload.Newer");

        var host = new Installation(
            Manifest("1111111111111111111111111111111111111111111111111111111111111111", First, older),
            older,
            newer);

        using var http = host.Connect();
        var browser = new InMemoryContractStore();
        var reloader = Reloader(http, browser);

        // Its sweep stalls at the listing, with its lease still held.
        var release = browser.HoldNextListing(out var sweeping);
        var first = Task.Run(() => reloader.ReloadAsync());
        await sweeping.WaitAsync(TimeSpan.FromSeconds(5));

        browser.Keys.Should().Equal(
            new[] { older.Published.ContentHash },
            "the first read fetched over the network and wrote what it verified");

        // The host moves on, and a second transaction is asked for while that sweep is still held.
        host.Publishes(Manifest("2222222222222222222222222222222222222222222222222222222222222222", Second, newer));
        var second = Entered(() => reloader.ReloadAsync(), out var entered);
        await entered.WaitAsync(TimeSpan.FromSeconds(5));

        host.ManifestReads.Should().Be(
            1,
            "the second transaction has entered the reloader and read nothing, which is the lease holding; "
            + "one that admitted it would have let it fetch and write bytes this sweep is about to evict");

        release.SetResult();

        var earlier = await first;
        var latest = await second;

        using var assertions = new AssertionScope();

        browser.Keys.Should().Equal(
            new[] { newer.Published.ContentHash },
            "the newest installation's bytes must survive a sweep computed from the older one");

        earlier.Sequence.Should().Be(1);
        latest.Sequence.Should().Be(2, "the second transaction could not start until the first finished");
        latest.Report!.InstallationHash.Should().Be(
            "2222222222222222222222222222222222222222222222222222222222222222");
        latest.StoredKeys.Should().Equal(
            new[] { newer.Published.ContentHash },
            "a transaction is sealed over the store it is leaving behind, read inside its own lease");
        latest.Failures.Should().BeEmpty();
    }

    /// <summary>Every failure one transaction contained is kept, whole and in the order it happened.</summary>
    /// <remarks>
    /// A single slot answers each step's question with the last step's answer. Order is part of the record:
    /// which step failed first is how a reader tells a cause from what followed it.
    /// </remarks>
    [Test]
    public async Task EveryFailureOneTransactionContainedIsKeptInTheOrderItHappened()
    {
        var host = new Installation(
            Nothing("6666666666666666666666666666666666666666666666666666666666666666"));

        using var http = host.Connect();

        // A store that lists an address and then refuses to take it back, so the sweep fails after it has
        // already discarded the one before it.
        var browser = new InMemoryContractStore(Stale, Unusable);

        var sealedRecord = await Reloader(http, browser).ReloadAsync();

        using var assertions = new AssertionScope();

        sealedRecord.Failures.Should().ContainSingle("the read produced a report and the sweep did not run clean")
            .Which.Should().Match<ContractFailure>(
                failure => failure.Stage == ContractFailureStage.Sweep
                    && failure.Message.Contains("contentHash"));

        sealedRecord.Report.Should().NotBeNull("a step failing does not cancel the ones after it");
        sealedRecord.StoredKeys.Should().Equal(
            new[] { Unusable },
            "the sweep discarded what it could before the address it could not");
        browser.Keys.Should().Equal(Unusable);
    }

    /// <summary>Discarding the stored bytes shares the lease a reload runs under.</summary>
    /// <remarks>
    /// Emptying the store beside a read would let a sweep evict from a store a discard had already emptied,
    /// or let a transaction seal keys another one is in the middle of removing. Observed to have entered
    /// rather than merely started, for the reason <see cref="TwoTransactionsCannotOverlap"/> gives, and it
    /// seals the same installation without reading one.
    /// </remarks>
    [Test]
    public async Task ADiscardWaitsForTheTransactionInFront()
    {
        var payload = Payload("Fixture.Client.Reload.Discarded");

        var host = new Installation(
            Manifest("7777777777777777777777777777777777777777777777777777777777777777", First, payload),
            payload);

        using var http = host.Connect();
        var browser = new InMemoryContractStore();
        var reloader = Reloader(http, browser);

        var release = browser.HoldNextListing(out var sweeping);
        var reload = Task.Run(() => reloader.ReloadAsync());
        await sweeping.WaitAsync(TimeSpan.FromSeconds(5));

        var discard = Entered(() => reloader.DiscardStoredBytesAsync(), out var entered);
        await entered.WaitAsync(TimeSpan.FromSeconds(5));

        browser.Keys.Should().Equal(
            new[] { payload.Published.ContentHash },
            "the discard has entered the reloader and emptied nothing, which is the lease holding; one "
            + "that admitted it would have cleared this store before its call returned");

        release.SetResult();

        var reloaded = await reload;
        var discarded = await discard;

        using var assertions = new AssertionScope();

        reloaded.Sequence.Should().Be(1);
        reloaded.StoredKeys.Should().Equal(
            new[] { payload.Published.ContentHash },
            "the reload sealed the store as it left it, before the discard emptied it");

        discarded.Sequence.Should().Be(2);
        discarded.StoredKeys.Should().BeEmpty();
        discarded.Report.Should().BeSameAs(
            reloaded.Report,
            "discarding bytes reads no installation and does not invent one");
        browser.Keys.Should().BeEmpty();
    }

    /// <summary>A process-unsound failure propagates rather than being filed as a step's defect.</summary>
    /// <remarks>Containment is for a defect in one step; an exhausted heap is not one.</remarks>
    [Test]
    public async Task AnUnsoundProcessPropagates()
    {
        var host = new Installation(
            Nothing("8888888888888888888888888888888888888888888888888888888888888888"));

        using var http = host.Connect();
        var browser = new InMemoryContractStore(Stale);
        var reloader = Reloader(http, browser);

        browser.FailNextListing(new OutOfMemoryException("this browser ran out of memory"));

        var reload = () => reloader.ReloadAsync();

        await reload.Should().ThrowAsync<OutOfMemoryException>()
            .WithMessage("this browser ran out of memory");
    }

    /// <summary>Starts a transaction and reports when the call itself has returned.</summary>
    /// <param name="start">The transaction to start.</param>
    /// <param name="entered">Completed once <paramref name="start"/> has returned its task.</param>
    /// <returns>The transaction.</returns>
    /// <remarks>
    /// An async method runs synchronously until its first incomplete await, so a transaction that got past
    /// the lease has already done everything up to that point by the time its call returns. That makes "it
    /// entered and did nothing" an observation rather than a wait.
    /// </remarks>
    private static Task<ContractReloadResult> Entered(
        Func<Task<ContractReloadResult>> start,
        out Task entered)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        entered = reached.Task;

        return Task.Run(() =>
        {
            var running = start();
            reached.SetResult();
            return running;
        });
    }

    private static ContractReloader Reloader(HttpClient http, InMemoryContractStore browser)
    {
        var store = browser.Open();

        return new ContractReloader(
            new MediaContractLoader(http, store),
            new ContractStoreJanitor(store),
            store);
    }

    /// <summary>A host that offers nothing, so a read costs one manifest and no bytes.</summary>
    private static ClientContractManifest Nothing(string installationHash)
        => new(MediaContractLoader.ClientContractIdentity, installationHash, [], []);

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

        public HttpClient Connect() => ContractHost.Answering(Answer);

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
}
