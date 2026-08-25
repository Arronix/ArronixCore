
namespace Arronix.Abstractions.Hosting;

/// <summary>
/// Describes how the host process itself was started.
/// </summary>
/// <remarks>
/// The contract is read-only by construction. Mutable lifecycle state — starting, exiting, restart
/// pending — belongs to the framework's application lifetime abstraction, not here: a property that any
/// consumer can set is a shared mutable global wearing an interface.
/// </remarks>
public interface IHostRuntimeInfo
{
    /// <summary>
    /// Gets the instant the host process started, or <see langword="null"/> when the platform will not say —
    /// a sandbox that denies the process table, for instance. No stand-in value is substituted.
    /// </summary>
    DateTimeOffset? StartTime { get; }

    /// <summary>
    /// Gets the platform's own answer to whether the process runs in user-interactive mode
    /// (<c>Environment.UserInteractive</c>). Unix reports every process interactive.
    /// </summary>
    bool IsUserInteractive { get; }

    /// <summary>
    /// Gets a value indicating whether the process is running with elevated privileges.
    /// </summary>
    bool IsAdministrator { get; }

    /// <summary>
    /// Gets a value indicating whether the Windows service control manager started this process. Always
    /// <see langword="false"/> elsewhere; a systemd unit or a launchd job is not this fact.
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
