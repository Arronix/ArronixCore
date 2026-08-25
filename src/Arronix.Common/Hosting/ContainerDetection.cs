namespace Arronix.Common.Hosting;

/// <summary>
/// What a host could establish about the container runtime it is inside, if any.
/// </summary>
/// <param name="IsContainerized">Whether the process is inside any container runtime.</param>
/// <param name="IsDocker">Whether that runtime was identified as Docker.</param>
/// <param name="IsPodman">Whether that runtime was identified as Podman.</param>
/// <remarks>
/// Containerized without either flag is a real and reportable answer: the process is demonstrably inside a
/// container whose runtime left no marker this platform recognizes. Reporting Docker because something is
/// a container would be a guess, and an extension mapping paths on that guess would map them wrongly.
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
/// <para>
/// The signals are the ones the runtimes themselves publish, in order of how specific they are. Podman
/// writes <c>/run/.containerenv</c> and sets <c>container=podman</c>; Docker writes <c>/.dockerenv</c>;
/// both, and every orchestrator built on them, set <c>DOTNET_RUNNING_IN_CONTAINER</c> when the image was
/// built from Microsoft's base images. The cgroup path is the last resort and the least reliable, because
/// cgroup v2 hosts routinely show nothing useful there.
/// </para>
/// <para>
/// Podman is tested before Docker because a Podman container built from a Docker-compatible image can
/// carry both markers, and in that case it is a Podman container.
/// </para>
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
            // Inside something, and nothing said what. That is the honest answer, and it is why the two
            // runtime flags are separate from the containerized one rather than derived from it.
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
