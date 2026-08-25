using Arronix.Abstractions.Caching;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Serialization;
using Arronix.Abstractions.Telemetry;
using Arronix.Abstractions.Throttling;


namespace Arronix.Host.Tests.DefinitionBinding;

/// <summary>
/// The context a media extension is configured with, reduced to the one member a declaration-only extension
/// touches.
/// </summary>
/// <remarks>
/// Every other member throws rather than returning an inert double. A media kind that reached for the cache,
/// the clock or the event publisher during registration would be doing something these tests are asserting
/// it does not do, and a stub that quietly answered would hide exactly that.
/// </remarks>
internal sealed class StubPluginContext(PluginId plugin, IPluginRegistry registry) : IPluginContext
{
    public PluginId PluginId { get; } = plugin;

    public CapabilitySet Capabilities => CapabilitySet.None;

    public IPluginRegistry Registry { get; } = registry;

    public IPluginPaths Paths => throw Unused(nameof(Paths));

    public ICacheProvider Cache => throw Unused(nameof(Cache));

    public IJsonSerializer Json => throw Unused(nameof(Json));

    public ITelemetryEmitter Telemetry => throw Unused(nameof(Telemetry));

    public IEventPublisher Events => throw Unused(nameof(Events));

    public IHostRuntimeInfo Runtime => throw Unused(nameof(Runtime));

    public IOperatingSystemInfo OperatingSystem => throw Unused(nameof(OperatingSystem));

    public TimeProvider Clock => throw Unused(nameof(Clock));

    public string PluginVersion => "0.1.0";

    public string HostContractVersion => "0.9.0";

    public IHttpGateway RequireHttp() => throw Unused(nameof(RequireHttp));

    public IFileSystem RequireFileSystem() => throw Unused(nameof(RequireFileSystem));

    public IFileTransferService RequireFileTransfer() => throw Unused(nameof(RequireFileTransfer));

    public bool TryGetHttp(out IHttpGateway? gateway)
    {
        gateway = null;
        return false;
    }

    public bool TryGetRateLimiter(out IRateLimiter? limiter)
    {
        limiter = null;
        return false;
    }

    public bool TryGetCertificatePolicy(out ICertificateValidationPolicy? policy)
    {
        policy = null;
        return false;
    }

    public bool TryGetFileSystem(out IFileSystem? fileSystem)
    {
        fileSystem = null;
        return false;
    }

    public bool TryGetFileTransfer(out IFileTransferService? transfer)
    {
        transfer = null;
        return false;
    }

    private static NotSupportedException Unused(string member) => new(
        $"A typed media kind registers by naming two types; it has no use for '{member}' while configuring. "
        + "Reaching this means the extension started doing work at registration time.");
}
