using System.IO;

namespace Arronix.Installation;

/// <summary>
/// Refuses an installation root before anything is composed, run or reset against it.
/// </summary>
/// <remarks>
/// <para>
/// A supplied <c>--root</c> is never self-authorizing. Composing creates and clears folders beneath the root
/// it is given, and reset removes them outright, so a root that resolves to the filesystem itself, an
/// operator's home directory, this repository, or a symbolic link away from where it appears to be would turn
/// an ordinary run into data loss somewhere nobody intended. This guard runs before every command, not only
/// <c>reset</c>, because composing already deletes and recreates whole subtrees under the root it is given.
/// </para>
/// <para>
/// This is a coarse, defense-in-depth refusal. It does not attempt to enumerate every sensitive directory on
/// every operating system; the load-bearing protection for <c>reset</c> specifically is the ownership check in
/// <see cref="InstallationManifest"/>, which requires the exact target to already carry a valid installation
/// this tool wrote. This guard exists for the cases that check cannot reach: a root that is dangerous before
/// anything has ever been installed there at all.
/// </para>
/// </remarks>
internal static class InstallationRootGuard
{
    /// <summary>
    /// Refuses a root that is unsafe for any command to touch.
    /// </summary>
    /// <param name="root">The installation root, already resolved to a full path.</param>
    /// <param name="repositoryRoot">This repository's root.</param>
    /// <exception cref="InstallationException">The root is unsafe.</exception>
    public static void EnsureSafe(string root, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        var normalizedRepository = Path.TrimEndingDirectorySeparator(repositoryRoot);

        if (PathEquals(normalizedRoot, normalizedRepository)
            || IsAncestor(normalizedRoot, normalizedRepository))
        {
            throw new InstallationException(
                $"'{root}' is this repository, or a directory above it. Name an installation directory "
                + "that is not this checkout, so a compose or reset here can never touch its source.");
        }

        if (IsAncestor(normalizedRepository, normalizedRoot))
        {
            // Somewhere inside the checkout. The one place that is safe is the ignored artifacts scratch
            // area — the default root lives there — because nothing tracked by source control is ever
            // supposed to be there. Anywhere else inside the checkout, such as a project's own source
            // folder, is refused: a compose or reset there would create or remove folders named server,
            // client, packages, state and installation.json beside real tracked source.
            var artifactsRoot = Path.Combine(normalizedRepository, "artifacts");

            if (!PathEquals(normalizedRoot, artifactsRoot) && !IsAncestor(artifactsRoot, normalizedRoot))
            {
                throw new InstallationException(
                    $"'{root}' is inside this repository's source tree. An installation directory must be "
                    + $"outside the checkout, or under its ignored '{artifactsRoot}' scratch area — for "
                    + $"example the default {CommandLine.DefaultRoot}.");
            }
        }

        foreach (var sensitive in SensitiveRoots())
        {
            if (PathEquals(normalizedRoot, sensitive))
            {
                throw new InstallationException(
                    $"'{root}' is '{sensitive}', which this tool refuses to treat as an installation "
                    + "directory. Name a directory dedicated to one Arronix installation.");
            }
        }

        if (Directory.Exists(normalizedRoot) || File.Exists(normalizedRoot))
        {
            var physical = RealPath.Resolve(normalizedRoot);

            if (!PathEquals(physical, normalizedRoot))
            {
                throw new InstallationException(
                    $"'{root}' reaches '{physical}' through a symbolic link or reparse point. This tool "
                    + "refuses to compose or reset an installation through one, because the path an operator "
                    + "read and the path an operation would actually touch could then differ.");
            }
        }
    }

    private static IEnumerable<string> SensitiveRoots()
    {
        yield return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

        foreach (var drive in DriveInfo.GetDrives())
        {
            yield return Path.TrimEndingDirectorySeparator(drive.RootDirectory.FullName);
        }

        if (OperatingSystem.IsWindows())
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        }
        else
        {
            foreach (var unix in new[]
                     {
                         "/etc", "/usr", "/bin", "/sbin", "/var", "/opt", "/System", "/Library",
                         "/private", "/Applications", "/home", "/root", "/boot", "/dev", "/proc", "/sys",
                     })
            {
                yield return unix;
            }
        }
    }

    private static bool IsAncestor(string candidateAncestor, string path)
        => path.StartsWith(candidateAncestor + Path.DirectorySeparatorChar, PathComparison());

    private static bool PathEquals(string left, string right)
        => string.Equals(left, right, PathComparison());

    private static StringComparison PathComparison()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
