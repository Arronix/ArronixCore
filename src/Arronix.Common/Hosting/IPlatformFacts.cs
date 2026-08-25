using System.Runtime.InteropServices;

namespace Arronix.Common.Hosting;

/// <summary>
/// Everything the host reads about the machine it is running on, behind one seam.
/// </summary>
/// <remarks>
/// Docker, Podman, an ordinary host, an elevated process and a service are answers derived from files and
/// environment variables a test must not create on the developer's machine, so the reads are named here and
/// substituted there. Internal: it is a reading surface, not a platform abstraction.
/// </remarks>
internal interface IPlatformFacts
{
    /// <summary>Gets a value indicating whether the current platform is Windows.</summary>
    bool IsWindows { get; }

    /// <summary>Gets a value indicating whether the current platform is Linux.</summary>
    bool IsLinux { get; }

    /// <summary>Gets a value indicating whether the current platform is macOS.</summary>
    bool IsMacOS { get; }

    /// <summary>Gets a value indicating whether the current platform is FreeBSD.</summary>
    bool IsFreeBsd { get; }

    /// <summary>Gets the platform's own description of itself, as <see cref="RuntimeInformation"/> reports it.</summary>
    string OperatingSystemDescription { get; }

    /// <summary>Gets the platform's own version number.</summary>
    Version OperatingSystemVersion { get; }

    /// <summary>Gets a value indicating whether the process runs elevated, or as root.</summary>
    bool IsPrivilegedProcess { get; }

    /// <summary>Gets a value indicating whether the platform reports an interactive session.</summary>
    bool IsUserInteractive { get; }

    /// <summary>Gets the path of the executable that started the process, when the platform reports one.</summary>
    string? ProcessPath { get; }

    /// <summary>Gets when the current process started, or <see langword="null"/> when it cannot be read.</summary>
    DateTimeOffset? ProcessStartTime { get; }

    /// <summary>Reads an environment variable, or <see langword="null"/> when it is unset.</summary>
    /// <param name="name">The variable's name.</param>
    /// <returns>Its value, or <see langword="null"/>.</returns>
    string? EnvironmentVariable(string name);

    /// <summary>Reports whether a file exists, without throwing for an unreadable path.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the file exists.</returns>
    bool FileExists(string path);

    /// <summary>Reads a text file, or returns <see langword="null"/> when it cannot be read.</summary>
    /// <param name="path">The path to read.</param>
    /// <returns>The contents, or <see langword="null"/>.</returns>
    string? ReadFile(string path);
}
