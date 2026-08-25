using System.IO;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Loading;
using FluentAssertions.Execution;

namespace Arronix.Plugins.Tests.Loading;

/// <summary>
/// What happens when a package's own code fails while it is being torn down.
/// </summary>
/// <remarks>
/// <para>
/// Teardown runs package code twice more: the disposer of every object the package registered, and any
/// handler it attached to its own load context's unloading event. Both are the package-code boundary, so
/// both contain an unfamiliar failure — a novel disposer bug must not stop the remaining packages from
/// being released — and neither may absorb a condition in which the process is no longer sound.
/// </para>
/// <para>
/// The second half is the one worth pinning. A cleanup path that files every exception as a string is the
/// easiest place in a platform to lose an out-of-memory condition, because the failure it produces looks
/// exactly like the ordinary one it is designed to swallow.
/// </para>
/// </remarks>
[TestFixture]
internal sealed class PackageTeardownContainmentTests
{
    private static readonly PluginId Package = PluginId.FromString("teardown");

    private string _root = string.Empty;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("arronix-teardown").FullName;

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task AnUnfamiliarDisposerFailureIsRecordedAndTheContextIsStillReleased()
    {
        var module = new ThrowingModule(new TeardownFixtureException("the disposer objected"));
        var context = Context();
        var lease = new PluginRuntimeLease(context, ledger: null, module);

        var failures = await lease.DisposeAsync();

        using var assertions = new AssertionScope();
        failures.Should().ContainSingle().Which.Should()
            .Contain(nameof(ThrowingModule))
            .And.Contain("the disposer objected");
        module.Disposed.Should().BeTrue();
        lease.LoadContext.Should().BeNull("the context is released whatever the disposer did");
    }

    /// <remarks>
    /// The exception a disposer raises arrives at exactly the same catch as an ordinary one. Only the
    /// containment rule tells them apart, and filing this one as a cleanup note would let an installation
    /// carry on releasing packages inside a process that can no longer allocate.
    /// </remarks>
    [Test]
    public void AProcessFatalDisposerFailurePropagatesRatherThanBecomingACleanupNote()
    {
        var module = new ThrowingModule(new OutOfMemoryException("the process is out of memory"));
        var lease = new PluginRuntimeLease(Context(), ledger: null, module);

        var dispose = async () => await lease.DisposeAsync();

        dispose.Should().ThrowAsync<OutOfMemoryException>().GetAwaiter().GetResult();
    }

    /// <remarks>
    /// Cancellation is the caller's, not a cleanup note. A disposer that observes a canceled token and
    /// throws must not have that answer recorded as its own failure and discarded.
    /// </remarks>
    [Test]
    public void ACanceledDisposerPropagatesRatherThanBecomingACleanupNote()
    {
        var module = new ThrowingModule(new OperationCanceledException("teardown was canceled"));
        var lease = new PluginRuntimeLease(Context(), ledger: null, module);

        var dispose = async () => await lease.DisposeAsync();

        dispose.Should().ThrowAsync<OperationCanceledException>().GetAwaiter().GetResult();
    }

    [TestCaseSource(nameof(UncontainableUnloadFailures))]
    public void AnUncontainableLoadContextHandlerFailurePropagatesRatherThanBecomingACleanupNote(
        Exception failure)
    {
        var context = Context();
        context.Unloading += _ => throw failure;
        var lease = new PluginRuntimeLease(context, ledger: null, module: null);

        var dispose = async () => await lease.DisposeAsync();

        dispose.Should().ThrowAsync<Exception>()
            .Where(thrown => ReferenceEquals(thrown, failure))
            .GetAwaiter()
            .GetResult();
    }

    private PluginLoadContext Context()
    {
        var entry = Path.Combine(_root, "Teardown.Entry.dll");
        File.WriteAllBytes(entry, []);

        return new PluginLoadContext(
            Package,
            entry,
            nativeLibraryResolver: null,
            PackageContractScope.Empty(Package));
    }

    private static IEnumerable<Exception> UncontainableUnloadFailures()
    {
        yield return new OutOfMemoryException("the process is out of memory");
        yield return new OperationCanceledException("teardown was canceled");
    }

    /// <summary>A failure type no policy in the platform names.</summary>
    private sealed class TeardownFixtureException(string message) : Exception(message);

    /// <summary>A module whose disposer raises whatever the fixture chose.</summary>
    private sealed class ThrowingModule(Exception failure) : IPluginModule, IDisposable
    {
        public PluginId Id => Package;

        public bool Disposed { get; private set; }

        public void Configure(IPluginContext context)
        {
        }

        public void Dispose()
        {
            Disposed = true;
            throw failure;
        }
    }
}
