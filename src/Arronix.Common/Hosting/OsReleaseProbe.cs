using System.Linq;
using Arronix.Abstractions.Hosting;

namespace Arronix.Common.Hosting;

/// <summary>
/// Reads a Linux distribution's identity out of the freedesktop <c>os-release</c> file.
/// </summary>
/// <remarks>
/// The one probe the platform ships: the source every mainstream distribution agrees on. Vendor-specific
/// readers belong to platform packs, which is what the probe contract exists for.
/// </remarks>
internal sealed class OsReleaseProbe : IOsVersionProbe
{
    private static readonly string[] CandidatePaths = ["/etc/os-release", "/usr/lib/os-release"];

    private readonly IPlatformFacts _facts;

    /// <summary>Initializes a new instance of the <see cref="OsReleaseProbe"/> class.</summary>
    internal OsReleaseProbe()
        : this(PlatformFacts.Instance)
    {
    }

    internal OsReleaseProbe(IPlatformFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        _facts = facts;
    }

    /// <inheritdoc />
    public bool IsSupported => _facts.IsLinux && CandidatePaths.Any(_facts.FileExists);

    /// <inheritdoc />
    public OsVersionDescriptor? Read()
    {
        foreach (var path in CandidatePaths)
        {
            if (_facts.ReadFile(path) is not { Length: > 0 } contents)
            {
                continue;
            }

            var fields = Parse(contents);

            if (!fields.TryGetValue("NAME", out var name) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            fields.TryGetValue("VERSION_ID", out var version);
            fields.TryGetValue("PRETTY_NAME", out var pretty);

            return OsVersionDescriptor.Create(name, version ?? string.Empty, pretty);
        }

        return null;
    }

    /// <summary>Reads the <c>KEY=value</c> lines, ignoring comments, blanks and lines without a separator.</summary>
    /// <remarks>Quoting is left to <see cref="OsVersionDescriptor.Create"/>, which already strips it.</remarks>
    private static Dictionary<string, string> Parse(string contents)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in contents.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            var separator = trimmed.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0)
            {
                continue;
            }

            fields[trimmed[..separator]] = trimmed[(separator + 1)..];
        }

        return fields;
    }
}
