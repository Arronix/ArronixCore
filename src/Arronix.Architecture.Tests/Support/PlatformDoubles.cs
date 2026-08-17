using System.IO;
using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Serialization;
using Arronix.Abstractions.Telemetry;
using Arronix.Abstractions.Throttling;

#pragma warning disable ARX0001 // Caching contracts are experimental; this fixture stands in for a host.
#pragma warning disable ARX0004 // Event contracts are experimental; this fixture stands in for a host.
#pragma warning disable ARX0005 // File-system contracts are experimental; this fixture stands in for a host.
#pragma warning disable ARX0007 // Hosting contracts are experimental; this fixture stands in for a host.
#pragma warning disable ARX0008 // Outbound-call contracts are experimental; this fixture stands in for a host.
#pragma warning disable ARX0010 // Serialization contracts are experimental; this fixture stands in for a host.
#pragma warning disable ARX0011 // Telemetry contracts are experimental; this fixture stands in for a host.
#pragma warning disable ARX0012 // Throttling contracts are experimental; this fixture stands in for a host.
#pragma warning disable ARX0014 // The extension model is experimental; this fixture drives it.

namespace Arronix.Architecture.Tests.Support;

/// <summary>
/// The smallest host an extension's configure method can be run against.
/// </summary>
/// <remarks>
/// <para>
/// Only three members are answered for real: the registry, because it is the admission gate under test;
/// telemetry, because an extension is entitled to say what it configured; and the clock, because a
/// declaration built at configure time may read it. Everything else is present because the contract
/// requires it and refuses on use.
/// </para>
/// <para>
/// Nothing gated is granted at all. That is the point rather than an omission: an extension that reached
/// for the network or the file system while merely declaring itself is doing something the capability
/// model exists to make visible, and a loud refusal is the correct outcome.
/// </para>
/// </remarks>
internal sealed class ConfigureOnlyPluginContext : IPluginContext
{
    private const string Unavailable =
        "This fixture runs an extension's configure method and grants nothing gated. Reaching for a "
        + "platform service while declaring is exactly what the capability model exists to surface.";

    public ConfigureOnlyPluginContext(
        PluginId pluginId,
        string pluginVersion,
        CapabilitySet capabilities,
        IPluginRegistry registry,
        RecordingTelemetryEmitter telemetry)
    {
        PluginId = pluginId;
        PluginVersion = pluginVersion;
        Capabilities = capabilities;
        Registry = registry;
        Telemetry = telemetry;
    }

    public PluginId PluginId { get; }

    public string PluginVersion { get; }

    public string HostContractVersion { get; } = "0.3.0";

    public CapabilitySet Capabilities { get; }

    public IPluginRegistry Registry { get; }

    public IPluginPaths Paths { get; } = new StubPluginPaths();

    public ICacheProvider Cache { get; } = new StubCacheProvider();

    public IJsonSerializer Json { get; } = new StubJsonSerializer();

    public ITelemetryEmitter Telemetry { get; }

    public IEventPublisher Events { get; } = new StubEventPublisher();

    public IHostRuntimeInfo Runtime { get; } = new StubHostRuntimeInfo();

    public IOperatingSystemInfo OperatingSystem { get; } = new StubOperatingSystemInfo();

    public TimeProvider Clock => TimeProvider.System;

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

    public IHttpGateway RequireHttp() => throw new NotSupportedException(Unavailable);

    public IFileSystem RequireFileSystem() => throw new NotSupportedException(Unavailable);

    public IFileTransferService RequireFileTransfer() => throw new NotSupportedException(Unavailable);
}

/// <summary>
/// Keeps what an extension said while it configured, so the fixture can prove it was reachable.
/// </summary>
internal sealed class RecordingTelemetryEmitter : ITelemetryEmitter
{
    private readonly List<TelemetryEvent> _events = [];

    public IReadOnlyList<TelemetryEvent> Events => _events;

    public void Emit(TelemetryEvent telemetryEvent) => _events.Add(telemetryEvent);
}

internal sealed class StubPluginPaths : IPluginPaths
{
    public string PluginId => "architecture-tests";

    public string DataFolder => Path.Combine(Path.GetTempPath(), "arronix-architecture-tests", "data");

    public string CacheFolder => Path.Combine(Path.GetTempPath(), "arronix-architecture-tests", "cache");

    public string TempFolder => Path.Combine(Path.GetTempPath(), "arronix-architecture-tests", "temp");
}

internal sealed class StubCacheProvider : ICacheProvider
{
    private const string Unavailable = "This fixture supplies no cache; nothing an extension does while configuring needs one.";

    public ICache<TValue> GetCache<TOwner, TValue>(string partition) => throw new NotSupportedException(Unavailable);

    public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
        => throw new NotSupportedException(Unavailable);

    public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
        string partition,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeSpan? lifetime = null)
        => throw new NotSupportedException(Unavailable);
}

internal sealed class StubJsonSerializer : IJsonSerializer
{
    private const string Unavailable = "This fixture supplies no serializer; configuring an extension does not serialize.";

    public string Serialize<TValue>(TValue value) => throw new NotSupportedException(Unavailable);

    public TValue? Deserialize<TValue>(string json) => throw new NotSupportedException(Unavailable);

    public bool TryDeserialize<TValue>(string json, out TValue? value) => throw new NotSupportedException(Unavailable);

    public Task SerializeAsync<TValue>(TValue value, Stream destination, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(Unavailable);

    public ValueTask<TValue?> DeserializeAsync<TValue>(Stream source, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(Unavailable);
}

internal sealed class StubEventPublisher : IEventPublisher
{
    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
        => Task.CompletedTask;
}

internal sealed class StubHostRuntimeInfo : IHostRuntimeInfo
{
    public DateTimeOffset StartTime => DateTimeOffset.UnixEpoch;

    public bool IsUserInteractive => false;

    public bool IsAdministrator => false;

    public bool IsWindowsService => false;

    public bool IsContainerized => false;

    public string? ExecutingApplication => null;
}

internal sealed class StubOperatingSystemInfo : IOperatingSystemInfo
{
    public string Name => "architecture-tests";

    public string Version => "0.0.0";

    public string FullName => "architecture-tests 0.0.0";

    public bool IsDocker => false;

    public bool IsPodman => false;

    public bool IsContainerized => false;
}
