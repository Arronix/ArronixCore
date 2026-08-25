using Arronix.Common.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Arronix.Host.Hosting;

/// <summary>
/// Answers whether the Windows service control manager started this process, using the framework's own test.
/// </summary>
/// <remarks>
/// The shared assembly ships no detector, because there is no reliable test it could apply from where it
/// sits. <see cref="WindowsServiceHelpers.IsWindowsService"/> is the official one; off Windows it is
/// <see langword="false"/>.
/// </remarks>
internal sealed class WindowsServiceDetector : IWindowsServiceDetector
{
    /// <inheritdoc />
    public bool IsWindowsService => WindowsServiceHelpers.IsWindowsService();
}
