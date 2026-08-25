namespace Arronix.Common.Hosting;

/// <summary>
/// What a host could establish about the container runtime it is inside, if any.
/// </summary>
/// <param name="IsContainerized">Whether the process is inside any container runtime.</param>
/// <param name="IsDocker">Whether that runtime was identified as Docker.</param>
/// <param name="IsPodman">Whether that runtime was identified as Podman.</param>
/// <remarks>
/// Containerized without either flag is a real answer: a container whose runtime left no marker this
/// platform recognizes. Naming a runtime on that evidence would be a guess.
/// </remarks>
internal readonly record struct ContainerDetection(bool IsContainerized, bool IsDocker, bool IsPodman)
{
    /// <summary>The answer for a process that is not in a container.</summary>
    internal static ContainerDetection None { get; }
}

/// <summary>
/// Decides whether the host is inside a container, and which runtime put it there.
/// </summary>
/// <remarks>
/// The signals the runtimes publish, most specific first. Podman is tested before Docker because a Podman
/// container built from a Docker-compatible image can carry both markers. The cgroup path is the last
/// resort: cgroup v2 hosts routinely show nothing useful there.
/// </remarks>
internal static class ContainerDetector
{
    private const string DockerMarkerFile = "/.dockerenv";
    private const string ContainerEnvironmentFile = "/run/.containerenv";
    private const string CgroupFile = "/proc/1/cgroup";
    private const string RunningInContainerVariable = "DOTNET_RUNNING_IN_CONTAINER";
    private const string ContainerVariable = "container";

    /// <summary>Reads the container facts from one platform reading surface.</summary>
    /// <param name="facts">Where the environment and file reads come from.</param>
    /// <returns>What could be established.</returns>
    internal static ContainerDetection Detect(IPlatformFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var declared = facts.EnvironmentVariable(ContainerVariable);
        var podman = string.Equals(declared, "podman", StringComparison.OrdinalIgnoreCase)
            || facts.FileExists(ContainerEnvironmentFile);

        if (podman)
        {
            return new ContainerDetection(IsContainerized: true, IsDocker: false, IsPodman: true);
        }

        if (facts.FileExists(DockerMarkerFile)
            || string.Equals(declared, "docker", StringComparison.OrdinalIgnoreCase))
        {
            return new ContainerDetection(IsContainerized: true, IsDocker: true, IsPodman: false);
        }

        var cgroup = FromCgroup(facts);

        if (cgroup.IsDocker || cgroup.IsPodman)
        {
            return cgroup;
        }

        if (IsTrue(facts.EnvironmentVariable(RunningInContainerVariable))
            || !string.IsNullOrWhiteSpace(declared)
            || cgroup.IsContainerized)
        {
            // Inside something, and nothing said what.
            return new ContainerDetection(IsContainerized: true, IsDocker: false, IsPodman: false);
        }

        return ContainerDetection.None;
    }

    private static bool IsTrue(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal);

    /// <summary>
    /// What the init process's cgroup path says. Two of these markers name the runtime that wrote them; the
    /// others say a container and nothing about which one, so that is all they are read as.
    /// </summary>
    private static ContainerDetection FromCgroup(IPlatformFacts facts)
    {
        if (facts.ReadFile(CgroupFile) is not { Length: > 0 } cgroup)
        {
            return ContainerDetection.None;
        }

        // Podman first, for the reason it is first above: its containers can carry Docker-shaped markers.
        if (cgroup.Contains("/libpod", StringComparison.Ordinal))
        {
            return new ContainerDetection(IsContainerized: true, IsDocker: false, IsPodman: true);
        }

        if (cgroup.Contains("/docker", StringComparison.Ordinal))
        {
            return new ContainerDetection(IsContainerized: true, IsDocker: true, IsPodman: false);
        }

        return cgroup.Contains("/containerd", StringComparison.Ordinal)
            || cgroup.Contains("kubepods", StringComparison.Ordinal)
                ? new ContainerDetection(IsContainerized: true, IsDocker: false, IsPodman: false)
                : ContainerDetection.None;
    }
}
