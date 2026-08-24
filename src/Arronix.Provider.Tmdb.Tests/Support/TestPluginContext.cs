using System;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Serialization;
using Arronix.Abstractions.Telemetry;
using Arronix.Abstractions.Throttling;

namespace Arronix.Provider.Tmdb.Tests.Support;

/// <summary>
/// The smallest plugin context the TMDb provider's call-time behavior needs: the clock and the HTTP
/// gateway. Every other member throws, so a test that reaches for one is asserting a real dependency
/// this provider does not have.
/// </summary>
internal sealed class TestPluginContext(IHttpGateway gateway, TimeProvider clock, IPluginRegistry? registry = null)
    : IPluginContext
{
    public PluginId PluginId { get; } = PluginId.FromString("tmdb");

    public string PluginVersion => "0.1.0";

    public string HostContractVersion => "0.8.0";

    public CapabilitySet Capabilities { get; } =
        CapabilitySet.Of(Capability.Metadata, Capability.Curation, Capability.Network);

    public IPluginRegistry Registry => registry ?? throw Unused(nameof(Registry));

    public IPluginPaths Paths => throw Unused(nameof(Paths));

    public ICacheProvider Cache => throw Unused(nameof(Cache));

    public IJsonSerializer Json => throw Unused(nameof(Json));

    public ITelemetryEmitter Telemetry => throw Unused(nameof(Telemetry));

    public IEventPublisher Events => throw Unused(nameof(Events));

    public IHostRuntimeInfo Runtime => throw Unused(nameof(Runtime));

    public IOperatingSystemInfo OperatingSystem => throw Unused(nameof(OperatingSystem));

    public TimeProvider Clock { get; } = clock;

    public bool TryGetHttp(out IHttpGateway? value)
    {
        value = gateway;
        return true;
    }

    public IHttpGateway RequireHttp() => gateway;

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

    public IFileSystem RequireFileSystem() => throw Unused(nameof(RequireFileSystem));

    public IFileTransferService RequireFileTransfer() => throw Unused(nameof(RequireFileTransfer));

    private static NotSupportedException Unused(string member) => new(
        $"This harness supplies only HTTP and the clock; the TMDb provider does not use '{member}'.");
}
