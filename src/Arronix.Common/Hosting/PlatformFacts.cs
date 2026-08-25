using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Arronix.Common.Hosting;

/// <summary>
/// The production reading surface: the BCL, the environment, and the file system.
/// </summary>
/// <remarks>
/// Every read that can fail on a locked-down or containerized host is contained here and answers
/// <see langword="null"/> or <see langword="false"/> rather than throwing, because a fact the platform
/// cannot establish is a fact the caller has to be told is absent — not an exception thrown out of a
/// property an extension read.
/// </remarks>
internal sealed class PlatformFacts : IPlatformFacts
{
    /// <summary>Gets the shared instance. It reads process-wide state and holds nothing of its own.</summary>
    internal static PlatformFacts Instance { get; } = new();

    /// <inheritdoc />
    public bool IsWindows => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public bool IsLinux => OperatingSystem.IsLinux();

    /// <inheritdoc />
    public bool IsMacOS => OperatingSystem.IsMacOS();

    /// <inheritdoc />
    public bool IsFreeBsd => OperatingSystem.IsFreeBSD();

    /// <inheritdoc />
    public string OperatingSystemDescription => RuntimeInformation.OSDescription;

    /// <inheritdoc />
    public Version OperatingSystemVersion => Environment.OSVersion.Version;

    /// <inheritdoc />
    public bool IsPrivilegedProcess => Environment.IsPrivilegedProcess;

    /// <inheritdoc />
    public bool IsUserInteractive => Environment.UserInteractive;

    /// <inheritdoc />
    public string? ProcessPath => Environment.ProcessPath;

    /// <inheritdoc />
    public DateTimeOffset? ProcessStartTime
    {
        get
        {
            try
            {
                using var current = Process.GetCurrentProcess();
                return new DateTimeOffset(current.StartTime.ToUniversalTime(), TimeSpan.Zero);
            }
            // A sandbox that denies /proc, and a platform that does not expose the value, both mean the
            // same thing to a caller: this host cannot report when its process started.
#pragma warning disable CA1031
            catch (Exception)
#pragma warning restore CA1031
            {
                return null;
            }
        }
    }

    /// <inheritdoc />
    public string? EnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
