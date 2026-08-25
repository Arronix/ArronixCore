using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Common.Caching;
using Microsoft.Extensions.Time.Testing;

namespace Arronix.Common.Tests.Caching;

/// <summary>
/// The distinctions the caching contract makes, held to by the implementation behind it.
/// </summary>
[TestFixture]
public class CacheProviderTests
{
    private sealed class Owner;

    private sealed class OtherOwner;

    private FakeTimeProvider _clock = null!;
    private CacheProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);

        // The sweep is proved by its own test below. Everywhere else it would be a second actor moving
        // entries while an expiry rule is being asserted, so it is off.
        _provider = NoSweep(_clock);
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    private static CacheProvider NoSweep(TimeProvider clock)
        => new(clock, Microsoft.Extensions.Options.Options.Create(new CacheOptions { SweepInterval = TimeSpan.Zero }));

    [Test]
    public void OrdinaryCache_KeepsAnEntryWithNoSuppliedLifetimeForever()
    {
        var cache = _provider.GetCache<Owner, string>("titles");
        cache.Set("k", "v");

        _clock.Advance(TimeSpan.FromDays(3650));

        Assert.That(cache.Find("k"), Is.EqualTo("v"));
    }

    [Test]
    public void FixedExpiry_IsMeasuredFromStorageAndIsNotExtendedByReading()
    {
        var cache = _provider.GetCache<Owner, string>("titles");
        cache.Set("k", "v", TimeSpan.FromMinutes(10));

        _clock.Advance(TimeSpan.FromMinutes(9));
        Assert.That(cache.Find("k"), Is.EqualTo("v"), "the entry is still inside its lifetime");

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.That(cache.Find("k"), Is.Null, "reading does not move a fixed expiry");
    }

    [Test]
    public void RollingExpiry_IsMeasuredFromTheLastSuccessfulRead()
    {
        var cache = _provider.GetRollingCache<Owner, string>("sessions", TimeSpan.FromMinutes(10));
        cache.Set("k", "v");

        for (var read = 0; read < 5; read++)
        {
            _clock.Advance(TimeSpan.FromMinutes(9));
            Assert.That(cache.Find("k"), Is.EqualTo("v"));
        }

        _clock.Advance(TimeSpan.FromMinutes(11));
        Assert.That(cache.Find("k"), Is.Null);
    }

    [Test]
    public void ExpiredEntries_AreCountedUntilSweptAndAreNeverReturned()
    {
        var cache = _provider.GetCache<Owner, string>("titles");
        cache.Set("live", "a", TimeSpan.FromHours(2));
        cache.Set("dead", "b", TimeSpan.FromMinutes(1));

        _clock.Advance(TimeSpan.FromMinutes(30));

        Assert.Multiple(() =>
        {
            Assert.That(cache.Count, Is.EqualTo(2), "an unswept expired entry is still held");
            Assert.That(cache.Values, Is.EquivalentTo(new[] { "a" }), "only live values are enumerated");
            Assert.That(cache.Find("dead"), Is.Null);
        });

        cache.ClearExpired();

        Assert.That(cache.Count, Is.EqualTo(1));
    }

    [Test]
    public void TheProviderSweep_RunsOnTheInjectedClock()
    {
        using var swept = new CacheProvider(_clock);
        var cache = swept.GetCache<Owner, string>("titles");
        cache.Set("dead", "b", TimeSpan.FromMinutes(1));

        Assert.That(cache.Count, Is.EqualTo(1));

        _clock.Advance(CacheOptions.DefaultSweepInterval + TimeSpan.FromMinutes(1));

        Assert.That(cache.Count, Is.EqualTo(0), "the sweep removed the expired entry");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ANonPositiveLifetime_IsRefusedRatherThanClamped(int minutes)
    {
        var lifetime = TimeSpan.FromMinutes(minutes);
        var cache = _provider.GetCache<Owner, string>("titles");

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.Set("k", "v", lifetime));
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get("k", () => "v", lifetime));
            Assert.That(
                () => cache.GetAsync("k", _ => Task.FromResult("v"), lifetime),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _provider.GetRollingCache<OtherOwner, string>("rolling", lifetime));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _provider.GetSelfRefreshingCache<OtherOwner, string>(
                    "bulk",
                    _ => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()),
                    lifetime));
        });
    }

    [Test]
    public async Task ConcurrentMisses_ObserveOneAsynchronousFactoryInvocation()
    {
        var cache = _provider.GetCache<Owner, string>("titles");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;

        async Task<string> Factory(CancellationToken token)
        {
            if (Interlocked.Increment(ref invocations) == 1)
            {
                started.TrySetResult();
            }

            await release.Task.ConfigureAwait(false);
            return "produced";
        }

        var callers = Enumerable.Range(0, 16)
            .Select(_ => cache.GetAsync("k", Factory))
            .ToArray();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        release.SetResult();

        var results = await Task.WhenAll(callers).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.All.EqualTo("produced"));
            Assert.That(invocations, Is.EqualTo(1), "a miss is single-flight");
        });
    }

    [Test]
    public void ConcurrentMisses_ObserveOneSynchronousFactoryInvocation()
    {
        var cache = _provider.GetCache<Owner, string>("titles");
        using var release = new ManualResetEventSlim(false);
        var entered = new CountdownEvent(1);
        var invocations = 0;

        string Factory()
        {
            if (Interlocked.Increment(ref invocations) == 1)
            {
                entered.Signal();
            }

            release.Wait(TimeSpan.FromSeconds(10));
            return "produced";
        }

        var callers = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => cache.Get("k", Factory)))
            .ToArray();

        Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        release.Set();

        Task.WaitAll(callers, TimeSpan.FromSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(callers.Select(caller => caller.Result), Is.All.EqualTo("produced"));
            Assert.That(invocations, Is.EqualTo(1));
        });
    }

    [Test]
    public void AFailedFactory_IsNotStoredAndDoesNotPoisonTheNextAttempt()
    {
        var cache = _provider.GetCache<Owner, string>("titles");

        Assert.Throws<InvalidOperationException>(
            () => cache.Get("k", () => throw new InvalidOperationException("upstream")));

        Assert.Multiple(() =>
        {
            Assert.That(cache.Find("k"), Is.Null, "a failure is not a value");
            Assert.That(cache.Count, Is.Zero);
            Assert.That(cache.Get("k", () => "second"), Is.EqualTo("second"));
        });
    }

    [Test]
    public async Task ACanceledFactory_IsNotStoredAndLeavesTheKeyProducibleAsync()
    {
        var cache = _provider.GetCache<Owner, string>("titles");
        using var cancellation = new CancellationTokenSource();

        var pending = cache.GetAsync(
            "k",
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return "never";
            },
            lifetime: null,
            cancellation.Token);

        await cancellation.CancelAsync().ConfigureAwait(false);
        Assert.That(async () => await pending.ConfigureAwait(false), Throws.TypeOf<TaskCanceledException>());

        Assert.That(cache.Find("k"), Is.Null);
        Assert.That(
            await cache.GetAsync("k", _ => Task.FromResult("after")).ConfigureAwait(false),
            Is.EqualTo("after"));
    }

    [Test]
    public async Task AWaiterOutlivingTheAttemptThatWasCanceled_ProducesTheValueItselfAsync()
    {
        var cache = _provider.GetCache<Owner, string>("titles");
        using var cancellation = new CancellationTokenSource();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var abandoned = cache.GetAsync(
            "k",
            async token =>
            {
                firstEntered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return "never";
            },
            lifetime: null,
            cancellation.Token);

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        var waiter = cache.GetAsync("k", _ => Task.FromResult("mine"));

        await cancellation.CancelAsync().ConfigureAwait(false);
        Assert.That(async () => await abandoned.ConfigureAwait(false), Throws.TypeOf<TaskCanceledException>());

        Assert.That(await waiter.ConfigureAwait(false), Is.EqualTo("mine"));
    }

    [Test]
    public void ReusingOneOwnerAndPartition_WithADifferentShape_IsRefused()
    {
        _ = _provider.GetCache<Owner, string>("titles");

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(
                () => _provider.GetCache<Owner, int>("titles"),
                "a different value type is a different cache");
            Assert.Throws<InvalidOperationException>(
                () => _provider.GetRollingCache<Owner, string>("titles", TimeSpan.FromMinutes(1)),
                "a different expiry mode is a different cache");
            Assert.Throws<InvalidOperationException>(
                () => _provider.GetSelfRefreshingCache<Owner, string>(
                    "titles",
                    _ => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>())),
                "a different cache contract is a different cache");
        });
    }

    [Test]
    public void ReusingOneRollingPartition_WithADifferentDefaultLifetime_IsRefused()
    {
        _ = _provider.GetRollingCache<Owner, string>("sessions", TimeSpan.FromMinutes(5));

        Assert.Throws<InvalidOperationException>(
            () => _provider.GetRollingCache<Owner, string>("sessions", TimeSpan.FromMinutes(6)));
    }

    [Test]
    public void ReusingOneShape_ReturnsTheSameCache()
    {
        var first = _provider.GetCache<Owner, string>("titles");
        var second = _provider.GetCache<Owner, string>("titles");

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void TwoOwnersSharingAPartitionName_DoNotShareACache()
    {
        var mine = _provider.GetCache<Owner, string>("titles");
        var theirs = _provider.GetCache<OtherOwner, string>("titles");

        mine.Set("k", "mine");

        Assert.Multiple(() =>
        {
            Assert.That(theirs, Is.Not.SameAs(mine));
            Assert.That(theirs.Find("k"), Is.Null);
        });
    }
}
