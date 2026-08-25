namespace Arronix.Common.Hosting;

/// <summary>
/// Answers whether this process was started by the Windows service control manager.
/// </summary>
/// <remarks>
/// A seam rather than a check, because there is no reliable in-box test: a non-interactive Windows process
/// may equally be a scheduled task, a container entry point or a redirected console, and treating any of
/// them as a service would make <see cref="Arronix.Abstractions.Hosting.IHostRuntimeInfo.IsWindowsService"/>
/// mean "not obviously interactive". The platform ships no detector, so a host that can run as a service
/// registers <c>WindowsServiceHelpers</c> or its own.
/// </remarks>
public interface IWindowsServiceDetector
{
    /// <summary>Gets a value indicating whether this process is a Windows service.</summary>
    bool IsWindowsService { get; }
}

/// <summary>The default answer for a host that registered no detector: not a service.</summary>
internal sealed class UndetectedWindowsService : IWindowsServiceDetector
{
    /// <inheritdoc />
    public bool IsWindowsService => false;
}
