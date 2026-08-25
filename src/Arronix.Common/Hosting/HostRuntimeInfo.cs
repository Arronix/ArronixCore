using Arronix.Abstractions.Hosting;

namespace Arronix.Common.Hosting;

/// <summary>
/// How this host process was started, read once at composition.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, as the contract requires. Mutable lifecycle state belongs to the framework's application
/// lifetime; everything here is a fact about the process that was already true before the platform was
/// composed.
/// </para>
/// <para>
/// Containerization is not detected a second time. It is taken from <see cref="IOperatingSystemInfo"/>,
/// which makes the two contracts' agreement structural rather than a coincidence two detectors happen to
/// share — a disagreement between them would have an extension mapping paths one way and reporting health
/// another.
/// </para>
/// </remarks>
public sealed class HostRuntimeInfo : IHostRuntimeInfo
{
    /// <summary>Initializes a new instance of the <see cref="HostRuntimeInfo"/> class.</summary>
    /// <param name="operatingSystem">The operating-system facts, which own containerization.</param>
    /// <param name="clock">The clock, used only when the process start time cannot be read.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public HostRuntimeInfo(IOperatingSystemInfo operatingSystem, TimeProvider clock)
        : this(operatingSystem, clock, PlatformFacts.Instance)
    {
    }

    internal HostRuntimeInfo(IOperatingSystemInfo operatingSystem, TimeProvider clock, IPlatformFacts facts)
    {
        ArgumentNullException.ThrowIfNull(operatingSystem);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(facts);

        // A host that cannot read its own process start time reports when the platform was composed, which
        // is the earliest instant this code can honestly speak for. Reporting a default date would put a
        // wrong uptime on every status page that subtracts it from now.
        StartTime = facts.ProcessStartTime ?? clock.GetUtcNow();
        ExecutingApplication = facts.ProcessPath;
        IsAdministrator = facts.IsPrivilegedProcess;
        IsContainerized = operatingSystem.IsContainerized;
        IsUserInteractive = facts.IsUserInteractive;

        // Windows services run in session 0 and the platform reports that directly. There is deliberately
        // no equivalent derived elsewhere: a systemd unit is not a Windows service, and answering true for
        // one here would make the property mean two different things on two platforms.
        IsWindowsService = facts.IsWindows && !facts.IsUserInteractive;
    }

    /// <inheritdoc />
    public DateTimeOffset StartTime { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The platform's own answer. On Windows a service reports false here, which is the same fact
    /// <see cref="IsWindowsService"/> reports from the other side. On Unix the platform reports every
    /// process as interactive, so the value there is what the platform said and not a derived claim.
    /// </remarks>
    public bool IsUserInteractive { get; }

    /// <inheritdoc />
    public bool IsAdministrator { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Windows only, as the member's name says. A process started by systemd or launchd is not a Windows
    /// service and is reported as false; a host that needs to know about those needs a fact of its own
    /// rather than this one widened to mean "started by something".
    /// </remarks>
    public bool IsWindowsService { get; }

    /// <inheritdoc />
    public bool IsContainerized { get; }

    /// <inheritdoc />
    public string? ExecutingApplication { get; }
}
