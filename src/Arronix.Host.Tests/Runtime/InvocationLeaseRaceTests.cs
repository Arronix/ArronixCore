using System.IO;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Host.Health;
using Arronix.Host.Providers;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;
using FluentAssertions;
using FluentAssertions.Execution;

namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// Teardown waits for a call that is inside extension code, whichever seam that call came through.
/// </summary>
/// <remarks>
/// The ordering is fixed by the test rather than raced: the callback signals that it is inside, teardown is
/// asked for and observed incomplete at that instant, and only then is the callback released. Both a
/// provider and a non-provider contribution are covered, because the lease is meant to be one rule rather
/// than a provider special case.
/// </remarks>
[TestFixture]
public class InvocationLeaseRaceTests
{
    private static readonly PluginId Package = PluginId.FromString("race.fixture");

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private string _root = string.Empty;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("arronix-lease-race").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task AProviderCallHoldsOffDisposalAndUnloadUntilItReturnsAsync()
    {
        var gate = new BlockingGate();
        var provider = new BlockingProvider(gate);
        var publication = new PluginPublicationGate();
        var registry = new ProviderRegistry(publication);
        var lease = Lease(provider);

        registry.TryPrepare(
            Package,
            ProviderFamily.Indexer,
            new ProviderDescriptor { LocalId = "blocking", Name = "Blocking", Settings = [] },
            provider,
            mediaItemType: null,
            out var candidate,
            out _,
            lease.Invocation).Should().BeTrue();
        registry.TryPublish(candidate, out _).Should().BeTrue();

        var calling = Task.Run(async () =>
        {
            registry.TryLease(candidate.Id, out var leased).Should().BeTrue();
            using var held = leased!;
            await held.Value.Provider.TestAsync(default, CancellationToken.None).ConfigureAwait(false);
        });

        await gate.Entered.WaitAsync(Patience).ConfigureAwait(false);

        var releasing = lease.DisposeAsync().AsTask();

        using (new AssertionScope())
        {
            releasing.IsCompleted.Should().BeFalse("the provider's own method is still executing");
            provider.Disposed.Should().BeFalse("nothing it owns may be disposed under a running call");
            lease.LoadContext.Should().NotBeNull("and its code may not be unloaded either");
        }

        gate.Release();
        await calling.WaitAsync(Patience).ConfigureAwait(false);

        var failures = await releasing.WaitAsync(Patience).ConfigureAwait(false);

        using (new AssertionScope())
        {
            failures.Should().BeEmpty();
            provider.Disposed.Should().BeTrue();
            lease.LoadContext.Should().BeNull();
        }
    }

    [Test]
    public async Task AHealthContributorCallHoldsOffDisposalAndUnloadUntilItReturnsAsync()
    {
        var gate = new BlockingGate();
        var contributor = new BlockingHealthContributor(gate);
        var publication = new PluginPublicationGate();
        var lease = Lease(contributor);
        var health = new PluginHealthContributor(new EmptyRuntimeRegistry(), publication);

        health.TryPublish(health.Prepare(Package, [contributor], lease.Invocation)).Should().BeTrue();

        var collecting = health.CheckAsync(CancellationToken.None);
        await gate.Entered.WaitAsync(Patience).ConfigureAwait(false);

        var releasing = lease.DisposeAsync().AsTask();

        using (new AssertionScope())
        {
            releasing.IsCompleted.Should().BeFalse("the contributor's own method is still executing");
            contributor.Disposed.Should().BeFalse();
            lease.LoadContext.Should().NotBeNull();
        }

        gate.Release();
        await collecting.WaitAsync(Patience).ConfigureAwait(false);
        await releasing.WaitAsync(Patience).ConfigureAwait(false);

        using (new AssertionScope())
        {
            contributor.Disposed.Should().BeTrue();
            lease.LoadContext.Should().BeNull();
        }
    }

    [Test]
    public void APublishedContributionWhoseRuntimeIsClosedIsReportedRatherThanSkipped()
    {
        var provider = new BlockingProvider(new BlockingGate());
        var registry = new ProviderRegistry(new PluginPublicationGate());
        var lease = Lease(provider);

        registry.TryPrepare(
            Package,
            ProviderFamily.Indexer,
            new ProviderDescriptor { LocalId = "closed", Name = "Closed", Settings = [] },
            provider,
            mediaItemType: null,
            out var candidate,
            out _,
            lease.Invocation).Should().BeTrue();
        registry.TryPublish(candidate, out _).Should().BeTrue();

        // The defect the invariant names: a runtime closed without its contributions being unpublished in
        // the same writer transition.
        lease.Invocation.Close();

        var take = () => registry.TryLease(candidate.Id, out _);

        take.Should().Throw<InvalidOperationException>()
            .WithMessage("*still published while extension*closed to invocation*");
    }

    [Test]
    public async Task AWithdrawnContributionCannotBeLeasedAtAllAsync()
    {
        var provider = new BlockingProvider(new BlockingGate());
        var registry = new ProviderRegistry(new PluginPublicationGate());
        var lease = Lease(provider);

        registry.TryPrepare(
            Package,
            ProviderFamily.Indexer,
            new ProviderDescriptor { LocalId = "gone", Name = "Gone", Settings = [] },
            provider,
            mediaItemType: null,
            out var candidate,
            out _,
            lease.Invocation).Should().BeTrue();
        registry.TryPublish(candidate, out _).Should().BeTrue();

        // The ordinary transition: unpublished first, then closed, both under the writer.
        registry.Remove(candidate).Should().BeTrue();
        await lease.DisposeAsync().ConfigureAwait(false);

        registry.TryLease(candidate.Id, out var leased).Should().BeFalse();
        leased.Should().BeNull();
    }

    private PluginRuntimeLease Lease(object owned)
    {
        var entry = Path.Combine(_root, $"Race.{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(entry, []);

        var context = new PluginLoadContext(
            Package,
            entry,
            nativeLibraryResolver: null,
            PackageContractScope.Empty(Package));

        // Recorded directly rather than through the capability gate: what this fixture needs is a ledger
        // whose entries the runtime lease will dispose, not a second proof of the admission rules.
        var ledger = new Arronix.Plugins.Registration.PluginRegistrationLedger(Package);
        ledger.Record(
            owned is IHealthContributor ? typeof(IHealthContributor) : typeof(IProvider),
            owned);
        ledger.Freeze();

        return new PluginRuntimeLease(context, ledger, module: null);
    }

    /// <summary>A callback that reports when it is inside and stays there until released.</summary>
    private sealed class BlockingGate
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Entered => _entered.Task;

        internal async Task WaitAsync()
        {
            _entered.TrySetResult();
            await _released.Task.ConfigureAwait(false);
        }

        internal void Release() => _released.TrySetResult();
    }

    private sealed class BlockingProvider(BlockingGate gate)
        : IProvider, Arronix.Abstractions.Parsing.IReleaseParser, IDisposable
    {
        public bool Disposed { get; private set; }

        public Arronix.Abstractions.Identity.MediaKindId MediaKind => new("race");

        public async Task<ValidationOutcome> TestAsync(
            ProviderInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            return ValidationOutcome.Success;
        }

        public Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
            ProviderInvocation invocation,
            string optionSourceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FacetValue>>([]);

        public bool CanParse(string releaseName) => false;

        public Arronix.Abstractions.DTOs.ParsedRelease Parse(string releaseName)
            => throw new NotSupportedException();

        public void Dispose() => Disposed = true;
    }

    private sealed class BlockingHealthContributor(BlockingGate gate) : IHealthContributor, IDisposable
    {
        public bool Disposed { get; private set; }

        public string ContributorId => "blocking";

        public async Task<IReadOnlyList<HealthCheck>> CheckAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            return [HealthCheck.Healthy("blocking", "Blocking")];
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>A runtime registry with nothing in it, so the health report is only the contributors.</summary>
    private sealed class EmptyRuntimeRegistry : IPluginRuntimeRegistry
    {
        public IReadOnlyList<Arronix.Abstractions.Wire.PluginStatusView> Snapshot() => [];
    }
}
