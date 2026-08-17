using Arronix.Host.Providers;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace Arronix.Host.Tests.Providers;

/// <summary>
/// Resting a failing provider, and the two grace windows that stop the resting being counter-productive.
/// </summary>
[TestFixture]
internal sealed class ProviderStatusStoreTests
{
    private static (ProviderStatusStore Store, FakeTimeProvider Clock) Build()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return (new ProviderStatusStore(clock), clock);
    }

    private static (ProviderStatusStore Store, FakeTimeProvider Clock) BuiltAndWarmedUp()
    {
        var (store, clock) = Build();
        clock.Advance(ProviderStatusStore.StartupGrace + TimeSpan.FromMinutes(1));
        return (store, clock);
    }

    [Test]
    public void AProviderThatHasNeverFailedIsAvailable()
    {
        var (store, _) = BuiltAndWarmedUp();
        store.IsAvailable(1).Should().BeTrue();
        store.Find(1).Should().BeNull();
    }

    [Test]
    public void AFailureInsideTheInitialGraceWindowDoesNotRestTheProvider()
    {
        var (store, clock) = BuiltAndWarmedUp();

        store.RecordFailure(1);
        store.IsAvailable(1).Should().BeTrue();

        clock.Advance(TimeSpan.FromMinutes(1));

        store.RecordFailure(1);
        store.IsAvailable(1).Should().BeTrue();
    }

    [Test]
    public void FailuresPastTheGraceWindowRestTheProvider()
    {
        var (store, clock) = BuiltAndWarmedUp();

        store.RecordFailure(1);
        clock.Advance(ProviderStatusStore.InitialFailureGrace + TimeSpan.FromMinutes(1));

        store.RecordFailure(1);

        store.IsAvailable(1).Should().BeFalse();
    }

    [Test]
    public void TheRestEndsWhenItsWindowDoes()
    {
        var (store, clock) = BuiltAndWarmedUp();

        store.RecordFailure(1);
        clock.Advance(ProviderStatusStore.InitialFailureGrace + TimeSpan.FromMinutes(1));
        var status = store.RecordFailure(1);

        store.IsAvailable(1).Should().BeFalse();

        clock.Advance(status.DisabledTill!.Value - clock.GetUtcNow() + TimeSpan.FromSeconds(1));

        store.IsAvailable(1).Should().BeTrue();
    }

    [Test]
    public void EveryProviderIsUsedAtLeastOnceInTheWindowAfterTheHostStarts()
    {
        var (store, _) = Build();

        store.RecordFailure(1);
        store.RecordFailure(1);
        store.RecordFailure(1);

        // A deployment that was offline overnight would otherwise start with every provider deep in
        // escalation and sit idle for hours without ever finding out they had recovered.
        store.IsAvailable(1).Should().BeTrue();
    }

    [Test]
    public void ARemotesOwnRetryDeadlineIsHonoredWhenItIsLongerThanTheLadder()
    {
        var (store, clock) = BuiltAndWarmedUp();

        var status = store.RecordFailure(1, TimeSpan.FromHours(6));

        status.DisabledTill.Should().Be(clock.GetUtcNow() + TimeSpan.FromHours(6));
    }

    [Test]
    public void RecordingAConnectionFailureLeavesTheProviderInService()
    {
        var (store, clock) = BuiltAndWarmedUp();

        store.RecordFailure(1);
        clock.Advance(ProviderStatusStore.InitialFailureGrace + TimeSpan.FromMinutes(1));
        store.RecordFailure(1);
        store.IsAvailable(1).Should().BeFalse();

        // A malformed response says something about one request, not about the provider, so it must not push
        // a working provider further up the ladder.
        var level = store.Find(1)!.Value.EscalationLevel;
        store.RecordConnectionFailure(1);
        store.Find(1)!.Value.EscalationLevel.Should().Be(level);
    }

    [Test]
    public void ASuccessClearsEverything()
    {
        var (store, clock) = BuiltAndWarmedUp();

        store.RecordFailure(1);
        clock.Advance(ProviderStatusStore.InitialFailureGrace + TimeSpan.FromMinutes(1));
        store.RecordFailure(1);

        store.RecordSuccess(1);

        store.Find(1).Should().BeNull();
        store.IsAvailable(1).Should().BeTrue();
    }

    [Test]
    public void EachProviderClimbsItsOwnLadder()
    {
        var (store, clock) = BuiltAndWarmedUp();

        store.RecordFailure(1);
        clock.Advance(ProviderStatusStore.InitialFailureGrace + TimeSpan.FromMinutes(1));
        store.RecordFailure(1);

        store.IsAvailable(1).Should().BeFalse();
        store.IsAvailable(2).Should().BeTrue();
    }
}
