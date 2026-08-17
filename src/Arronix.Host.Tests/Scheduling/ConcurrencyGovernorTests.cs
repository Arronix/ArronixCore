using Arronix.Abstractions.Plugins;
using Arronix.Host.Configuration;
using Arronix.Host.Scheduling;
using Arronix.Host.Tests.Support;
using FluentAssertions;

// The plugin contracts are experimental; throttle keys are synthesized from the capability vocabulary.
#pragma warning disable ARX0014

namespace Arronix.Host.Tests.Scheduling;

/// <summary>
/// Three ceilings applying at once, and the ordering that makes deadlock impossible.
/// </summary>
[TestFixture]
internal sealed class ConcurrencyGovernorTests
{
    private static ConcurrencyGovernor Governor(int global, params (string Key, int Limit)[] throttles)
    {
        var options = new SchedulerOptions { MaxConcurrentJobs = global };
        options.ThrottleLimits.Clear();

        foreach (var (key, limit) in throttles)
        {
            options.ThrottleLimits[key] = limit;
        }

        return new ConcurrencyGovernor(TestOptions.Of(options));
    }

    [Test]
    public void TheGlobalCeilingIsTheConfiguredNumberOfConcurrentJobs()
    {
        using var governor = Governor(3);
        governor.LimitFor(ConcurrencyGovernor.GlobalKey).Should().Be(3);
    }

    [Test]
    public void AKeyWithNoConfiguredCeilingIsUnthrottled()
    {
        using var governor = Governor(3);
        governor.LimitFor("kind:anything").Should().Be(int.MaxValue);
    }

    [Test]
    public void AnOperatorMayWriteTheBareCapabilityName()
    {
        using var governor = Governor(3, ("indexing", 2));
        governor.LimitFor("cap:indexing").Should().Be(2);
    }

    [Test]
    public async Task TheGlobalCeilingHoldsAcrossUnrelatedWork()
    {
        using var governor = Governor(2);
        var keys = new[] { ConcurrencyGovernor.GlobalKey };

        governor.TryAcquire(keys, out var first).Should().BeTrue();
        governor.TryAcquire(keys, out var second).Should().BeTrue();
        governor.TryAcquire(keys, out var third).Should().BeFalse();
        third.Should().BeNull();

        await first!.DisposeAsync();

        governor.TryAcquire(keys, out var fourth).Should().BeTrue();

        await second!.DisposeAsync();
        await fourth!.DisposeAsync();
    }

    [Test]
    public async Task ACapabilityThrottleHoldsIndependentlyOfTheGlobalCeiling()
    {
        using var governor = Governor(8, ("metadata", 1));
        var metadata = new[] { ConcurrencyGovernor.GlobalKey, "cap:metadata" };
        var indexing = new[] { ConcurrencyGovernor.GlobalKey };

        governor.TryAcquire(metadata, out var held).Should().BeTrue();

        // The capability is saturated, so a second piece of that work waits...
        governor.TryAcquire(metadata, out _).Should().BeFalse();

        // ...while unrelated work is unaffected, which is the whole point of a per-key throttle.
        governor.TryAcquire(indexing, out var unrelated).Should().BeTrue();

        await held!.DisposeAsync();
        await unrelated!.DisposeAsync();
    }

    [Test]
    public void AFailedAcquisitionReleasesEverythingItHadAlreadyTaken()
    {
        using var governor = Governor(4, ("indexing", 1), ("metadata", 1));

        governor.TryAcquire(["cap:metadata"], out var blocking).Should().BeTrue();

        // The ordered acquisition takes "cap:indexing" first and then fails on "cap:metadata". If the
        // partial acquisition were not unwound, "cap:indexing" would be held by nobody forever.
        governor.TryAcquire([ConcurrencyGovernor.GlobalKey, "cap:indexing", "cap:metadata"], out _)
            .Should().BeFalse();

        governor.TryAcquire(["cap:indexing"], out var proof).Should().BeTrue();

        proof.Should().NotBeNull();
        blocking.Should().NotBeNull();
    }

    [Test]
    public async Task ALeaseIsIdempotentWhenDisposedTwice()
    {
        using var governor = Governor(1);

        governor.TryAcquire([ConcurrencyGovernor.GlobalKey], out var lease).Should().BeTrue();

        await lease!.DisposeAsync();
        await lease.DisposeAsync();

        // A second release would push the semaphore's count above its maximum and the next acquisition would
        // wrongly succeed twice.
        governor.TryAcquire([ConcurrencyGovernor.GlobalKey], out var again).Should().BeTrue();
        governor.TryAcquire([ConcurrencyGovernor.GlobalKey], out _).Should().BeFalse();

        await again!.DisposeAsync();
    }

    [Test]
    public async Task WaitingForKeysSucceedsOnceTheyAreReleased()
    {
        using var governor = Governor(1);

        governor.TryAcquire([ConcurrencyGovernor.GlobalKey], out var held).Should().BeTrue();

        var waiting = governor.AcquireAsync([ConcurrencyGovernor.GlobalKey], CancellationToken.None);

        waiting.IsCompleted.Should().BeFalse();

        await held!.DisposeAsync();

        var acquired = await waiting;
        await acquired.DisposeAsync();
    }

    [Test]
    public void KeysAreSynthesizedAtThreeScopesAndOrdered()
    {
        var keys = ConcurrencyGovernor.KeysFor(
            PluginId.FromString("fixture"),
            CapabilitySet.Of(Capability.Indexing).WithImplied(),
            "fixture-kind");

        keys.Should().Contain(ConcurrencyGovernor.GlobalKey);
        keys.Should().Contain("plugin:fixture");
        keys.Should().Contain("cap:indexing");
        keys.Should().Contain("cap:network");
        keys.Should().Contain("kind:fixture-kind");
        keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Test]
    public void WorkThatConcernsNoMediaKindGetsNoKindKey()
    {
        var keys = ConcurrencyGovernor.KeysFor(
            PluginId.FromString("fixture"),
            CapabilitySet.None,
            mediaKind: null);

        keys.Should().NotContain(key => key.StartsWith("kind:", StringComparison.Ordinal));
    }

    [Test]
    public void AcquisitionIsAlwaysInOrdinalOrderWhateverOrderTheCallerAsksIn()
    {
        using var governor = Governor(4, ("indexing", 1), ("metadata", 1), ("import", 1));

        // Two callers naming the same keys in opposite orders is the classic multi-lock deadlock. A total
        // order over the keys removes the cycle, so the second caller simply fails to acquire.
        governor.TryAcquire(["cap:metadata", "cap:indexing"], out var first).Should().BeTrue();
        governor.TryAcquire(["cap:indexing", "cap:metadata"], out var second).Should().BeFalse();

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Test]
    public void ADisposedGovernorRefusesFurtherAcquisition()
    {
        var governor = Governor(1);
        governor.Dispose();

        var act = () => governor.TryAcquire([ConcurrencyGovernor.GlobalKey], out _);
        act.Should().Throw<ObjectDisposedException>();
    }
}
