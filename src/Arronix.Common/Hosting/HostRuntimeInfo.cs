using Arronix.Abstractions.Hosting;

namespace Arronix.Common.Hosting;

/// <summary>
/// How this host process was started, read once at composition.
/// </summary>
/// <remarks>
/// Immutable, as the contract requires. Containerization is taken from <see cref="IOperatingSystemInfo"/>
/// rather than detected again, so the two contracts agree structurally.
/// </remarks>
public sealed class HostRuntimeInfo : IHostRuntimeInfo
{
    /// <summary>Initializes a new instance of the <see cref="HostRuntimeInfo"/> class.</summary>
    /// <param name="operatingSystem">The operating-system facts, which own containerization.</param>
    /// <param name="clock">The clock, used only when the process start time cannot be read.</param>
    /// <param name="serviceDetector">Whether the service control manager started this process.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public HostRuntimeInfo(
        IOperatingSystemInfo operatingSystem,
        TimeProvider clock,
        IWindowsServiceDetector serviceDetector)
        : this(operatingSystem, clock, serviceDetector, PlatformFacts.Instance)
    {
    }

    internal HostRuntimeInfo(
        IOperatingSystemInfo operatingSystem,
        TimeProvider clock,
        IWindowsServiceDetector serviceDetector,
        IPlatformFacts facts)
    {
        ArgumentNullException.ThrowIfNull(operatingSystem);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(serviceDetector);
        ArgumentNullException.ThrowIfNull(facts);

        // A host that cannot read its process start time reports when the platform was composed: the
        // earliest instant this code can speak for, and not a default date every status page would subtract.
        StartTime = facts.ProcessStartTime ?? clock.GetUtcNow();
        ExecutingApplication = facts.ProcessPath;
        IsAdministrator = facts.IsPrivilegedProcess;
        IsContainerized = operatingSystem.IsContainerized;
        IsUserInteractive = facts.IsUserInteractive;

        // Answered by the registered detector, never derived from interactivity: a non-interactive Windows
        // process may be a scheduled task or a container entry point. False off Windows by definition.
        IsWindowsService = facts.IsWindows && serviceDetector.IsWindowsService;
    }

    /// <inheritdoc />
    public DateTimeOffset StartTime { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The platform's own answer. On Unix every process is reported interactive, so the value there is
    /// what the platform said rather than a derived claim.
    /// </remarks>
    public bool IsUserInteractive { get; }

    /// <inheritdoc />
    public bool IsAdministrator { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Windows only, as the member's name says, and answered by the registered
    /// <see cref="IWindowsServiceDetector"/>. A host that registers none reports false, and a systemd or
    /// launchd process reports false because it is not a Windows service.
    /// </remarks>
    public bool IsWindowsService { get; }

    /// <inheritdoc />
    public bool IsContainerized { get; }

    /// <inheritdoc />
    public string? ExecutingApplication { get; }
}
