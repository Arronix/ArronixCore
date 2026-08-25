using System.IO;
using Arronix.Abstractions.Plugins;
using Arronix.Common.Contributions;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;
using FluentAssertions.Execution;

namespace Arronix.Plugins.Tests.Registry;

/// <summary>
/// The rule every runtime call into extension code follows, and what teardown is entitled to conclude
/// from it.
/// </summary>
[TestFixture]
internal sealed class PluginInvocationLifetimeTests
{
    private static readonly PluginId Package = PluginId.FromString("invocation.fixture");

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private string _root = string.Empty;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("arronix-invocation").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task ADrainWaitsForEveryTicketAlreadyTakenAsync()
    {
        var lifetime = new PluginInvocationLifetime(Package);

        lifetime.TryEnter(out var first).Should().BeTrue();
        lifetime.TryEnter(out var second).Should().BeTrue();

        var drained = lifetime.DrainAsync();

        using (new AssertionScope())
        {
            drained.IsCompleted.Should().BeFalse("two tickets are outstanding");
            lifetime.IsClosed.Should().BeTrue("draining closes first, so nothing new can start");
            lifetime.Outstanding.Should().Be(2);
        }

        first!.Dispose();
        drained.IsCompleted.Should().BeFalse("one is still outstanding");

        second!.Dispose();
        await drained.WaitAsync(Patience).ConfigureAwait(false);
    }

    [Test]
    public void AClosedLifetimeAdmitsNothingFurther()
    {
        var lifetime = new PluginInvocationLifetime(Package);
        lifetime.Close();

        using (new AssertionScope())
        {
            lifetime.TryEnter(out var ticket).Should().BeFalse();
            ticket.Should().BeNull();
            lifetime.IsClosed.Should().BeTrue();
        }
    }

    [Test]
    public void ReleasingOneTicketTwiceReleasesItOnce()
    {
        var lifetime = new PluginInvocationLifetime(Package);
        lifetime.TryEnter(out var ticket).Should().BeTrue();

        ticket!.Dispose();
        ticket.Dispose();

        lifetime.Outstanding.Should().Be(0);
    }

    [Test]
    public async Task TeardownWaitsForAnInvocationBeforeItDisposesAnythingAsync()
    {
        var module = new RecordingModule();
        var lifetime = new PluginInvocationLifetime(Package);
        var lease = new PluginRuntimeLease(Context(), ledger: null, module, invocation: lifetime);

        lifetime.TryEnter(out var inFlight).Should().BeTrue();

        var releasing = lease.DisposeAsync().AsTask();

        using (new AssertionScope())
        {
            releasing.IsCompleted.Should().BeFalse("a call is still inside the extension");
            module.Disposed.Should().BeFalse("nothing it owns may be disposed while that call runs");
            lease.LoadContext.Should().NotBeNull("and its code may not be unloaded either");
        }

        inFlight!.Dispose();

        var failures = await releasing.WaitAsync(Patience).ConfigureAwait(false);

        using (new AssertionScope())
        {
            failures.Should().BeEmpty();
            module.Disposed.Should().BeTrue();
            lease.LoadContext.Should().BeNull("the unload was requested, so the lease stops rooting it");
        }
    }

    [Test]
    public async Task ACacheNamespaceThatWillNotReleaseIsContainedAndRetainsTheContextAsync()
    {
        var module = new RecordingModule();
        var lease = new PluginRuntimeLease(
            Context(),
            ledger: null,
            module,
            caches: new RefusingNamespace());

        var failures = await lease.DisposeAsync().ConfigureAwait(false);

        using (new AssertionScope())
        {
            failures.Should().HaveCount(2);
            failures[0].Should().Contain("cache namespace").And.Contain("the namespace objected");
            failures[1].Should().Contain("load context").And.Contain("retained");
            lease.LoadContext.Should().NotBeNull(
                "a package whose caches could not be taken back may still be reachable");
        }
    }

    [Test]
    public void ASetOfLeasesIsReleasedWhollyWhenAConsumerThrowsPartWayThrough()
    {
        var lifetime = new PluginInvocationLifetime(Package);
        var leases = new List<Leased<int>>();

        for (var index = 0; index < 4; index++)
        {
            lifetime.TryEnter(out var ticket).Should().BeTrue();
            leases.Add(new Leased<int>(index, ticket));
        }

        lifetime.Outstanding.Should().Be(4);

        var consume = () =>
        {
            using var set = new LeasedSet<int>(leases);

            foreach (var value in set)
            {
                if (value == 1)
                {
                    throw new InvalidOperationException("the second contribution objected");
                }
            }
        };

        consume.Should().Throw<InvalidOperationException>();
        lifetime.Outstanding.Should().Be(0, "a leaked ticket is an extension that can never be torn down");
    }

    [Test]
    public void EveryLeaseIsReleasedEvenWhenAnEarlierReleaseThrows()
    {
        var lifetime = new PluginInvocationLifetime(Package);
        var released = new List<int>();
        var leases = new List<Leased<int>>();

        for (var index = 0; index < 4; index++)
        {
            lifetime.TryEnter(out var ticket).Should().BeTrue();
            leases.Add(new Leased<int>(index, new ObjectingTicket(index, ticket!, released, objectsAt: 1)));
        }

        var set = new LeasedSet<int>(leases);
        var dispose = set.Dispose;

        var failure = dispose.Should().Throw<AggregateException>().Which;

        using (new AssertionScope())
        {
            failure.InnerExceptions.Should().ContainSingle();
            released.Should().Equal([0, 1, 2, 3]);
            lifetime.Outstanding.Should().Be(
                1,
                "the three that could be released were, and only the one that objected is still held");
        }
    }

    /// <summary>A ticket whose release throws, after passing the release through.</summary>
    private sealed class ObjectingTicket(
        int index,
        IDisposable inner,
        List<int> released,
        int objectsAt) : IDisposable
    {
        public void Dispose()
        {
            released.Add(index);

            if (index == objectsAt)
            {
                throw new InvalidOperationException($"lease {index} objected");
            }

            inner.Dispose();
        }
    }

    private PluginLoadContext Context()
    {
        var entry = Path.Combine(_root, "Invocation.Entry.dll");
        File.WriteAllBytes(entry, []);

        return new PluginLoadContext(
            Package,
            entry,
            nativeLibraryResolver: null,
            PackageContractScope.Empty(Package));
    }

    private sealed class RecordingModule : IPluginModule, IDisposable
    {
        public PluginId Id => Package;

        public bool Disposed { get; private set; }

        public void Configure(IPluginContext context)
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class RefusingNamespace : Arronix.Common.Caching.ICacheNamespace
    {
        public string Name => "plugin:invocation.fixture#1";

        public bool IsReleased => false;

        public ValueTask ReleaseAsync() => throw new InvalidOperationException("the namespace objected");

        public Arronix.Abstractions.Caching.ICache<TValue> GetCache<TOwner, TValue>(string partition)
            => throw new NotSupportedException();

        public Arronix.Abstractions.Caching.ICache<TValue> GetRollingCache<TOwner, TValue>(
            string partition,
            TimeSpan defaultLifetime) => throw new NotSupportedException();

        public Arronix.Abstractions.Caching.ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
            string partition,
            Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
            TimeSpan? lifetime = null) => throw new NotSupportedException();
    }
}
