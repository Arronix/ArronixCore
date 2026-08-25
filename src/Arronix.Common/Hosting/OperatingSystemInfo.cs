using System.Globalization;
using Arronix.Abstractions.Hosting;

namespace Arronix.Common.Hosting;

/// <summary>
/// The platform's answer to "what am I running on", read once at composition.
/// </summary>
/// <remarks>
/// <para>
/// Immutable by construction. Every value is established when the host is composed and never re-read,
/// because the operating system does not change under a running process and a property that re-probed on
/// every access would put a file read behind an extension's property getter.
/// </para>
/// <para>
/// The identity comes from the registered <see cref="IOsVersionProbe"/> chain first, in registration order,
/// then from what the BCL can state about the platform it is running on. When neither can answer, the
/// result is <see cref="UnknownName"/> rather than a guess: an extension that maps behaviour by
/// distribution must be able to tell "I do not know" from "Ubuntu".
/// </para>
/// </remarks>
public sealed class OperatingSystemInfo : IOperatingSystemInfo
{
    /// <summary>The name reported when no probe and no platform API could identify the system.</summary>
    public const string UnknownName = "unknown";

    /// <summary>Initializes a new instance of the <see cref="OperatingSystemInfo"/> class.</summary>
    /// <param name="probes">The identity probes, consulted in registration order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="probes"/> is <see langword="null"/>.</exception>
    public OperatingSystemInfo(IEnumerable<IOsVersionProbe> probes)
        : this(probes, PlatformFacts.Instance)
    {
    }

    internal OperatingSystemInfo(IEnumerable<IOsVersionProbe> probes, IPlatformFacts facts)
    {
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(facts);

        var identity = FromProbes(probes) ?? FromPlatform(facts);

        Name = identity.Name;
        Version = identity.Version;
        FullName = identity.FullName;

        var container = ContainerDetector.Detect(facts);
        IsDocker = container.IsDocker;
        IsPodman = container.IsPodman;
        IsContainerized = container.IsContainerized;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Version { get; }

    /// <inheritdoc />
    public string FullName { get; }

    /// <inheritdoc />
    public bool IsDocker { get; }

    /// <inheritdoc />
    public bool IsPodman { get; }

    /// <inheritdoc />
    public bool IsContainerized { get; }

    /// <summary>
    /// Takes the first answer from the first supported probe, in registration order.
    /// </summary>
    /// <remarks>
    /// <see cref="IOsVersionProbe"/> requires a probe that ran and could not determine an identity to
    /// return <see langword="null"/>, precisely so that "no answer" is ordinary and needs no exception.
    /// A probe that throws has broken that contract, and probes are host-trusted platform-pack code rather
    /// than extension code — so it is reported with the probe's identity rather than absorbed as another
    /// way of saying nothing. Swallowing it would leave a host quietly misidentifying its own operating
    /// system with no evidence anywhere that a probe was even installed.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A probe threw instead of answering.</exception>
    private static OsVersionDescriptor? FromProbes(IEnumerable<IOsVersionProbe> probes)
    {
        foreach (var probe in probes)
        {
            OsVersionDescriptor? read;

            try
            {
                if (!probe.IsSupported)
                {
                    continue;
                }

                read = probe.Read();
            }
#pragma warning disable CA1031
            catch (Exception failure)
#pragma warning restore CA1031
            {
                throw new InvalidOperationException(
                    $"Operating-system identity probe '{probe.GetType().FullName ?? probe.GetType().Name}' "
                    + "threw instead of returning null. A probe that cannot answer must say so by returning "
                    + "null.",
                    failure);
            }

            if (read is not null)
            {
                return read;
            }
        }

        return null;
    }

    private static OsVersionDescriptor FromPlatform(IPlatformFacts facts)
    {
        var description = facts.OperatingSystemDescription;
        var version = facts.OperatingSystemVersion.ToString();

        if (facts.IsWindows)
        {
            return OsVersionDescriptor.Create("Windows", version, description);
        }

        if (facts.IsMacOS)
        {
            return OsVersionDescriptor.Create("macOS", version, description);
        }

        if (facts.IsFreeBsd)
        {
            return OsVersionDescriptor.Create("FreeBSD", version, description);
        }

        if (facts.IsLinux)
        {
            // The kernel version is the only thing left once no release file answered, and it is a
            // truthful statement about a Linux system whose distribution is genuinely unidentified.
            return OsVersionDescriptor.Create("Linux", version, description);
        }

        return OsVersionDescriptor.Create(
            UnknownName,
            UnknownName,
            string.IsNullOrWhiteSpace(description)
                ? UnknownName
                : string.Create(CultureInfo.InvariantCulture, $"{UnknownName} ({description})"));
    }
}
