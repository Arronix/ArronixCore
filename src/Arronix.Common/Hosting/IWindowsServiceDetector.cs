namespace Arronix.Common.Hosting;

/// <summary>
/// Answers whether this process was started by the Windows service control manager.
/// </summary>
/// <remarks>
/// A seam rather than a check, because this assembly has no reliable test to apply: a non-interactive
/// Windows process may equally be a scheduled task, a container entry point or a redirected console, and
/// treating any of them as a service would make
/// <see cref="Arronix.Abstractions.Hosting.IHostRuntimeInfo.IsWindowsService"/> mean "not obviously
/// interactive". This assembly therefore has no Windows-specific default at all. <c>AddArronixHost</c>
/// registers the official <c>WindowsServiceHelpers.IsWindowsService()</c> implementation before the
/// fallback below is reached.
/// </remarks>
public interface IWindowsServiceDetector
{
    /// <summary>Gets a value indicating whether this process is a Windows service.</summary>
    bool IsWindowsService { get; }
}

/// <summary>
/// The answer for a host that registered no detector: not established, which is reported as not a service.
/// </summary>
internal sealed class UndetectedWindowsService : IWindowsServiceDetector
{
    /// <inheritdoc />
    public bool IsWindowsService => false;
}
