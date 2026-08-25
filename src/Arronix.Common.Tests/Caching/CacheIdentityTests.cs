using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Arronix.Abstractions.Caching;
using Arronix.Common.Caching;
using Microsoft.Extensions.Time.Testing;

namespace Arronix.Common.Tests.Caching;

/// <summary>
/// A cache is named by its namespace, its owning type's assembly-qualified identity and its partition.
/// </summary>
[TestFixture]
public class CacheIdentityTests
{
    private const string SharedFullName = "Arronix.Fixture.SameName";

    [Test]
    public void TwoOwnersSharingAFullNameInDifferentAssembliesAreTwoCaches()
    {
        var first = EmitOwner("Arronix.Fixture.CacheIdentityA");
        var second = EmitOwner("Arronix.Fixture.CacheIdentityB");

        Assert.That(first.FullName, Is.EqualTo(second.FullName), "the fixture is only meaningful if they collide");

        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        using var provider = NoSweep(clock);

        var mine = GetCache(provider, first, "titles");
        var theirs = GetCache(provider, second, "titles");

        mine.Set("k", "mine");

        Assert.Multiple(() =>
        {
            Assert.That(theirs, Is.Not.SameAs(mine), "one full name in two assemblies is two owners");
            Assert.That(theirs.Find("k"), Is.Null);
            Assert.That(provider.CacheNames, Has.Count.EqualTo(2));
            Assert.That(mine.Name, Is.Not.EqualTo(theirs.Name));
        });
    }

    [Test]
    public void TwoOwnersDifferingOnlyByAssemblyVersionAreTwoCaches()
    {
        var first = EmitOwner("Arronix.Fixture.CacheIdentityVersioned", new Version(1, 0));
        var second = EmitOwner("Arronix.Fixture.CacheIdentityVersioned", new Version(2, 0));

        Assert.That(
            first.Assembly.GetName().Name,
            Is.EqualTo(second.Assembly.GetName().Name),
            "the fixture is only meaningful if the simple names collide");
        Assert.That(first.Assembly.FullName, Is.Not.EqualTo(second.Assembly.FullName));

        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        using var provider = NoSweep(clock);

        var mine = GetCache(provider, first, "titles");
        var theirs = GetCache(provider, second, "titles");

        mine.Set("k", "mine");

        Assert.Multiple(() =>
        {
            Assert.That(theirs, Is.Not.SameAs(mine), "the name carries the exact assembly identity");
            Assert.That(theirs.Find("k"), Is.Null);
            Assert.That(provider.CacheNames, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void ACacheNameCarriesItsNamespaceOwnerIdentityAndPartition()
    {
        var name = CacheProvider.NameOf(typeof(CacheIdentityTests), "titles", "plugin:a");

        Assert.Multiple(() =>
        {
            Assert.That(name, Does.StartWith("plugin:a/"));
            Assert.That(name, Does.Contain(typeof(CacheIdentityTests).FullName!));
            Assert.That(name, Does.Contain(typeof(CacheIdentityTests).Assembly.FullName!));
            Assert.That(name, Does.EndWith(":titles"));
        });
    }

    private static CacheProvider NoSweep(TimeProvider clock)
        => new(clock, Microsoft.Extensions.Options.Options.Create(new CacheOptions { SweepInterval = TimeSpan.Zero }));

    private static ICache<string> GetCache(CacheProvider provider, Type owner, string partition)
    {
        var method = typeof(CacheProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(candidate =>
                candidate.Name == nameof(CacheProvider.GetCache)
                && candidate.GetParameters().Length == 1);

        return (ICache<string>)method
            .MakeGenericMethod(owner, typeof(string))
            .Invoke(provider, [partition])!;
    }

    /// <summary>Emits a type whose full name is shared, in an assembly of its own.</summary>
    private static Type EmitOwner(string assemblyName, Version? version = null)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(assemblyName) { Version = version },
            AssemblyBuilderAccess.RunAndCollect);

        return assembly
            .DefineDynamicModule(assemblyName)
            .DefineType(SharedFullName, TypeAttributes.Public | TypeAttributes.Class)
            .CreateType();
    }
}
