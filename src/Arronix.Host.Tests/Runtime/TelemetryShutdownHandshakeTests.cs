using System.IO;
using System.Linq;
using Arronix.Common.Telemetry;
using Arronix.Host.Composition;
using FluentAssertions;
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

    private ServiceProvider Build(Action<IServiceCollection>? extra = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arronix:Host:ExtensionFolder"] = Path.Combine(_root, "extensions"),
                ["Arronix:Plugins:RootFolder"] = Path.Combine(_root, "extensions"),
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
