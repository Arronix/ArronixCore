using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Telemetry;

namespace Arronix.Host.Tests.Support;

/// <summary>
/// The platform services a host must register before it can activate anything, stubbed.
/// </summary>
/// <remarks>
/// Every gated member throws. A packaged extension that reaches one has exercised a privilege the test did
/// not grant it, and a silent no-op there would make that indistinguishable from not reaching it at all.
/// </remarks>
internal sealed class PlatformServiceStub :
    ICacheProvider,
    ITelemetryEmitter,
    IEventPublisher,
    IHostRuntimeInfo,
    IOperatingSystemInfo
{
    public DateTimeOffset StartTime { get; } = DateTimeOffset.UnixEpoch;

    public bool IsUserInteractive => false;

    public bool IsAdministrator => false;

    public bool IsWindowsService => false;

    public bool IsContainerized => false;

    public string? ExecutingApplication => null;

    public string Name => "Test";

    public string Version => "1";

    public string FullName => "Test operating system";

    public bool IsDocker => false;

    public bool IsPodman => false;

    public ICache<TValue> GetCache<TOwner, TValue>(string partition) => throw Unused();

    public ICache<TValue> GetRollingCache<TOwner, TValue>(string partition, TimeSpan defaultLifetime)
        => throw Unused();

    public ISelfRefreshingCache<TValue> GetSelfRefreshingCache<TOwner, TValue>(
        string partition,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, TValue>>> fetch,
        TimeSpan? lifetime = null) => throw Unused();

    public void Emit(TelemetryEvent telemetryEvent)
    {
    }

    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent => Task.CompletedTask;

    private static InvalidOperationException Unused() =>
        new("A packaged extension must not exercise undeclared platform privileges.");
}
