using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Telemetry;
using Arronix.Plugins.Registry;
using Arronix.Plugins.Scoping;
using Arronix.Plugins.Tests.Support;
using FluentAssertions.Execution;

namespace Arronix.Plugins.Tests.Scoping;

/// <summary>
/// What an extension may raise while it is being torn down, and what that must not cost.
/// </summary>
/// <remarks>
/// Telemetry raised during disposal is the telemetry an operator most wants, and it arrives after the
/// extension's invocation lifetime has closed. It is still emitted and still attributed; the one thing it
/// cannot carry across the close is a live exception, whose type may be the extension's own — and holding
/// one would keep that extension's load context loaded for the life of the process.
/// </remarks>
[TestFixture]
public sealed class CleanupTelemetryTests
{
    private static readonly PluginId Plugin = PluginId.FromString("cleanup.fixture");

    [Test]
    public void CleanupTelemetryIsStillEmittedAndStillAttributed()
    {
        var inner = new RecordingTelemetryEmitter();
        var emitter = Closed(inner);

        emitter.Emit(Event("disposing") with { Exception = new InvalidOperationException("on the way out") });

        var received = inner.Events.Should().ContainSingle().Which;

        using var assertions = new AssertionScope();
        received.Tags[PluginTelemetryEmitter.PluginTag].Should().Be(Plugin.ToString());
        received.Exception.Should().BeNull("a live exception's type may be the extension's own");
        received.ExceptionSummary!.Message.Should().Be("on the way out");
    }

    [Test]
    public void AFailureThatWillNotDescribeItselfIsRenderedWithoutHoldingItsExtensionOpen()
    {
        var inner = new RecordingTelemetryEmitter();

        // The failure is a real extension's: its type comes from a collectible context, and its own members
        // throw. Reading it is calling into the extension, so an escape here would turn cleanup telemetry
        // into a failed release; keeping it would retain the package instead.
        var attempt = OnItsOwnThread(() => EmitUnreadable(inner));

        // Nothing here renders the failure or the event holding it: formatting either one would call the
        // very getter that objects, and the assertion would be lost inside it.
        using var assertions = new AssertionScope();
        (attempt.Escaped is null).Should().BeTrue("an emit that throws on teardown is a release that failed");
        inner.Events.Count.Should().Be(1);

        var received = inner.Events[0];
        received.ExceptionSummary!.TypeName.Should().Be(typeof(UnreadableProbe).FullName);
        received.ExceptionSummary.Message.Should().Contain("would not describe itself");
        (received.Exception is null).Should().BeTrue("a live exception's type may be the extension's own");

        Collected(attempt.Context).Should().BeTrue(
            "what was recorded is host-owned text, so the context that defines the failure still unloads");

        GC.KeepAlive(inner);
    }

    /// <summary>
    /// Emits a failure whose type is an extension's own, unloads that extension, and hands back only a weak
    /// reference to its context.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Attempt EmitUnreadable(ITelemetryEmitter inner)
    {
        var context = new AssemblyLoadContext("extension-" + Guid.CreateVersion7().ToString("N"), isCollectible: true);
        using var image = new MemoryStream(File.ReadAllBytes(typeof(CleanupTelemetryTests).Assembly.Location));

        var probe = context.LoadFromStream(image).GetType(typeof(UnreadableProbe).FullName!)!;

        if (!probe.Assembly.IsCollectible || probe == typeof(UnreadableProbe))
        {
            throw new InvalidOperationException("The probe type did not come from a collectible context.");
        }

        Exception? escaped = null;

        try
        {
            Closed(inner).Emit(Event("disposing") with { Exception = (Exception)Activator.CreateInstance(probe)! });
        }
#pragma warning disable CA1031
        catch (Exception failure)
#pragma warning restore CA1031
        {
            // Carried back rather than thrown: an escape on this thread would take the process with it, and
            // the assertion belongs on the test's own thread.
            escaped = failure;
        }

        context.Unload();
        return new Attempt(new WeakReference(context), escaped);
    }

    private static PluginTelemetryEmitter Closed(ITelemetryEmitter inner)
    {
        var invocation = new PluginInvocationLifetime(Plugin);
        invocation.Close();

        return new PluginTelemetryEmitter(inner, Plugin, invocation);
    }

    private static TelemetryEvent Event(string message)
        => new(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, TelemetrySeverity.Info, message);

    /// <summary>Runs the fixture on a thread that then exits, so no live stack can hold what it touched.</summary>
    private static T OnItsOwnThread<T>(Func<T> fixture)
    {
        T produced = default!;
        var worker = new Thread(() => produced = fixture());
        worker.Start();
        worker.Join();
        return produced;
    }

    /// <summary>Whether the collector could take it, which is the only real proof of release.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Collected(WeakReference context)
    {
        for (var attempt = 0; attempt < 20 && context.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return !context.IsAlive;
    }

    private sealed record Attempt(WeakReference Context, Exception? Escaped);
}
