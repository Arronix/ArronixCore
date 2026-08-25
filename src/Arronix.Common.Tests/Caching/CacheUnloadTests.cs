using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Arronix.Abstractions.Caching;
using Arronix.Common.Caching;
using Microsoft.Extensions.Options;

namespace Arronix.Common.Tests.Caching;

/// <summary>
/// What a released namespace and a disposed provider actually let go of: the extension's load context, its
/// types, its values and its delegates.
/// </summary>
/// <remarks>
/// <para>
/// The extension here is this assembly loaded a second time into a collectible context of its own, so its
/// types are genuinely different types in a context that can be unloaded. Counting entries would prove
/// nothing: the provider is a singleton that outlives every extension, so the only honest assertion is that
/// the context unloads while that provider is still alive.
/// </para>
/// <para>
/// Every fixture runs on a thread that then exits, and the collection is measured from another frame. A
/// reference left in a live thread's dead stack slot is not something the provider holds, and the collector
/// cannot tell the difference — which is exactly why the harness proves itself first.
/// </para>
/// </remarks>
[TestFixture]
public class CacheUnloadTests
{
    [Test]
    public void TheHarnessUnloadsAContextNothingEverTouched()
    {
        // The control. If this fails, no other assertion in this file means anything.
        Assert.That(Collected(OnItsOwnThread(static () => Load().Context)), Is.True);
    }

    [Test]
    public void AReleasedNamespaceStopsPinningTheExtensionThatFilledIt()
    {
        using var provider = new CacheProvider(TimeProvider.System, Quiet());

        var extension = OnItsOwnThread(() => FillAndRelease(provider));

        Assert.Multiple(() =>
        {
            Assert.That(
                Collected(extension),
                Is.True,
                "a provider that outlives an extension must not keep its types, values or delegates");
            Assert.That(Reaching(provider), Is.Empty, "and it names none of them either");
        });

        GC.KeepAlive(provider);
    }

    [Test]
    public void ADisposedProviderStopsPinningTheNamespaceNobodyReleased()
    {
        var provider = new CacheProvider(TimeProvider.System, Quiet());

        var extension = OnItsOwnThread(() => FillAndDispose(provider));

        Assert.Multiple(() =>
        {
            Assert.That(
                Collected(extension),
                Is.True,
                "disposal takes back a namespace nobody released, and lets its extension unload");
            Assert.That(Reaching(provider), Is.Empty, "and the disposed provider names none of its types");
        });

        GC.KeepAlive(provider);
    }

    [Test]
    public void AReleasedCacheLetsGoOfItsValuesEvenWhileSomebodyStillHoldsIt()
    {
        using var provider = new CacheProvider(TimeProvider.System, Quiet());
        var space = provider.CreateNamespace("plugin/holder/1");
        var cache = space.GetCache<CacheUnloadTests, object>("titles");

        var stored = Stored(cache);
        space.ReleaseAsync().AsTask().GetAwaiter().GetResult();

        Assert.That(
            Collected(stored),
            Is.True,
            "release drops the values; the handle itself may outlive the namespace that served it");

        GC.KeepAlive(cache);
    }

    /// <summary>Fills a namespace, releases it, unloads the extension, and hands back only a weak reference.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference FillAndRelease(CacheProvider provider)
    {
        var extension = Fill(provider);
        extension.Space.ReleaseAsync().AsTask().GetAwaiter().GetResult();
        extension.Live.Unload();
        return extension.Context;
    }

    /// <summary>The same, except that the provider's own disposal is what takes the live namespace back.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference FillAndDispose(CacheProvider provider)
    {
        var extension = Fill(provider);
        provider.Dispose();
        extension.Live.Unload();
        return extension.Context;
    }

    /// <summary>
    /// Gives one extension a namespace and fills it: a keyed cache and a rolling cache closed over the
    /// extension's own owner and value types, values of that type, a factory, and a self-refreshing cache
    /// whose fetch delegate is closed over them too.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Extension Fill(CacheProvider provider)
    {
        var loaded = Load();
        var space = provider.CreateNamespace("plugin/" + loaded.Live.Name + "/1");

        typeof(CacheUnloadTests)
            .GetMethod(nameof(FillTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(loaded.Owner, loaded.Value)
            .Invoke(null, [space]);

        return loaded with { Space = space };
    }

    /// <summary>
    /// Everything the extension would do with its caches, written once in ordinary typed code so that only
    /// one call has to go through reflection.
    /// </summary>
    private static void FillTyped<TOwner, TValue>(ICacheNamespace space)
        where TValue : class, new()
    {
        var keyed = space.GetCache<TOwner, TValue>("titles");
        keyed.Set("k", new TValue());
        keyed.Get("factory", static () => new TValue());

        var rolling = space.GetRollingCache<TOwner, TValue>("rolling", TimeSpan.FromHours(1));
        rolling.Set("k", new TValue());

        var bulk = space.GetSelfRefreshingCache<TOwner, TValue>(
            "bulk",
            static _ => Task.FromResult<IReadOnlyDictionary<string, TValue>>(
                new Dictionary<string, TValue>(StringComparer.Ordinal) { ["k"] = new() }));

        // Supplied rather than fetched. The fetch delegate above is still the extension's and is still what
        // the cache holds; a refresh would additionally leave a continuation closed over the extension's
        // types parked on a pool worker, which measures the runtime rather than this provider. The
        // in-flight refresh path has its own deterministic disposal tests.
        bulk.Update(new Dictionary<string, TValue>(StringComparer.Ordinal) { ["k"] = new() });

        Require(keyed.Count == 2, "the keyed cache did not take both entries");
        Require(bulk.Find("k") is not null, "the supplied contents did not land");
    }

    /// <summary>Loads this assembly again, into a collectible context of its own.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Extension Load()
    {
        var context = new AssemblyLoadContext("extension-" + Guid.CreateVersion7().ToString("N"), isCollectible: true);
        using var image = new MemoryStream(File.ReadAllBytes(typeof(CacheUnloadTests).Assembly.Location));

        var assembly = context.LoadFromStream(image);
        var owner = assembly.GetType(typeof(CacheOwnerProbe).FullName!)!;
        var value = assembly.GetType(typeof(CacheValueProbe).FullName!)!;

        // Checked without NUnit: a constraint's actual value would be one of these types or the context
        // itself, and the framework holds what it was given until the test ends — which is what is measured.
        Require(owner.Assembly.IsCollectible, "the fixture proves nothing if the context cannot be unloaded");
        Require(!ReferenceEquals(owner, typeof(CacheOwnerProbe)), "the probe type came from the default context");

        return new Extension(new WeakReference(context), context, owner, value, null!);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Stored(ICache<object> cache)
    {
        var value = new object();
        cache.Set("k", value);
        return new WeakReference(value);
    }

    /// <summary>Runs the fixture on a thread that then exits, so no live stack can hold what it touched.</summary>
    private static WeakReference OnItsOwnThread(Func<WeakReference> fixture)
    {
        WeakReference produced = new(null);
        var worker = new Thread(() => produced = fixture());
        worker.Start();
        worker.Join();
        return produced;
    }

    /// <summary>Whether the collector could take it, which is the only real proof of release.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Collected(WeakReference extension)
    {
        for (var attempt = 0; attempt < 20 && extension.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return !extension.IsAlive;
    }

    /// <summary>
    /// Every object reachable from a root that names collectible code. Supplementary: it says what is
    /// holding on when an unload assertion fails, and it cannot see runtime, JIT or execution-context
    /// roots, so it is never the acceptance evidence on its own.
    /// </summary>
    private static IReadOnlyList<string> Reaching(object root)
    {
        var found = new List<string>();
        Walk(root, "provider", found, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
        return found;
    }

    private static void Walk(object? value, string path, List<string> found, HashSet<object> seen, int depth)
    {
        if (value is null or string || depth > 24 || !seen.Add(value))
        {
            return;
        }

        if (Collectible(value.GetType()) || (value is Type carried && Collectible(carried)))
        {
            found.Add($"{path}: {value}");
            return;
        }

        if (value is System.Collections.IEnumerable sequence)
        {
            var index = 0;

            foreach (var item in sequence)
            {
                Walk(
                    item is System.Collections.DictionaryEntry entry ? entry.Value : item,
                    $"{path}[{index++}]",
                    found,
                    seen,
                    depth + 1);
            }
        }

        foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            Walk(Read(field, value), $"{path}.{field.Name}", found, seen, depth + 1);
        }
    }

    private static object? Read(FieldInfo field, object value)
    {
        try
        {
            return field.GetValue(value);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static bool Collectible(Type type)
        => type.Assembly.IsCollectible
            || (type.HasElementType && Collectible(type.GetElementType()!))
            || type.GenericTypeArguments.Any(Collectible);

    /// <summary>A check whose failure carries only text, so nothing the extension owns reaches the framework.</summary>
    private static void Require(bool held, string what)
    {
        if (!held)
        {
            throw new InvalidOperationException(what);
        }
    }

    private static IOptions<CacheOptions> Quiet()
        => Options.Create(new CacheOptions { SweepInterval = TimeSpan.Zero });

    /// <summary>One extension: its context, the types it declares, and the namespace it was given.</summary>
    private sealed record Extension(
        WeakReference Context,
        AssemblyLoadContext Live,
        Type Owner,
        Type Value,
        ICacheNamespace Space);
}
