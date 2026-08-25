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

        if (IsTrue(facts.EnvironmentVariable(RunningInContainerVariable))
            || !string.IsNullOrWhiteSpace(declared)
            || HasContainerCgroup(facts))
        {
            // Inside something, and nothing said what.
            return new ContainerDetection(IsContainerized: true, IsDocker: false, IsPodman: false);
        }

        return ContainerDetection.None;
    }

    private static bool IsTrue(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.Ordinal);

    private static bool HasContainerCgroup(IPlatformFacts facts)
    {
        if (facts.ReadFile(CgroupFile) is not { Length: > 0 } cgroup)
        {
            return false;
        }

        return cgroup.Contains("/docker", StringComparison.Ordinal)
            || cgroup.Contains("/containerd", StringComparison.Ordinal)
            || cgroup.Contains("kubepods", StringComparison.Ordinal)
            || cgroup.Contains("/libpod", StringComparison.Ordinal);
    }
}
