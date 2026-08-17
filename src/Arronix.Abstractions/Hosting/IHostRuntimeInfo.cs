using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Hosting;

/// <summary>
/// Describes how the host process itself was started.
/// </summary>
/// <remarks>
/// The contract is read-only by construction. Mutable lifecycle state — starting, exiting, restart
/// pending — belongs to the framework's application lifetime abstraction, not here: a property that any
/// consumer can set is a shared mutable global wearing an interface.
/// </remarks>
[Experimental(ExperimentalContracts.Hosting, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IHostRuntimeInfo
{
    /// <summary>
    /// Gets the instant the host process started.
    /// </summary>
    DateTimeOffset StartTime { get; }

    /// <summary>
    /// Gets a value indicating whether the process has an attached console a user can interact with.
    /// </summary>
    bool IsUserInteractive { get; }

    /// <summary>
    /// Gets a value indicating whether the process is running with elevated privileges.
    /// </summary>
    bool IsAdministrator { get; }

    /// <summary>
    /// Gets a value indicating whether the process is running as a managed operating system service.
    /// </summary>
    bool IsWindowsService { get; }

    /// <summary>
    /// Gets a value indicating whether the process is running inside a container.
    /// </summary>
    bool IsContainerized { get; }

    /// <summary>
    /// Gets the path of the executable that started the process, or <see langword="null"/> when it
    /// cannot be determined — which is the normal outcome under some single-file publish layouts.
    /// </summary>
    string? ExecutingApplication { get; }
}
