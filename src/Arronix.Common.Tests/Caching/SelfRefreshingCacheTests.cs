using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Caching;
using Arronix.Common.Caching;
using Microsoft.Extensions.Time.Testing;

namespace Arronix.Common.Tests.Caching;

/// <summary>
/// The bulk-fetched cache's documented meanings: forced versus conditional refresh, <c>Touch</c> versus
/// <c>Update</c>, and one fetch for concurrent callers.
/// </summary>
[TestFixture]
public class SelfRefreshingCacheTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private sealed class Owner;

    private FakeTimeProvider _clock = null!;
    private CacheProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        _provider = new CacheProvider(
            _clock,
            Microsoft.Extensions.Options.Options.Create(new CacheOptions { SweepInterval = TimeSpan.Zero }));
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    [Test]
    public async Task ACallerThatJoinedAFetchIsNotWokenBeforeItsContentsArePublishedAsync()
    {
        var publishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetches = 0;

        var cache = Bulk(() => Interlocked.Increment(ref fetches));

        // Held open between the fetch producing contents and those contents becoming the cache's. A caller
        // woken inside that window reads what the fetch was replacing - here, on contents never loaded,
        // nothing at all.
        cache.OnFetched = () =>
        {
            publishing.TrySetResult();
            proceed.Task.GetAwaiter().GetResult();
        };

        var owner = Task.Run(() => cache.RefreshAsync());
        var joinedRefresh = Task.CompletedTask;
        var joinedRead = Task.FromResult("(never started)");
        var wokenEarly = false;

        try
        {
            await publishing.Task.WaitAsync(Patience).ConfigureAwait(false);

            // Both join the flight, which cannot be retired while publication is held.
            joinedRefresh = Task.Run(() => cache.RefreshAsync());
            joinedRead = Task.Run(() => cache.GetAsync("k"));

            var woken = await Task.WhenAny(joinedRefresh, joinedRead, Task.Delay(TimeSpan.FromMilliseconds(250)))
                .ConfigureAwait(false);

            wokenEarly = ReferenceEquals(woken, joinedRefresh) || ReferenceEquals(woken, joinedRead);
        }
        finally
        {
            // Released whatever was found. A gate a failed assertion left shut would strand the fetch and
            // hang the provider's disposal, and a proof has to report red rather than have to be killed.
            proceed.TrySetResult();
        }

        var ownerFailure = await SettledAsync(owner).ConfigureAwait(false);
        var refreshFailure = await SettledAsync(joinedRefresh).ConfigureAwait(false);
        var readFailure = await SettledAsync(joinedRead).ConfigureAwait(false);
        var read = readFailure is null ? joinedRead.Result : null;

        Assert.Multiple(() =>
        {
            Assert.That(
                wokenEarly,
                Is.False,
                "a caller woken by a fetch it joined goes straight on to read the contents");
            Assert.That(ownerFailure, Is.Null);
            Assert.That(refreshFailure, Is.Null);
            Assert.That(readFailure, Is.Null, "the contents are the cache's before anyone is woken");
            Assert.That(read, Is.EqualTo("v"), "and what it reads is what the fetch it joined produced");
            Assert.That(fetches, Is.EqualTo(1), "which is that one fetch, not one of its own");
        });
    }

    [Test]
    public async Task ACallerHeldAtTheStalenessDecisionDoesNotFetchWhatAnotherJustMadeFreshAsync()
    {
        var deciding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetches = 0;

        var cache = Bulk(() => Interlocked.Increment(ref fetches), TimeSpan.FromMinutes(10));

        cache.OnDeciding = () =>
        {
            // One caller only: the refresh below must not wait for itself.
            cache.OnDeciding = null;
            deciding.TrySetResult();
            published.Task.GetAwaiter().GetResult();
        };

        // This caller reaches the decision on contents that have never been loaded. Everything below
        // happens before it makes that decision.
        var held = Task.Run(() => cache.RefreshIfExpiredAsync());
        Exception? refreshFailure = null;

        try
        {
            await deciding.Task.WaitAsync(Patience).ConfigureAwait(false);
            refreshFailure = await SettledAsync(cache.RefreshAsync()).ConfigureAwait(false);
        }
        finally
        {
            published.TrySetResult();
        }

        var heldFailure = await SettledAsync(held).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(refreshFailure, Is.Null);
            Assert.That(heldFailure, Is.Null);
            Assert.That(
                fetches,
                Is.EqualTo(1),
                "the staleness decision and the joining of a flight are one step, so contents made fresh "
                + "in between are seen rather than fetched again");
        });
    }

    [Test]
    public async Task ConcurrentRefreshesObserveOneFetchAsync()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetches = 0;

        var cache = _provider.GetSelfRefreshingCache<Owner, string>("bulk", async _ =>
        {
            if (Interlocked.Increment(ref fetches) == 1)
            {
                entered.SetResult();
            }

            await release.Task.ConfigureAwait(false);
            return (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["k"] = "v" };
        });

        var callers = Enumerable.Range(0, 12).Select(_ => cache.RefreshAsync()).ToArray();

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        release.SetResult();
        await Task.WhenAll(callers).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(fetches, Is.EqualTo(1), "stampeding the source is what this contract prevents");
            Assert.That(cache.Find("k"), Is.EqualTo("v"));
        });
    }

    [Test]
    public async Task ARefreshReplacesTheWholeSnapshotAtOnceAsync()
    {
        var generation = 0;

        var cache = _provider.GetSelfRefreshingCache<Owner, string>(
            "bulk",
            _ =>
            {
                generation++;
                return Task.FromResult<IReadOnlyDictionary<string, string>>(
                    generation == 1
                        ? new Dictionary<string, string> { ["a"] = "1", ["b"] = "1" }
                        : new Dictionary<string, string> { ["b"] = "2", ["c"] = "2" });
            },
            TimeSpan.FromMinutes(10));

        await cache.RefreshAsync().ConfigureAwait(false);
        await cache.RefreshAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(cache.Find("a"), Is.Null, "a key absent from the new contents is gone");
            Assert.That(cache.Find("b"), Is.EqualTo("2"));
            Assert.That(cache.Find("c"), Is.EqualTo("2"));
            Assert.That(cache.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AConditionalRefreshFetchesOnlyWhenTheContentsAreStaleAsync()
    {
        var fetches = 0;
        var cache = Cache(TimeSpan.FromMinutes(10), () => fetches++);

        await cache.RefreshIfExpiredAsync().ConfigureAwait(false);
        await cache.RefreshIfExpiredAsync().ConfigureAwait(false);

        Assert.That(fetches, Is.EqualTo(1), "contents that were never loaded are stale exactly once");

        _clock.Advance(TimeSpan.FromMinutes(11));
        await cache.RefreshIfExpiredAsync().ConfigureAwait(false);

        Assert.That(fetches, Is.EqualTo(2));
    }

    [Test]
    public async Task AForcedRefreshFetchesWhetherOrNotTheContentsAreFreshAsync()
    {
        var fetches = 0;
        var cache = Cache(TimeSpan.FromHours(1), () => fetches++);

        await cache.RefreshAsync().ConfigureAwait(false);
        await cache.RefreshAsync().ConfigureAwait(false);

        Assert.That(fetches, Is.EqualTo(2));
    }

    [Test]
    public async Task AConditionalRefreshHonorsASuppliedThresholdOverTheConfiguredOneAsync()
    {
        var fetches = 0;
        var cache = Cache(TimeSpan.FromHours(1), () => fetches++);

        await cache.RefreshAsync().ConfigureAwait(false);
        _clock.Advance(TimeSpan.FromMinutes(5));

        await cache.RefreshIfExpiredAsync().ConfigureAwait(false);
        Assert.That(fetches, Is.EqualTo(1), "the configured hour has not elapsed");

        await cache.RefreshIfExpiredAsync(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        Assert.That(fetches, Is.EqualTo(2), "the supplied threshold has");
    }

    [Test]
    public async Task TouchResetsTheStalenessClockWithoutRefetchingAsync()
    {
        var fetches = 0;
        var cache = Cache(TimeSpan.FromMinutes(10), () => fetches++);

        await cache.RefreshAsync().ConfigureAwait(false);
        _clock.Advance(TimeSpan.FromMinutes(9));

        cache.Touch();
        _clock.Advance(TimeSpan.FromMinutes(9));

        await cache.RefreshIfExpiredAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(fetches, Is.EqualTo(1));
            Assert.That(cache.IsExpired(TimeSpan.FromMinutes(10)), Is.False);
        });
    }

    [Test]
    public async Task UpdateReplacesTheContentsAndMarksThemFreshAsync()
    {
        var fetches = 0;
        var cache = Cache(TimeSpan.FromMinutes(10), () => fetches++);

        cache.Update(new Dictionary<string, string> { ["supplied"] = "value" });

        await cache.RefreshIfExpiredAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(cache.Find("supplied"), Is.EqualTo("value"));
            Assert.That(fetches, Is.Zero, "supplied contents are fresh contents");
        });
    }

    [Test]
    public async Task AMissingKeyAfterARefreshIsReportedAsOneAsync()
    {
        var cache = _provider.GetSelfRefreshingCache<Owner, string>(
            "bulk",
            _ => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()),
            TimeSpan.FromMinutes(10));

        Assert.That(() => cache.GetAsync("absent"), Throws.TypeOf<KeyNotFoundException>());
        await Task.CompletedTask.ConfigureAwait(false);
    }

    [Test]
    public async Task AFailedFetchLeavesThePreviousContentsAndDoesNotBlockTheNextAttemptAsync()
    {
        var attempts = 0;

        var cache = _provider.GetSelfRefreshingCache<Owner, string>(
            "bulk",
            _ =>
            {
                attempts++;
                return attempts == 2
                    ? Task.FromException<IReadOnlyDictionary<string, string>>(new InvalidOperationException("upstream"))
                    : Task.FromResult<IReadOnlyDictionary<string, string>>(
                        new Dictionary<string, string> { ["k"] = $"v{attempts}" });
            },
            TimeSpan.FromMinutes(10));

        await cache.RefreshAsync().ConfigureAwait(false);
        Assert.That(cache.Find("k"), Is.EqualTo("v1"));

        Assert.That(() => cache.RefreshAsync(), Throws.TypeOf<InvalidOperationException>());
        Assert.That(cache.Find("k"), Is.EqualTo("v1"), "a failed fetch is not a set of contents");

        await cache.RefreshAsync().ConfigureAwait(false);
        Assert.That(cache.Find("k"), Is.EqualTo("v3"));
    }

    [Test]
    public void ACacheWithNoLifetimeIsLoadedOnceAndThenLeftAlone()
    {
        var fetches = 0;
        var cache = _provider.GetSelfRefreshingCache<Owner, string>(
            "bulk",
            _ =>
            {
                fetches++;
                return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
            });

        Assert.DoesNotThrowAsync(() => cache.RefreshIfExpiredAsync());
        Assert.That(fetches, Is.EqualTo(1), "contents that have never been loaded are stale under any reading");

        _clock.Advance(TimeSpan.FromDays(30));
        Assert.DoesNotThrowAsync(() => cache.RefreshIfExpiredAsync());

        Assert.That(
            fetches,
            Is.EqualTo(1),
            "and with no configured threshold and none supplied, loaded contents never go stale on their own");
    }

    [Test]
    public async Task StaleContentsAreNeverReadButAreStillHeldUntilSweptAsync()
    {
        var cache = _provider.GetSelfRefreshingCache<Owner, string>(
            "bulk",
            _ => Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" }),
            TimeSpan.FromMinutes(10));

        await cache.RefreshAsync().ConfigureAwait(false);
        _clock.Advance(TimeSpan.FromMinutes(11));

        Assert.Multiple(() =>
        {
            Assert.That(cache.Find("k"), Is.Null, "contents past their lifetime are not current contents");
            Assert.That(cache.Count, Is.EqualTo(1), "and reading past them does not remove them");
        });

        Assert.That(await cache.GetAsync("k").ConfigureAwait(false), Is.EqualTo("v"), "GetAsync refreshes first");

        cache.Clear();
        Assert.That(cache.Count, Is.Zero);
    }

    [Test]
    public void ACacheWithNoLifetimeStillAnswersFromItsFetchDelegate()
    {
        var cache = _provider.GetSelfRefreshingCache<Owner, string>(
            "bulk",
            _ => Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" }));

        Assert.That(cache.GetAsync("k").GetAwaiter().GetResult(), Is.EqualTo("v"), "a cache nobody can fill is not a cache");
    }

    /// <summary>Waits for a caller to finish and reports how, rather than throwing here.</summary>
    /// <remarks>
    /// Every caller is observed even when an assertion has already failed, so a red proof leaves nothing
    /// running against a provider that is about to be disposed.
    /// </remarks>
#pragma warning disable CA1031
    private static async Task<Exception?> SettledAsync(Task caller)
    {
        try
        {
            await caller.WaitAsync(Patience).ConfigureAwait(false);
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }
#pragma warning restore CA1031

    /// <summary>The concrete cache, which is what the deterministic seams are declared on.</summary>
    private SelfRefreshingCache<string> Bulk(Action onFetch, TimeSpan? lifetime = null)
        => (SelfRefreshingCache<string>)_provider.GetSelfRefreshingCache<Owner, string>(
            "bulk",
            _ =>
            {
                onFetch();
                return Task.FromResult<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" });
            },
            lifetime);

    private ISelfRefreshingCache<string> Cache(TimeSpan lifetime, Action onFetch)
        => _provider.GetSelfRefreshingCache<Owner, string>(
            "bulk",
            _ =>
            {
                onFetch();
                return Task.FromResult<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string> { ["k"] = "v" });
            },
            lifetime);
}
