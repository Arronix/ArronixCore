using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Hosting;

/// <summary>
/// The state of a managed operating system service, as reported by the platform's service manager.
/// </summary>
[Experimental(ExperimentalContracts.Hosting, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum ServiceLifecycleStatus
{
    /// <summary>The state could not be determined.</summary>
    Unknown = 0,

    /// <summary>No service with that name is registered.</summary>
    NotInstalled = 1,

    /// <summary>Registered but not running.</summary>
    Stopped = 2,

    /// <summary>Starting up.</summary>
    Starting = 3,

    /// <summary>Running.</summary>
    Running = 4,

    /// <summary>Shutting down.</summary>
    Stopping = 5
}

/// <summary>
/// Queries and restarts a managed operating system service.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by platform packs — one per service manager. The contract is limited to querying state
/// and requesting a restart. Installing, uninstalling and adjusting a service's access control are
/// packaging concerns that leave the running process entirely, and exposing them here would put the
/// host one call away from de-registering itself.
/// </para>
/// <para>
/// The name also resolves the collision with the framework's own service-locator interface, which the
/// legacy name shadowed.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Hosting, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IServiceLifecycleController
{
    /// <summary>
    /// Gets a value indicating whether a service manager this controller understands is present.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Reads the current state of a service.
    /// </summary>
    /// <param name="serviceName">The service manager's name for the service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The service state.</returns>
    Task<ServiceLifecycleStatus> GetStatusAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the service manager to restart a service.
    /// </summary>
    /// <param name="serviceName">The service manager's name for the service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the restart has been requested.</returns>
    /// <remarks>
    /// Completion means the request was accepted, not that the service is running again — the caller is
    /// frequently the process being restarted.
    /// </remarks>
    Task RequestRestartAsync(string serviceName, CancellationToken cancellationToken = default);
}
