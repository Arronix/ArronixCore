using System.IO;

namespace Arronix.Installation;

/// <summary>
/// Resolves the physical path a nominal path names, following symbolic links and reparse points.
/// </summary>
/// <remarks>
/// <see cref="Path.GetFullPath(string)"/> normalizes separators and <c>..</c> segments; it never asks the
/// file system whether a directory it names is actually a link somewhere else. That gap matters here because
/// this tool decides whether a path is safe to delete from the same string it was given. An installation root
/// or any of its ancestors being a symbolic link to some other real location is exactly the shape of escape
/// this tool must refuse rather than silently follow.
/// </remarks>
internal static class RealPath
{
    /// <summary>
    /// Resolves the physical path an existing path names, one path segment at a time.
    /// </summary>
    /// <param name="path">The path to resolve. Need not exist.</param>
    /// <returns>
    /// The physical path: every existing prefix has had its symbolic links and reparse points followed. A
    /// trailing portion that does not yet exist on disk is appended literally, because it cannot itself be a
    /// link.
    /// </returns>
    /// <exception cref="InstallationException">A symbolic-link chain does not terminate.</exception>
    public static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var segments = full[root.Length..].Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);

        var resolved = string.IsNullOrEmpty(root) ? full : root;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var segment in segments)
        {
            var candidate = Path.Combine(resolved, segment);
            resolved = ResolveOneLevel(candidate, visited);
        }

        return Path.TrimEndingDirectorySeparator(resolved);
    }

    private static string ResolveOneLevel(string candidate, HashSet<string> visited)
    {
        var current = candidate;

        while (true)
        {
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);

            if (!info.Exists || info.LinkTarget is null)
            {
                return current;
            }

            if (!visited.Add(current))
            {
                throw new InstallationException($"'{candidate}' is part of a circular symbolic link.");
            }

            current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? throw new InstallationException(
                    $"'{current}' is a symbolic link whose target could not be resolved.");
        }
    }
}
