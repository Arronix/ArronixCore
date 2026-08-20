
namespace Arronix.Abstractions.Hosting;

/// <summary>
/// Describes the operating system the host is running on, including whether it is containerized.
/// </summary>
/// <remarks>
/// Containerization awareness is why this crosses the extension boundary. An extension that talks to a
/// service reachable at a different path inside a container than outside it — the canonical case being
/// a path-mapping health check — cannot produce a correct diagnosis without knowing.
/// </remarks>
public interface IOperatingSystemInfo
{
    /// <summary>
    /// Gets the short product name of the operating system.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the operating system version as the platform reports it.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the display name of the operating system.
    /// </summary>
    string FullName { get; }

    /// <summary>
    /// Gets a value indicating whether the host is running under Docker.
    /// </summary>
    bool IsDocker { get; }

    /// <summary>
    /// Gets a value indicating whether the host is running under Podman.
    /// </summary>
    bool IsPodman { get; }

    /// <summary>
    /// Gets a value indicating whether the host is running inside any container runtime. Prefer this
    /// over the runtime-specific properties unless the distinction actually matters.
    /// </summary>
    bool IsContainerized { get; }
}
