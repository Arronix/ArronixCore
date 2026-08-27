using System.IO;
using System.Linq;
using Arronix.Common.Telemetry;
using Arronix.Host.Composition;
using Arronix.Host.Runtime;
using Arronix.Host.Telemetry;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arronix.Host.Tests.Runtime;

/// <summary>
/// That the telemetry pipeline and the extension runtime can be stopped together, however the host does it.
/// </summary>
/// <remarks>
/// The two have an order — extensions first, so their cleanup telemetry is still delivered — and it is a
/// handshake rather than a registration order, because the generic host is free to stop hosted services
/// concurrently. Both shapes are exercised here, and both are bounded: a shutdown that deadlocks is a
/// process that never exits, which is the failure this pair is most able to produce.
/// </remarks>
[TestFixture]
internal sealed class TelemetryShutdownHandshakeTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "arronix-telemetry-stop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public async Task TheOrdinarySequentialShutdownCompletesAsync()
    {
        using var provider = Build();
        var services = provider.GetServices<IHostedService>().ToArray();

        foreach (var service in services)
        {
            await service.StartAsync(CancellationToken.None).WaitAsync(Patience).ConfigureAwait(false);
        }

        // Exactly what the generic host does: reverse registration order, one at a time.
        var stopping = Task.Run(async () =>
        {
            foreach (var service in services.Reverse())
            {
                await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        });

        var finished = await Task.WhenAny(stopping, Task.Delay(Patience)).ConfigureAwait(false);

        finished.Should().BeSameAs(
            stopping,
            "a pump that waits for the extensions while the host waits for the pump is a process that never exits");

        await stopping.ConfigureAwait(false);
    }

    [Test]
    public async Task AConcurrentShutdownCompletesTooAsync()
    {
        using var provider = Build();
        var services = provider.GetServices<IHostedService>().ToArray();

        foreach (var service in services)
        {
            await service.StartAsync(CancellationToken.None).WaitAsync(Patience).ConfigureAwait(false);
        }

        var stopping = Task.WhenAll(services.Select(service => service.StopAsync(CancellationToken.None)));
        var finished = await Task.WhenAny(stopping, Task.Delay(Patience)).ConfigureAwait(false);

        finished.Should().BeSameAs(stopping, "the order between them is a handshake, not a registration order");
        await stopping.ConfigureAwait(false);
    }

    [Test]
    public async Task CleanupTelemetryRaisedWhileExtensionsAreTornDownStillReachesTheHostsSinksAsync()
    {
        var sink = new CountingSink();
        using var provider = Build(services => services.AddSingleton<Arronix.Abstractions.Telemetry.ITelemetrySink>(sink));

        var services = provider.GetServices<IHostedService>().ToArray();

        foreach (var service in services)
        {
            await service.StartAsync(CancellationToken.None).WaitAsync(Patience).ConfigureAwait(false);
        }

        var emitter = provider.GetRequiredService<HostTelemetryEmitter>();
        emitter.Emit(new Arronix.Abstractions.Telemetry.TelemetryEvent(
            Guid.CreateVersion7(),
            DateTimeOffset.UnixEpoch,
            Arronix.Abstractions.Telemetry.TelemetrySeverity.Info,
            "raised while stopping"));

        foreach (var service in services.Reverse())
        {
            await service.StopAsync(CancellationToken.None).WaitAsync(Patience).ConfigureAwait(false);
        }

        sink.Received.Should().Be(1, "the pump stops after the extensions, which is what makes that event deliverable");
        sink.Flushes.Should().Be(1);
    }

    [Test]
    public async Task ABacklogTheDrainCouldNotFinishIsAbandonedRatherThanWaitedForAsync()
    {
        var slow = new SlowSink(TimeSpan.FromMilliseconds(50));

        using var provider = Build(services =>
        {
            services.AddSingleton<Arronix.Abstractions.Telemetry.ITelemetrySink>(slow);
            services.Configure<Arronix.Common.Telemetry.TelemetryOptions>(options =>
            {
                options.ShutdownDrain = TimeSpan.FromMilliseconds(200);
                options.SinkAttempt = TimeSpan.FromSeconds(5);
            });
        });

        var services = provider.GetServices<IHostedService>().ToArray();

        foreach (var service in services)
        {
            await service.StartAsync(CancellationToken.None).WaitAsync(Patience).ConfigureAwait(false);
        }

        var emitter = provider.GetRequiredService<HostTelemetryEmitter>();

        for (var index = 0; index < 200; index++)
        {
            emitter.Emit(new Arronix.Abstractions.Telemetry.TelemetryEvent(
                Guid.CreateVersion7(),
                DateTimeOffset.UnixEpoch,
                Arronix.Abstractions.Telemetry.TelemetrySeverity.Info,
                $"backlog {index}"));
        }

        var started = System.Diagnostics.Stopwatch.StartNew();

        foreach (var service in services.Reverse())
        {
            await service.StopAsync(CancellationToken.None).WaitAsync(Patience).ConfigureAwait(false);
        }

        started.Stop();

        using var assertions = new AssertionScope();
        slow.Received.Should().BeLessThan(
            200,
            "a backlog of ten seconds' work cannot be delivered inside a drain budget of a fifth of a second");
        started.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(5),
            "the budget is the budget: what it could not drain is abandoned rather than waited for");

        // Every accepted event is accounted for, and the queue is empty: what the budget cut short was the
        // waiting on the sink, not the reading of the queue.
        (emitter.Delivered + emitter.Dropped + emitter.Duplicates + emitter.Refused).Should().Be(200);
    }

    [Test]
    public async Task ASecondStopReturnsWhenTheFirstOneDoesRatherThanBeforeItAsync()
    {
        using var provider = Build();
        var services = provider.GetServices<IHostedService>().ToArray();

        foreach (var service in services)
        {
            await service.StartAsync(CancellationToken.None).WaitAsync(Patience).ConfigureAwait(false);
        }

        var pump = services.OfType<TelemetryPumpService>().Single();
        var extensions = services.OfType<PluginBootstrapper>().Single();

        // Both callers are held at the same place, because the extensions have not said they are gone.
        var first = pump.StopAsync(CancellationToken.None);
        var second = pump.StopAsync(CancellationToken.None);

        await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(250))).ConfigureAwait(false);

        using (new AssertionScope())
        {
            second.IsCompleted.Should().BeFalse(
                "a caller who finds a stop already running is told when stopping finished, not that it started");
            first.IsCompleted.Should().BeFalse("the extensions have not been torn down yet");
            second.Should().BeSameAs(first, "there is one stop, and every caller awaits that one");
        }

        await extensions.StopAsync(CancellationToken.None).WaitAsync(Patience).ConfigureAwait(false);

        var both = Task.WhenAll(first, second);
        var finished = await Task.WhenAny(both, Task.Delay(Patience)).ConfigureAwait(false);

        finished.Should().BeSameAs(both, "the shared stop completes both callers");
        await both.ConfigureAwait(false);
    }

    private ServiceProvider Build(Action<IServiceCollection>? extra = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arronix:Host:ExtensionFolder"] = Path.Combine(_root, "extensions"),
                ["Arronix:Plugins:RootFolder"] = Path.Combine(_root, "extensions"),
                ["Arronix:Store:DataSource"] = Path.Combine(_root, "arronix.db"),
                ["Arronix:Library:RootFolders:0"] = _root,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddArronixHost(configuration);
        extra?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    /// <summary>A sink slow enough that a backlog cannot be delivered inside a small drain budget.</summary>
    private sealed class SlowSink(TimeSpan perEvent) : Arronix.Abstractions.Telemetry.ITelemetrySink
    {
        private int _received;

        public string SinkId => "slow";

        internal int Received => Volatile.Read(ref _received);

        public async Task SendAsync(
            Arronix.Abstractions.Telemetry.TelemetryEvent telemetryEvent,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(perEvent, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _received);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>A host-registered sink, which is what an extension's cleanup telemetry has left to reach.</summary>
    private sealed class CountingSink : Arronix.Abstractions.Telemetry.ITelemetrySink
    {
        public string SinkId => "counting";

        internal int Received { get; private set; }

        internal int Flushes { get; private set; }

        public Task SendAsync(
            Arronix.Abstractions.Telemetry.TelemetryEvent telemetryEvent,
            CancellationToken cancellationToken = default)
        {
            Received++;
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            Flushes++;
            return Task.CompletedTask;
        }
    }
}
