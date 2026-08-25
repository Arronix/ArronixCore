using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Hosting;
using Arronix.Common.Caching;
using Arronix.Common.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arronix.Common.Tests.Composition;

/// <summary>
/// What an ordinary host gets from one call to <c>AddArronixCommon</c>.
/// </summary>
[TestFixture]
public class PlatformServiceCompositionTests
{
    [Test]
    public void ThePlatformSuppliesTheCachingAndHostingContracts()
    {
        using var provider = Build();

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetService<ICacheProvider>(), Is.Not.Null);
            Assert.That(provider.GetService<ICacheNamespaceProvider>(), Is.Not.Null);
            Assert.That(provider.GetService<IHostRuntimeInfo>(), Is.Not.Null);
            Assert.That(provider.GetService<IOperatingSystemInfo>(), Is.Not.Null);
        });
    }

    [Test]
    public void TheTwoCacheFacesAreOneObject()
    {
        using var provider = Build();

        var extensionFacing = provider.GetRequiredService<ICacheProvider>();
        var hostFacing = provider.GetRequiredService<ICacheNamespaceProvider>();

        Assert.That(
            hostFacing,
            Is.SameAs(extensionFacing),
            "a host releasing namespaces on a different object than extensions fill would release nothing");
    }

    [Test]
    public async Task ASubstitutedProviderStaysTheOneTeardownControlsAsync()
    {
        var substitute = new NamespacedSubstitute();
        var services = new ServiceCollection();
        services.AddSingleton<ICacheProvider>(substitute);
        services.AddArronixCommon(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        var hostFacing = provider.GetRequiredService<ICacheNamespaceProvider>();

        Assert.That(provider.GetRequiredService<ICacheProvider>(), Is.SameAs(substitute));
        Assert.That(hostFacing, Is.SameAs(substitute));

        // The contract is implementable outside this assembly, not just satisfiable by identity: the
        // substitute creates a namespace, serves a cache from it, and takes it back.
        var owned = hostFacing.CreateNamespace("plugin:a");
        var cache = owned.GetCache<PlatformServiceCompositionTests, string>("titles");
        cache.Set("k", "v");

        Assert.That(cache.Find("k"), Is.EqualTo("v"));

        await owned.ReleaseAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(owned.IsReleased, Is.True);
            Assert.Throws<ObjectDisposedException>(() => cache.Find("k"));
            Assert.Throws<ObjectDisposedException>(
                () => owned.GetCache<PlatformServiceCompositionTests, string>("more"));
        });
    }

    [Test]
    public void TheRuntimeAndOperatingSystemFactsAgreeAboutContainerization()
    {
        using var provider = Build();

        Assert.That(
            provider.GetRequiredService<IHostRuntimeInfo>().IsContainerized,
            Is.EqualTo(provider.GetRequiredService<IOperatingSystemInfo>().IsContainerized));
    }

    [Test]
    public void AHostMaySubstituteItsOwnCacheProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICacheProvider, SubstituteCacheProvider>();
        services.AddArronixCommon(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<ICacheProvider>(), Is.TypeOf<SubstituteCacheProvider>());
    }

    [Test]
    public void ASubstitutedProviderWithNoNamespaceControlIsRefusedExplicitly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICacheProvider, SubstituteCacheProvider>();
        services.AddArronixCommon(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        var failure = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<ICacheNamespaceProvider>());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain(typeof(SubstituteCacheProvider).FullName!));
            Assert.That(failure.Message, Does.Contain(nameof(ICacheNamespaceProvider)));
        });
    }

    [Test]
    public void TheCacheSweepIntervalBindsFromTheOneConfigurationPrefix()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CacheOptions.SectionName}:SweepInterval"] = "00:02:00"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddArronixCommon(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.That(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CacheOptions>>().Value.SweepInterval,
            Is.EqualTo(TimeSpan.FromMinutes(2)));
    }

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddArronixCommon(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A namespace-capable substitute written entirely against the public contract, which is the proof that
    /// a host really can supply its own and keep extension teardown working.
    /// </summary>
    private sealed class NamespacedSubstitute : ICacheNamespaceProvider
    {
        private readonly CacheProvider _inner = new(TimeProvider.System);

        public ICacheNamespace CreateNamespace(string name) => new Group(_inner.CreateNamespace(name));

        public ICache<TValue> GetCache<TOwner, TValue>(string partition)
            => _inner.GetCache<TOwner, TValue>(partition);

        public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
            => _inner.GetRollingCache<TOwner, TValue>(partition, defaultLifetime);

        public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
            string partition,
            Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
            TimeSpan? lifetime = null)
            => _inner.GetSelfRefreshingCache<TOwner, TValue>(partition, fetch, lifetime);

        private sealed class Group(ICacheNamespace inner) : ICacheNamespace
        {
            public string Name => inner.Name;

            public bool IsReleased => inner.IsReleased;

            public ValueTask ReleaseAsync() => inner.ReleaseAsync();

            public ICache<TValue> GetCache<TOwner, TValue>(string partition)
                => inner.GetCache<TOwner, TValue>(partition);

            public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
                => inner.GetRollingCache<TOwner, TValue>(partition, defaultLifetime);

            public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
                string partition,
                Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
                TimeSpan? lifetime = null)
                => inner.GetSelfRefreshingCache<TOwner, TValue>(partition, fetch, lifetime);
        }
    }

    private sealed class SubstituteCacheProvider : ICacheProvider
    {
        public ICache<TValue> GetCache<TOwner, TValue>(string partition) => throw new NotSupportedException();

        public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
            => throw new NotSupportedException();

        public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
            string partition,
            Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
            TimeSpan? lifetime = null) => throw new NotSupportedException();
    }
}
