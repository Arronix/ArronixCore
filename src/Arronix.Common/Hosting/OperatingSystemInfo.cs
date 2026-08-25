using System.Globalization;
using Arronix.Abstractions.Hosting;
using Arronix.Common.Lifetimes;

namespace Arronix.Common.Hosting;

/// <summary>
/// The platform's answer to "what am I running on", read once at composition.
/// </summary>
/// <remarks>
/// Established once at composition and never re-read, so no file read sits behind an extension's property
/// getter. Identity comes from the registered <see cref="IOsVersionProbe"/> chain in registration order,
/// then from the BCL; when neither answers the result is <see cref="UnknownName"/> rather than a guess.
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

    /// <summary>Takes the first answer from the first supported probe, in registration order.</summary>
    /// <remarks>
    /// The contract requires a probe that cannot answer to return <see langword="null"/>. A probe that
    /// throws has broken it, and probes are host-trusted, so the failure is reported with the probe's
    /// identity rather than absorbed as another way of saying nothing.
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
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
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
            // No release file answered, so the kernel version is what is truthfully known.
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
