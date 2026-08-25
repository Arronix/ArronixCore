using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Common.Caching;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Arronix.Common.Tests.Caching;

/// <summary>
/// What disposing the provider itself waits for, and what it refuses while it waits.
/// </summary>
/// <remarks>
/// The host's own unscoped caches have the same lifetime as an extension's namespaced ones. A provider that
/// returned while a factory it admitted was still running would report itself disposed while code it handed
/// a cache to was still using it.
/// </remarks>
[TestFixture]
public class CacheProviderDisposalTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private sealed class Owner;

    [Test]
    public async Task DisposalWaitsForAnUnscopedFactoryAndRefusesEverythingMeanwhileAsync()
    {
        var provider = new CacheProvider(new FakeTimeProvider(DateTimeOffset.UnixEpoch), Quiet());
        var cache = provider.GetCache<Owner, string>("titles");

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var caller = Task.Run(() => cache.Get("k", () =>
        {
            entered.Set();
            release.Wait(Patience);
            return "v";
        }));

        Assert.That(entered.Wait(Patience), Is.True, "the factory has to be running for the test to mean anything");

        var disposal = provider.DisposeAsync().AsTask();

        Assert.Multiple(() =>
        {
            Assert.That(disposal.Wait(TimeSpan.FromMilliseconds(200)), Is.False, "a factory is still running");
            Assert.That(() => cache.Find("k"), Throws.TypeOf<ObjectDisposedException>(), "nothing new starts");
            Assert.That(() => provider.CreateNamespace("plugin/late"), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => provider.GetCache<Owner, string>("more"), Throws.TypeOf<ObjectDisposedException>());
        });

        release.Set();
        await caller.WaitAsync(Patience).ConfigureAwait(false);
        await disposal.WaitAsync(Patience).ConfigureAwait(false);

        Assert.That(provider.CacheNames, Is.Empty, "and everything it held is let go");
    }

    [Test]
    public async Task DisposalWaitsForABulkFetchTooAsync()
    {
        var provider = new CacheProvider(new FakeTimeProvider(DateTimeOffset.UnixEpoch), Quiet());

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var cache = provider.GetSelfRefreshingCache<Owner, string>(
            "bulk",
            async _ =>
            {
                entered.Set();
                await Task.Run(() => release.Wait(Patience)).ConfigureAwait(false);
                return (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["k"] = "v" };
            });

        var refreshing = cache.RefreshAsync();
        Assert.That(entered.Wait(Patience), Is.True);

        var disposal = provider.DisposeAsync().AsTask();

        Assert.Multiple(() =>
        {
            Assert.That(disposal.Wait(TimeSpan.FromMilliseconds(200)), Is.False, "a fetch is still running");
            Assert.That(() => cache.Find("k"), Throws.TypeOf<ObjectDisposedException>());
        });

        release.Set();
        await refreshing.WaitAsync(Patience).ConfigureAwait(false);
        await disposal.WaitAsync(Patience).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(() => cache.Count, Throws.TypeOf<ObjectDisposedException>(), "the cache is released, not merely idle");
            Assert.That(provider.CacheNames, Is.Empty, "and the provider holds nothing that could still be read");
        });
    }

    [Test]
    public void ANamespaceCannotBePublishedIntoADisposedProvider()
    {
        var provider = new CacheProvider(new FakeTimeProvider(DateTimeOffset.UnixEpoch), Quiet());
        provider.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(() => provider.CreateNamespace("plugin/movies/1"), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => provider.GetCache<Owner, string>("titles"), Throws.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public async Task ACacheResolutionAdmittedAsDisposalBeginsIsWaitedForRatherThanStrandedAsync()
    {
        var provider = new CacheProvider(new FakeTimeProvider(DateTimeOffset.UnixEpoch), Quiet());

        using var admitted = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();

        // Inside the resolution, after its admission is taken: exactly the window a check-then-add leaves.
        provider.OnResolutionAdmitted = () =>
        {
            admitted.Set();
            proceed.Wait(Patience);
        };

        var resolving = Task.Run(() => provider.GetCache<Owner, string>("titles"));
        Assert.That(admitted.Wait(Patience), Is.True);

        provider.OnResolutionAdmitted = null;
        var disposal = provider.DisposeAsync().AsTask();

        Assert.That(disposal.Wait(TimeSpan.FromMilliseconds(200)), Is.False, "a resolution is still in flight");

        proceed.Set();
        await resolving.WaitAsync(Patience).ConfigureAwait(false);
        await disposal.WaitAsync(Patience).ConfigureAwait(false);

        Assert.That(provider.CacheNames, Is.Empty, "the cache that landed during disposal is released with the rest");
    }

    [Test]
    public async Task EveryDisposalCallerAwaitsTheSameCompletionAsync()
    {
        var provider = new CacheProvider(new FakeTimeProvider(DateTimeOffset.UnixEpoch), Quiet());
        var cache = provider.GetCache<Owner, string>("titles");

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var caller = Task.Run(() => cache.Get("k", () =>
        {
            entered.Set();
            release.Wait(Patience);
            return "v";
        }));

        Assert.That(entered.Wait(Patience), Is.True);

        var callers = Enumerable.Range(0, 8).Select(_ => provider.DisposeAsync().AsTask()).ToArray();

        Assert.That(
            Task.WhenAny(callers).Wait(TimeSpan.FromMilliseconds(200)),
            Is.False,
            "a later caller that returned early would report the provider disposed while a factory was running");

        release.Set();
        await caller.WaitAsync(Patience).ConfigureAwait(false);
        await Task.WhenAll(callers).WaitAsync(Patience).ConfigureAwait(false);

        Assert.That(provider.CacheNames, Is.Empty);
    }

    [Test]
    public async Task ANamespaceAdmittedAsDisposalBeginsIsPublishedAndThenReleasedWithTheRestAsync()
    {
        var provider = new CacheProvider(new FakeTimeProvider(DateTimeOffset.UnixEpoch), Quiet());

        using var admitted = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();

        // Inside creation, after its root ticket is taken and before it is published: the window in which a
        // namespace exists but no snapshot of the table can see it.
        provider.OnCreationAdmitted = () =>
        {
            admitted.Set();
            proceed.Wait(Patience);
        };

        ICacheNamespace? late = null;
        var creating = Task.Run(() => late = provider.CreateNamespace("plugin/movies/1"));
        Assert.That(admitted.Wait(Patience), Is.True);

        provider.OnCreationAdmitted = null;
        var disposal = provider.DisposeAsync().AsTask();

        Assert.That(
            disposal.Wait(TimeSpan.FromMilliseconds(200)),
            Is.False,
            "a namespace this provider already admitted is still being published");

        proceed.Set();
        await creating.WaitAsync(Patience).ConfigureAwait(false);
        await disposal.WaitAsync(Patience).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(late, Is.Not.Null, "it was admitted, so it is published rather than refused");
            Assert.That(late!.IsReleased, Is.True, "and released with everything else the provider held");
            Assert.That(provider.CacheNames, Is.Empty);
        });
    }

    /// <summary>A provider whose sweep timer is off, so only the test's own actions move it.</summary>
    private static IOptions<CacheOptions> Quiet()
        => Options.Create(new CacheOptions { SweepInterval = TimeSpan.Zero });
}
