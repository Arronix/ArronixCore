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
        var mine = (CacheNamespace)_provider.CreateNamespace("plugin:a");
        var theirs = (CacheNamespace)_provider.CreateNamespace("plugin:b");

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
        var owned = (CacheNamespace)_provider.CreateNamespace("plugin:a");
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
        var owned = (CacheNamespace)_provider.CreateNamespace("plugin:a");
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

        Assert.That(releasing.IsCompleted, Is.False, "release must wait for the factory it admitted");

        release.SetResult();

        Assert.That(await producing.ConfigureAwait(false), Is.EqualTo("produced"));
        await releasing.WaitAsync(Patience).ConfigureAwait(false);

        Assert.That(owned.CacheCount, Is.Zero);
    }

    [Test]
    public async Task ReleaseDoesNotCompleteWhileABulkFetchIsStillRunningAsync()
    {
        var owned = (CacheNamespace)_provider.CreateNamespace("plugin:a");
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

        Assert.That(releasing.IsCompleted, Is.False, "release must wait for the fetch it admitted");

        release.SetResult();
        await fetching.ConfigureAwait(false);
        await releasing.WaitAsync(Patience).ConfigureAwait(false);

        Assert.Throws<ObjectDisposedException>(() => cache.Find("k"), "and the fetched values are gone");
    }

    [Test]
    public async Task ConcurrentReleaseCallersAllAwaitTheSameCompletionAsync()
    {
        var owned = (CacheNamespace)_provider.CreateNamespace("plugin:a");
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
    public async Task AResolutionAdmittedBeforeTheCloseIsWaitedForAndThenRemovedAsync()
    {
        var owned = (CacheNamespace)_provider.CreateNamespace("plugin:a");
        Task? releasing = null;

        // Begins release from inside the resolution that already holds an admission, so the ordering is
        // fixed rather than left to whichever task the scheduler runs first.
        _provider.OnResolutionAdmitted = () =>
        {
            _provider.OnResolutionAdmitted = null;
            releasing = owned.ReleaseAsync().AsTask();

            Assert.That(owned.IsReleased, Is.True, "the close lands before this resolution finishes");
            Assert.That(releasing.IsCompleted, Is.False, "and release cannot finish while it holds one");
        };

        var cache = owned.GetCache<Owner, string>("plugin:a:mid-flight");

        await releasing!.WaitAsync(Patience).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(owned.CacheCount, Is.Zero);
            Assert.That(_provider.CacheNames, Is.Empty, "a cache registered under the close is still removed");
            Assert.Throws<ObjectDisposedException>(() => cache.Find("k"));
        });
    }

    [Test]
    public async Task AResolutionAttemptedAfterTheCloseIsRefusedAsync()
    {
        var owned = (CacheNamespace)_provider.CreateNamespace("plugin:a");
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

        Assert.Multiple(() =>
        {
            Assert.That(releasing.IsCompleted, Is.False);
            Assert.Throws<ObjectDisposedException>(() => owned.GetCache<Owner, string>("plugin:a:after"));
        });

        release.SetResult();
        await producing.ConfigureAwait(false);
        await releasing.WaitAsync(Patience).ConfigureAwait(false);

        Assert.That(_provider.CacheNames, Is.Empty);
    }

    [Test]
    public async Task TheSweepDoesNotThrowWhileANamespaceIsDrainingAsync()
    {
        var owned = (CacheNamespace)_provider.CreateNamespace("plugin:a");
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
        var first = (CacheNamespace)_provider.CreateNamespace("plugin:a");

        Assert.Throws<InvalidOperationException>(() => _provider.CreateNamespace("plugin:a"));

        await first.ReleaseAsync().ConfigureAwait(false);

        var second = (CacheNamespace)_provider.CreateNamespace("plugin:a");

        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public async Task ANamespaceHandsOutNoFurtherCachesOnceReleasedAsync()
    {
        var owned = (CacheNamespace)_provider.CreateNamespace("plugin:a");
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
    public async Task TwoNamespacesAskingForOneOwnerAndPartitionGetTwoIndependentCachesAsync()
    {
        var mine = (CacheNamespace)_provider.CreateNamespace("plugin:a");
        var theirs = (CacheNamespace)_provider.CreateNamespace("plugin:b");

        var ours = mine.GetCache<Owner, string>("shared");
        var yours = theirs.GetCache<Owner, string>("shared");

        ours.Set("k", "mine");

        Assert.Multiple(() =>
        {
            Assert.That(yours, Is.Not.SameAs(ours), "the namespace is part of a cache's identity");
            Assert.That(yours.Find("k"), Is.Null);
        });

        await mine.ReleaseAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => ours.Find("k"));
            Assert.That(yours.Find("k"), Is.Null, "the other namespace is untouched");
            Assert.That(theirs.CacheCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void AnUnscopedCacheAndANamespacedOneDoNotCollide()
    {
        var owned = (CacheNamespace)_provider.CreateNamespace("plugin:a");

        var scoped = owned.GetCache<Owner, string>("shared");
        var unscoped = _provider.GetCache<Owner, string>("shared");

        Assert.That(unscoped, Is.Not.SameAs(scoped));
    }
}
