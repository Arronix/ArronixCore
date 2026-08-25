using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arronix.Common.Caching;
using Microsoft.Extensions.Time.Testing;

namespace Arronix.Common.Tests.Caching;

/// <summary>
/// The release sequence a host depends on before it unloads the extension that filled a namespace.
/// </summary>
/// <remarks>
/// Every test here is about a claim teardown makes on the strength of release having returned: nothing is
/// running, nothing is held, and nothing new can start. A test that only counted dictionary entries would
/// pass against an implementation that dropped its references while a factory was still producing into it.
/// </remarks>
[TestFixture]
public class CacheNamespaceTests
{
    private sealed class Owner;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private FakeTimeProvider _clock = null!;
    private CacheProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        _provider = new CacheProvider(_clock);
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    [Test]
    public async Task ReleaseRemovesEveryCacheInTheNamespaceAndNothingElseAsync()
    {
        var mine = _provider.CreateNamespace("plugin:a");
        var theirs = _provider.CreateNamespace("plugin:b");

        mine.GetCache<Owner, string>("plugin:a:titles").Set("k", "v");
        theirs.GetCache<Owner, string>("plugin:b:titles").Set("k", "v");
        _provider.GetCache<Owner, string>("host:titles").Set("k", "v");

        Assert.That(_provider.CacheNames, Has.Count.EqualTo(3));

        await mine.ReleaseAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(
                _provider.CacheNames.Any(name => name.Contains("plugin:a", StringComparison.Ordinal)),
                Is.False);
            Assert.That(_provider.CacheNames, Has.Count.EqualTo(2));
            Assert.That(mine.CacheCount, Is.Zero);
            Assert.That(theirs.CacheCount, Is.EqualTo(1), "another extension's namespace is untouched");
        });
    }

    [Test]
    public async Task EveryOperationOnAReleasedCacheIsRefusedAsync()
    {
        var owned = _provider.CreateNamespace("plugin:a");
        var keyed = owned.GetCache<Owner, string>("plugin:a:titles");
        var bulk = owned.GetSelfRefreshingCache<Owner, string>(
            "plugin:a:bulk",
            _ => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));

        keyed.Set("k", "v");

        await owned.ReleaseAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => keyed.Set("k", "v"));
            Assert.Throws<ObjectDisposedException>(() => keyed.Find("k"));
            Assert.Throws<ObjectDisposedException>(() => keyed.Get("k", () => "v"));
            Assert.ThrowsAsync<ObjectDisposedException>(() => keyed.GetAsync("k", _ => Task.FromResult("v")));
            Assert.Throws<ObjectDisposedException>(() => _ = keyed.Count);
            Assert.Throws<ObjectDisposedException>(() => _ = keyed.Values);
            Assert.Throws<ObjectDisposedException>(keyed.Clear);
            Assert.Throws<ObjectDisposedException>(keyed.ClearExpired);
            Assert.Throws<ObjectDisposedException>(() => keyed.Remove("k"));

            Assert.Throws<ObjectDisposedException>(() => bulk.Find("k"));
            Assert.ThrowsAsync<ObjectDisposedException>(() => bulk.GetAsync("k"));
            Assert.ThrowsAsync<ObjectDisposedException>(() => bulk.RefreshAsync());
            Assert.ThrowsAsync<ObjectDisposedException>(() => bulk.RefreshIfExpiredAsync());
            Assert.Throws<ObjectDisposedException>(
                () => bulk.Update(new Dictionary<string, string> { ["k"] = "v" }));
            Assert.Throws<ObjectDisposedException>(bulk.Touch);
            Assert.Throws<ObjectDisposedException>(() => bulk.IsExpired(TimeSpan.FromMinutes(1)));
            Assert.Throws<ObjectDisposedException>(() => _ = bulk.Count);
            Assert.Throws<ObjectDisposedException>(bulk.Clear);
            Assert.Throws<ObjectDisposedException>(bulk.ClearExpired);
            Assert.Throws<ObjectDisposedException>(() => bulk.Remove("k"));

            Assert.That(keyed.Name, Is.Not.Empty, "the name stays readable, because it holds nothing");
        });
    }

    [Test]
    public async Task ReleaseDoesNotCompleteWhileAFactoryIsStillRunningAsync()
    {
        var owned = _provider.CreateNamespace("plugin:a");
        var cache = owned.GetCache<Owner, string>("plugin:a:titles");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var producing = cache.GetAsync("k", async _ =>
        {
            entered.SetResult();
            await release.Task.ConfigureAwait(false);
            return "produced";
        });

        await entered.Task.WaitAsync(Patience).ConfigureAwait(false);

        var releasing = owned.ReleaseAsync().AsTask();
        var raced = await Task.WhenAny(releasing, Task.Delay(TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);

        Assert.That(raced, Is.Not.SameAs(releasing), "release must wait for the factory it admitted");

        release.SetResult();

        Assert.That(await producing.ConfigureAwait(false), Is.EqualTo("produced"));
        await releasing.WaitAsync(Patience).ConfigureAwait(false);

        Assert.That(owned.CacheCount, Is.Zero);
    }

    [Test]
    public async Task ReleaseDoesNotCompleteWhileABulkFetchIsStillRunningAsync()
    {
        var owned = _provider.CreateNamespace("plugin:a");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var cache = owned.GetSelfRefreshingCache<Owner, string>(
            "plugin:a:bulk",
            async _ =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["k"] = "v" };
            });

        var fetching = cache.RefreshAsync();
        await entered.Task.WaitAsync(Patience).ConfigureAwait(false);

        var releasing = owned.ReleaseAsync().AsTask();
        var raced = await Task.WhenAny(releasing, Task.Delay(TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);

        Assert.That(raced, Is.Not.SameAs(releasing), "release must wait for the fetch it admitted");

        release.SetResult();
        await fetching.ConfigureAwait(false);
        await releasing.WaitAsync(Patience).ConfigureAwait(false);

        Assert.Throws<ObjectDisposedException>(() => cache.Find("k"), "and the fetched values are gone");
    }

    [Test]
    public async Task ConcurrentReleaseCallersAllAwaitTheSameCompletionAsync()
    {
        var owned = _provider.CreateNamespace("plugin:a");
        var cache = owned.GetCache<Owner, string>("plugin:a:titles");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var producing = cache.GetAsync("k", async _ =>
        {
            entered.SetResult();
            await release.Task.ConfigureAwait(false);
            return "produced";
        });

        await entered.Task.WaitAsync(Patience).ConfigureAwait(false);

        var releases = Enumerable.Range(0, 4).Select(_ => owned.ReleaseAsync().AsTask()).ToArray();

        Assert.That(
            releases.Any(task => task.IsCompleted),
            Is.False,
            "a second caller must not report a namespace released while the first is still draining");

        release.SetResult();
        await producing.ConfigureAwait(false);
        await Task.WhenAll(releases).WaitAsync(Patience).ConfigureAwait(false);

        Assert.That(releases.All(task => task.IsCompletedSuccessfully), Is.True);
    }

    [Test]
    public async Task ResolvingACacheRacesReleaseToOneOfTwoOutcomesAsync()
    {
        var owned = _provider.CreateNamespace("plugin:a");
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolving = Task.Run(async () =>
        {
            await barrier.Task.ConfigureAwait(false);

            for (var attempt = 0; attempt < 200; attempt++)
            {
                try
                {
                    owned.GetCache<Owner, string>($"plugin:a:p{attempt}");
                }
                catch (ObjectDisposedException)
                {
                    return attempt;
                }
            }

            return -1;
        });

        var releasing = Task.Run(async () =>
        {
            await barrier.Task.ConfigureAwait(false);
            await owned.ReleaseAsync().ConfigureAwait(false);
        });

        barrier.SetResult();

        var refusedAt = await resolving.WaitAsync(Patience).ConfigureAwait(false);
        await releasing.WaitAsync(Patience).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(refusedAt, Is.Not.EqualTo(-1), "release must eventually refuse new resolutions");
            Assert.That(owned.CacheCount, Is.Zero, "and nothing resolved before it may survive release");
            Assert.That(
                _provider.CacheNames.Any(name => name.Contains(":plugin:a:p", StringComparison.Ordinal)),
                Is.False,
                "including a cache registered while release was closing");
        });
    }

    [Test]
    public async Task TheSweepDoesNotThrowWhileANamespaceIsDrainingAsync()
    {
        var owned = _provider.CreateNamespace("plugin:a");
        var cache = owned.GetCache<Owner, string>("plugin:a:titles");
        cache.Set("expired", "v", TimeSpan.FromMinutes(1));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var producing = cache.GetAsync("k", async _ =>
        {
            entered.SetResult();
            await release.Task.ConfigureAwait(false);
            return "produced";
        });

        await entered.Task.WaitAsync(Patience).ConfigureAwait(false);

        // Closed to new work, still registered with the provider, and still draining: the exact window in
        // which a timer callback would otherwise throw out of a thread nobody is watching.
        var releasing = owned.ReleaseAsync().AsTask();

        Assert.Multiple(() =>
        {
            Assert.That(owned.IsReleased, Is.True);
            Assert.That(_provider.CacheNames, Is.Not.Empty, "the cache has not been removed yet");
            Assert.DoesNotThrow(_provider.SweepExpired);
            Assert.DoesNotThrow(
                () => _clock.Advance(CacheOptions.DefaultSweepInterval + TimeSpan.FromMinutes(1)));
        });

        release.SetResult();
        await producing.ConfigureAwait(false);
        await releasing.WaitAsync(Patience).ConfigureAwait(false);

        Assert.DoesNotThrow(_provider.SweepExpired);
    }

    [Test]
    public async Task ANameIsReusableOnlyAfterTheNamespaceHoldingItIsReleasedAsync()
    {
        var first = _provider.CreateNamespace("plugin:a");

        Assert.Throws<InvalidOperationException>(() => _provider.CreateNamespace("plugin:a"));

        await first.ReleaseAsync().ConfigureAwait(false);

        var second = _provider.CreateNamespace("plugin:a");

        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public async Task ANamespaceHandsOutNoFurtherCachesOnceReleasedAsync()
    {
        var owned = _provider.CreateNamespace("plugin:a");
        await owned.ReleaseAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(owned.IsReleased, Is.True);
            Assert.Throws<ObjectDisposedException>(() => owned.GetCache<Owner, string>("plugin:a:titles"));
            Assert.Throws<ObjectDisposedException>(
                () => owned.GetRollingCache<Owner, string>("plugin:a:sessions", TimeSpan.FromMinutes(1)));
            Assert.Throws<ObjectDisposedException>(
                () => owned.GetSelfRefreshingCache<Owner, string>(
                    "plugin:a:bulk",
                    _ => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>())));
        });
    }

    [Test]
    public void TwoNamespacesMayNotShareOneOwnerAndPartition()
    {
        var mine = _provider.CreateNamespace("plugin:a");
        var theirs = _provider.CreateNamespace("plugin:b");

        _ = mine.GetCache<Owner, string>("shared");

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => theirs.GetCache<Owner, string>("shared"));
            Assert.Throws<InvalidOperationException>(() => _provider.GetCache<Owner, string>("shared"));
        });
    }
}
