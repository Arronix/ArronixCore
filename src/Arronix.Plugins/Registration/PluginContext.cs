using Arronix.Abstractions.Caching;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Hosting;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Serialization;
using Arronix.Abstractions.Telemetry;
using Arronix.Abstractions.Throttling;


namespace Arronix.Plugins.Registration;

/// <summary>
/// The platform services an extension context is built from.
/// </summary>
/// <remarks>
/// <para>
/// Bundled into one type rather than injected member by member so that the loader's constructor stays a
/// signature a reviewer can read, and so that "what does the platform have to provide before an extension
/// can run" is answerable by looking at one place.
/// </para>
/// <para>
/// The gated services are optional at construction. A host that has not built its outbound-call stack yet
/// can still load an extension that never asks for one, and an extension that does ask gets a clear failure
/// naming the missing platform service rather than a null reference from inside its own code. The ungated
/// services are not optional, because there is no useful extension that can run without them.
/// </para>
/// </remarks>
public sealed class PluginPlatformServices
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginPlatformServices"/> class.
    /// </summary>
    /// <param name="json">The serializer, so an extension needs no serialization package of its own.</param>
    /// <param name="clock">The clock every component reads.</param>
    /// <param name="cache">The shared cache provider.</param>
    /// <param name="telemetry">The platform's telemetry emitter.</param>
    /// <param name="events">The platform's event publisher.</param>
    /// <param name="runtime">Facts about the running host.</param>
    /// <param name="operatingSystem">Facts about the operating system.</param>
    /// <param name="fileSystem">The unconfined file system, when the host has one.</param>
    /// <param name="http">The unattributed outbound-call gateway, when the host has one.</param>
    /// <param name="rateLimiter">The shared limiter, when the host has one.</param>
    /// <param name="certificatePolicy">The certificate policy, when the host has one.</param>
    /// <param name="fileTransfer">The file transfer service, when the host has one.</param>
    public PluginPlatformServices(
        IJsonSerializer json,
        TimeProvider clock,
        ICacheProvider? cache = null,
        ITelemetryEmitter? telemetry = null,
        IEventPublisher? events = null,
        IHostRuntimeInfo? runtime = null,
        IOperatingSystemInfo? operatingSystem = null,
        IFileSystem? fileSystem = null,
        IHttpGateway? http = null,
        IRateLimiter? rateLimiter = null,
        ICertificateValidationPolicy? certificatePolicy = null,
        IFileTransferService? fileTransfer = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(clock);

        Json = json;
        Clock = clock;
        Cache = cache;
        Telemetry = telemetry;
        Events = events;
        Runtime = runtime;
        OperatingSystem = operatingSystem;
        FileSystem = fileSystem;
        Http = http;
        RateLimiter = rateLimiter;
        CertificatePolicy = certificatePolicy;
        FileTransfer = fileTransfer;
    }

    /// <summary>Gets the serializer.</summary>
    public IJsonSerializer Json { get; }

    /// <summary>Gets the clock.</summary>
    public TimeProvider Clock { get; }

    /// <summary>Gets the shared cache provider, when the host has one.</summary>
    public ICacheProvider? Cache { get; }

    /// <summary>Gets the platform's telemetry emitter, when the host has one.</summary>
    public ITelemetryEmitter? Telemetry { get; }

    /// <summary>Gets the platform's event publisher, when the host has one.</summary>
    public IEventPublisher? Events { get; }

    /// <summary>Gets facts about the running host, when the host supplies them.</summary>
    public IHostRuntimeInfo? Runtime { get; }

    /// <summary>Gets facts about the operating system, when the host supplies them.</summary>
    public IOperatingSystemInfo? OperatingSystem { get; }

    /// <summary>Gets the unconfined file system, when the host has one.</summary>
    public IFileSystem? FileSystem { get; }

    /// <summary>Gets the unattributed outbound-call gateway, when the host has one.</summary>
    public IHttpGateway? Http { get; }

    /// <summary>Gets the shared limiter, when the host has one.</summary>
    public IRateLimiter? RateLimiter { get; }

    /// <summary>Gets the certificate policy, when the host has one.</summary>
    public ICertificateValidationPolicy? CertificatePolicy { get; }

    /// <summary>Gets the file transfer service, when the host has one.</summary>
    public IFileTransferService? FileTransfer { get; }

    /// <summary>
    /// Names the ungated services the host has not registered.
    /// </summary>
    /// <returns>The missing service names, or an empty list when everything an extension needs is present.</returns>
    /// <remarks>
    /// Checked before an extension is activated rather than when it first reaches for something, so that a
    /// host missing a subsystem fails as a host misconfiguration rather than as an extension defect.
    /// </remarks>
    public IReadOnlyList<string> MissingRequiredServices()
    {
        var missing = new List<string>();

        if (Cache is null)
        {
            missing.Add(nameof(ICacheProvider));
        }

        if (Telemetry is null)
        {
            missing.Add(nameof(ITelemetryEmitter));
        }

        if (Events is null)
        {
            missing.Add(nameof(IEventPublisher));
        }

        if (Runtime is null)
        {
            missing.Add(nameof(IHostRuntimeInfo));
        }

        if (OperatingSystem is null)
        {
            missing.Add(nameof(IOperatingSystemInfo));
        }

        return missing;
    }
}

/// <summary>
/// The whole of what one extension can reach.
/// </summary>
/// <remarks>
/// <para>
/// A locator, which is normally an anti-pattern and here is the enforcement point. "What can this extension
/// reach?" is answerable by reading one type, and the paired try-and-require form puts every gated
/// dependency at its call site rather than hiding it in a constructor signature the host would have to
/// audit.
/// </para>
/// <para>
/// Nothing gated is handed over undecorated. The outbound gateway is attributed and throttled, the file
/// system is confined to the extension's granted roots, cache partitions and event publication are
/// namespaced. The contracts already promised all of that; assembling the decorators here is what makes the
/// promises true, and doing it in one place is what makes them checkable.
/// </para>
/// </remarks>
public sealed class PluginContext : IPluginContext
{
    private readonly IHttpGateway? _http;
    private readonly IRateLimiter? _rateLimiter;
    private readonly ICertificateValidationPolicy? _certificatePolicy;
    private readonly IFileSystem? _fileSystem;
    private readonly IFileTransferService? _fileTransfer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginContext"/> class.
    /// </summary>
    /// <param name="pluginId">The extension's identifier.</param>
    /// <param name="pluginVersion">Its version, verbatim from its declaration.</param>
    /// <param name="hostContractVersion">The host's contract-assembly version, verbatim.</param>
    /// <param name="capabilities">The capabilities granted, after implication.</param>
    /// <param name="registry">The only surface through which it contributes anything.</param>
    /// <param name="paths">Its own folders.</param>
    /// <param name="cache">Its namespaced cache provider.</param>
    /// <param name="json">The serializer.</param>
    /// <param name="telemetry">Its attributed telemetry emitter.</param>
    /// <param name="events">Its filtered event publisher.</param>
    /// <param name="runtime">Facts about the running host.</param>
    /// <param name="operatingSystem">Facts about the operating system.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="http">Its attributed outbound gateway, when it holds the privilege.</param>
    /// <param name="rateLimiter">Its composed limiter, when it holds the privilege.</param>
    /// <param name="certificatePolicy">The certificate policy, when it holds the privilege.</param>
    /// <param name="fileSystem">Its confined file system, when it holds the privilege.</param>
    /// <param name="fileTransfer">The file transfer service, when it holds the privilege.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public PluginContext(
        PluginId pluginId,
        string pluginVersion,
        string hostContractVersion,
        CapabilitySet capabilities,
        IPluginRegistry registry,
        IPluginPaths paths,
        ICacheProvider cache,
        IJsonSerializer json,
        ITelemetryEmitter telemetry,
        IEventPublisher events,
        IHostRuntimeInfo runtime,
        IOperatingSystemInfo operatingSystem,
        TimeProvider clock,
        IHttpGateway? http = null,
        IRateLimiter? rateLimiter = null,
        ICertificateValidationPolicy? certificatePolicy = null,
        IFileSystem? fileSystem = null,
        IFileTransferService? fileTransfer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostContractVersion);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(operatingSystem);
        ArgumentNullException.ThrowIfNull(clock);

        PluginId = pluginId;
        PluginVersion = pluginVersion;
        HostContractVersion = hostContractVersion;
        Capabilities = capabilities;
        Registry = registry;
        Paths = paths;
        Cache = cache;
        Json = json;
        Telemetry = telemetry;
        Events = events;
        Runtime = runtime;
        OperatingSystem = operatingSystem;
        Clock = clock;

        _http = http;
        _rateLimiter = rateLimiter;
        _certificatePolicy = certificatePolicy;
        _fileSystem = fileSystem;
        _fileTransfer = fileTransfer;
    }

    /// <inheritdoc />
    public PluginId PluginId { get; }

    /// <inheritdoc />
    public string PluginVersion { get; }

    /// <inheritdoc />
    public string HostContractVersion { get; }

    /// <inheritdoc />
    public CapabilitySet Capabilities { get; }

    /// <inheritdoc />
    public IPluginRegistry Registry { get; }

    /// <inheritdoc />
    public IPluginPaths Paths { get; }

    /// <inheritdoc />
    public ICacheProvider Cache { get; }

    /// <inheritdoc />
    public IJsonSerializer Json { get; }

    /// <inheritdoc />
    public ITelemetryEmitter Telemetry { get; }

    /// <inheritdoc />
    public IEventPublisher Events { get; }

    /// <inheritdoc />
    public IHostRuntimeInfo Runtime { get; }

    /// <inheritdoc />
    public IOperatingSystemInfo OperatingSystem { get; }

    /// <inheritdoc />
    public TimeProvider Clock { get; }

    /// <inheritdoc />
    public bool TryGetHttp(out IHttpGateway? gateway)
    {
        gateway = Permitted(typeof(IHttpGateway)) ? _http : null;
        return gateway is not null;
    }

    /// <inheritdoc />
    public bool TryGetRateLimiter(out IRateLimiter? limiter)
    {
        limiter = Permitted(typeof(IRateLimiter)) ? _rateLimiter : null;
        return limiter is not null;
    }

    /// <inheritdoc />
    public bool TryGetCertificatePolicy(out ICertificateValidationPolicy? policy)
    {
        policy = Permitted(typeof(ICertificateValidationPolicy)) ? _certificatePolicy : null;
        return policy is not null;
    }

    /// <inheritdoc />
    public bool TryGetFileSystem(out IFileSystem? fileSystem)
    {
        fileSystem = Permitted(typeof(IFileSystem)) ? _fileSystem : null;
        return fileSystem is not null;
    }

    /// <inheritdoc />
    public bool TryGetFileTransfer(out IFileTransferService? transfer)
    {
        transfer = Permitted(typeof(IFileTransferService)) ? _fileTransfer : null;
        return transfer is not null;
    }

    /// <inheritdoc />
    public IHttpGateway RequireHttp() => Require(_http, typeof(IHttpGateway));

    /// <inheritdoc />
    public IFileSystem RequireFileSystem() => Require(_fileSystem, typeof(IFileSystem));

    /// <inheritdoc />
    public IFileTransferService RequireFileTransfer() => Require(_fileTransfer, typeof(IFileTransferService));

    private bool Permitted(Type contract) => CapabilityMatrix.IsPermitted(Capabilities, contract);

    /// <summary>
    /// Distinguishes the two ways a gated dependency can be unavailable.
    /// </summary>
    /// <remarks>
    /// A missing capability is the extension's mistake and is reported as one. A missing implementation is
    /// the host's, and saying so plainly is what stops an operator filing it against the extension.
    /// </remarks>
    private TContract Require<TContract>(TContract? instance, Type contract)
        where TContract : class
    {
        if (!Permitted(contract))
        {
            throw new PluginCapabilityException(
                PluginId,
                CapabilityMatrix.RequirementToReport(contract),
                contract.Name);
        }

        return instance
            ?? throw new InvalidOperationException(
                $"Extension '{PluginId}' holds the capability for '{contract.Name}', but this host has registered no implementation of it.");
    }
}
