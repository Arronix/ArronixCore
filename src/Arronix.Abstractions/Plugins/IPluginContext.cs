using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Serialization;
using Arronix.Abstractions.Telemetry;
using Arronix.Abstractions.Throttling;

namespace Arronix.Abstractions.Plugins;

/// <summary>
/// The whole of what an extension can reach.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a locator, which is normally an anti-pattern. Here it is the enforcement point: "what can
/// an extension reach?" is answerable by reading one interface, and the paired try-and-require form makes
/// every gated dependency visible at the call site rather than hidden in a constructor signature.
/// </para>
/// <para>
/// The alternative — handing an extension the host's service container and filtering afterwards — is
/// unavailable by construction, because this assembly takes no package references and so cannot name a
/// container type at all. That constraint turned out to be a benefit: admission control is strictly
/// stronger than post-hoc filtering, and it is checkable before any extension code runs.
/// </para>
/// <para>
/// Every gated instance handed out is a scoping decorator built by the host: outbound calls are attributed
/// and rate-limited per extension, file access is confined to the extension's granted roots, cache
/// partitions and event subscriptions are namespaced. The contracts already promised that behavior; this
/// is where it is enforced.
/// </para>
/// </remarks>
public interface IPluginContext
{
    /// <summary>
    /// Gets the extension's own identifier.
    /// </summary>
    PluginId PluginId { get; }

    /// <summary>
    /// Gets the extension's own version, verbatim from its manifest.
    /// </summary>
    string PluginVersion { get; }

    /// <summary>
    /// Gets the version of the contract assembly the host is running, verbatim.
    /// </summary>
    string HostContractVersion { get; }

    /// <summary>
    /// Gets the capabilities granted to the extension, after implication.
    /// </summary>
    CapabilitySet Capabilities { get; }

    /// <summary>
    /// Gets the only surface through which the extension contributes anything.
    /// </summary>
    IPluginRegistry Registry { get; }

    /// <summary>
    /// Gets the extension's own folders, created before activation. The platform's own data folder is
    /// never exposed.
    /// </summary>
    IPluginPaths Paths { get; }

    /// <summary>
    /// Gets the cache provider, whose partitions are namespaced by the extension's identifier.
    /// </summary>
    ICacheProvider Cache { get; }

    /// <summary>
    /// Gets the serializer, so that an extension needs no serialization package of its own.
    /// </summary>
    IJsonSerializer Json { get; }

    /// <summary>
    /// Gets the telemetry emitter, which is also the extension's diagnostic surface. There is
    /// deliberately no separate logging contract: one pipeline, one correlation identifier, and no
    /// extension takes a hard dependency on a logging framework.
    /// </summary>
    ITelemetryEmitter Telemetry { get; }

    /// <summary>
    /// Gets the event publisher, whose publications are namespaced by the extension's identifier and
    /// whose subscriptions are filtered to the platform's own events and the extension's.
    /// </summary>
    IEventPublisher Events { get; }

    /// <summary>
    /// Gets facts about the running host.
    /// </summary>
    IHostRuntimeInfo Runtime { get; }

    /// <summary>
    /// Gets facts about the operating system.
    /// </summary>
    IOperatingSystemInfo OperatingSystem { get; }

    /// <summary>
    /// Gets the clock. Injected rather than read from the ambient system clock so that an extension's
    /// time-dependent behavior is testable and so that the platform has one source of time.
    /// </summary>
    TimeProvider Clock { get; }

    /// <summary>
    /// Attempts to obtain the outbound call gateway.
    /// </summary>
    /// <param name="gateway">The gateway when the network capability was granted; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the capability was granted; otherwise <see langword="false"/>.</returns>
    bool TryGetHttp(out IHttpGateway? gateway);

    /// <summary>
    /// Attempts to obtain the rate limiter.
    /// </summary>
    /// <param name="limiter">The limiter when the network capability was granted; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the capability was granted; otherwise <see langword="false"/>.</returns>
    bool TryGetRateLimiter(out IRateLimiter? limiter);

    /// <summary>
    /// Attempts to obtain the certificate validation policy.
    /// </summary>
    /// <param name="policy">The policy when the network capability was granted; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the capability was granted; otherwise <see langword="false"/>.</returns>
    bool TryGetCertificatePolicy(out ICertificateValidationPolicy? policy);

    /// <summary>
    /// Attempts to obtain the file system, confined to the extension's granted roots.
    /// </summary>
    /// <param name="fileSystem">The file system when the storage capability was granted; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the capability was granted; otherwise <see langword="false"/>.</returns>
    bool TryGetFileSystem(out IFileSystem? fileSystem);

    /// <summary>
    /// Attempts to obtain the file transfer service.
    /// </summary>
    /// <param name="transfer">The service when the import capability was granted; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the capability was granted; otherwise <see langword="false"/>.</returns>
    bool TryGetFileTransfer(out IFileTransferService? transfer);

    /// <summary>
    /// Obtains the outbound call gateway.
    /// </summary>
    /// <returns>The gateway.</returns>
    /// <exception cref="ArronixException">
    /// The network capability was not granted. The failure carries
    /// <see cref="CoreErrorCode.PluginCapabilityMissing"/> and names the capability and the contract.
    /// </exception>
    IHttpGateway RequireHttp();

    /// <summary>
    /// Obtains the file system, confined to the extension's granted roots.
    /// </summary>
    /// <returns>The file system.</returns>
    /// <exception cref="ArronixException">
    /// The storage capability was not granted. The failure carries
    /// <see cref="CoreErrorCode.PluginCapabilityMissing"/> and names the capability and the contract.
    /// </exception>
    IFileSystem RequireFileSystem();

    /// <summary>
    /// Obtains the file transfer service.
    /// </summary>
    /// <returns>The service.</returns>
    /// <exception cref="ArronixException">
    /// The import capability was not granted. The failure carries
    /// <see cref="CoreErrorCode.PluginCapabilityMissing"/> and names the capability and the contract.
    /// </exception>
    IFileTransferService RequireFileTransfer();
}
