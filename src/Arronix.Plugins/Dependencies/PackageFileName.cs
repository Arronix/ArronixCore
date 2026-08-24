using System.IO;

namespace Arronix.Plugins.Dependencies;

/// <summary>
/// The one rule for a file name a package declares: bare, and inside the package's own folder.
/// </summary>
/// <remarks>
/// Manifest validation reports a violation against the member that carries it; the installed-package model
/// refuses one outright. Both ask this type, so the two cannot drift.
/// </remarks>
internal static class PackageFileName
{
    /// <summary>Determines whether a declared file name is a bare name inside the package folder.</summary>
    /// <param name="fileName">The declared name.</param>
    /// <returns><see langword="true"/> when the name may be combined with the package folder.</returns>
    public static bool IsBare(string? fileName)
        => !string.IsNullOrWhiteSpace(fileName)
            && !fileName.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !fileName.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            && !fileName.Contains('/', StringComparison.Ordinal)
            && !fileName.Contains('\\', StringComparison.Ordinal)
            && !fileName.Contains(Path.VolumeSeparatorChar, StringComparison.Ordinal)
            && !fileName.Contains("..", StringComparison.Ordinal);

    /// <summary>Proves a declared file name is bare.</summary>
    /// <param name="fileName">The declared name.</param>
    /// <param name="parameterName">The parameter being checked.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentException">The name is blank, rooted, or escapes the package folder.</exception>
    public static string Required(string? fileName, string parameterName)
        => IsBare(fileName)
            ? fileName!
            : throw new ArgumentException(
                $"'{fileName}' is not a bare file name inside the package folder.",
                parameterName);
}
