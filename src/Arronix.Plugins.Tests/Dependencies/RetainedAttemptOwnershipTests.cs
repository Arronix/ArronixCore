using System.IO;
using System.Runtime.CompilerServices;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using Arronix.Plugins.Versioning;

namespace Arronix.Plugins.Tests.Dependencies;

/// <summary>
/// What a retained attempt actually retains.
/// </summary>
/// <remarks>
/// The platform tells an operator that a package whose cleanup failed may still be resident and that its
/// identifier is occupied for the life of the process. Rooting only the receipt would make that a claim
/// about a description: the receipt says what the attempt was, and the lifetime is what holds its load
/// context, its instances and its contract hold. Either the registry roots the lifetime or the statement
/// is false.
/// </remarks>
[TestFixture]
public sealed class RetainedAttemptOwnershipTests
{
    private static readonly PluginId Package = PluginId.FromString("retained.fixture");

    private string _root = string.Empty;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("arronix-retained").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void ARetainedAttemptKeepsItsLifetimeAndEverythingUnderItReachable()
    {
        var dependencies = new PackageDependencyRegistry(new PluginPublicationGate());

        var (lifetime, activated) = Retain(dependencies, retain: true);

        Assert.Multiple(() =>
        {
            Assert.That(Collected(lifetime), Is.False, "the lifetime that owns the code is rooted");
            Assert.That(Collected(activated), Is.False, "and so is what the host activated under it");
        });

        GC.KeepAlive(dependencies);
    }

    [Test]
    public void AnAttemptNobodyRetainedIsNotKeptAliveByTheRegistry()
    {
        // The control. Without it the assertion above would pass on any registry, including one that keeps
        // every attempt it has ever seen.
        var dependencies = new PackageDependencyRegistry(new PluginPublicationGate());

        var (lifetime, activated) = Retain(dependencies, retain: false);

        Assert.Multiple(() =>
        {
            Assert.That(Collected(lifetime), Is.True);
            Assert.That(Collected(activated), Is.True);
        });

        GC.KeepAlive(dependencies);
    }

    /// <summary>
    /// Builds one package lifetime with a host-activated object under it, optionally retains it as an
    /// attempt whose cleanup failed, and hands back only weak references.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private (WeakReference Lifetime, WeakReference Activated) Retain(
        PackageDependencyRegistry dependencies,
        bool retain)
    {
        var package = new InstalledPackage(
            Package,
            SemanticVersion.Parse("1.0.0"),
            Path.Combine(_root, "plugin.json"),
            _root,
            entryAssemblyFileName: "Retained.Entry.dll");

        var receipt = new PackageAdmissionReceipt(package, []);
        var lease = new PackageAdmissionLease(receipt, PackageContractScope.Empty(Package));

        var ledger = new PluginRegistrationLedger(Package);
        var activated = new Activated();
        ledger.RecordHostActivation(activated);

        lease.AttachRuntime(new PluginRuntimeLease(Context(), ledger, module: null));

        if (retain)
        {
            dependencies.RetainFailedAttempt(receipt, lease);
        }

        return (new WeakReference(lease), new WeakReference(activated));
    }

    private PluginLoadContext Context()
    {
        var entry = Path.Combine(_root, "Retained.Entry.dll");
        File.WriteAllBytes(entry, []);

        return new PluginLoadContext(Package, entry, nativeLibraryResolver: null, PackageContractScope.Empty(Package));
    }

    private static bool Collected(WeakReference held)
    {
        for (var attempt = 0; attempt < 12 && held.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return !held.IsAlive;
    }

    /// <summary>Something the host built for the extension, which the lifetime owns.</summary>
    private sealed class Activated;
}
