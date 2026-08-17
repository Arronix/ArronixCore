using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Hosting;

/// <summary>
/// What an <see cref="IOsVersionProbe"/> managed to determine about the operating system.
/// </summary>
/// <param name="Name">The short product name, for example <c>Ubuntu</c>.</param>
/// <param name="Version">The version as the platform reports it, for example <c>24.04</c>.</param>
/// <param name="FullName">The display name, normally the name and version together.</param>
[Experimental(ExperimentalContracts.Hosting, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record OsVersionDescriptor(string Name, string Version, string FullName)
{
    /// <summary>
    /// Creates a descriptor from raw probe output, trimming whitespace and the quoting that release
    /// files and shell output routinely carry, and composing a display name when none was supplied.
    /// </summary>
    /// <param name="name">The raw product name.</param>
    /// <param name="version">The raw version.</param>
    /// <param name="fullName">
    /// The raw display name, or <see langword="null"/> to compose one from the other two.
    /// </param>
    /// <returns>The normalized descriptor.</returns>
    public static OsVersionDescriptor Create(string name, string version, string? fullName = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(version);

        var trimmedName = Normalize(name);
        var trimmedVersion = Normalize(version);
        var trimmedFullName = string.IsNullOrWhiteSpace(fullName)
            ? $"{trimmedName} {trimmedVersion}".Trim()
            : Normalize(fullName);

        return new OsVersionDescriptor(trimmedName, trimmedVersion, trimmedFullName);
    }

    private static string Normalize(string value) => value.Trim().Trim('"', '\'').Trim();
}
