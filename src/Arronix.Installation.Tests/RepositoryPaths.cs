using System;
using System.IO;

namespace Arronix.Installation.Tests;

/// <summary>Finds the real repository root a test runs from, the same way the tool itself does.</summary>
internal static class RepositoryPaths
{
    /// <summary>Gets this repository's root directory.</summary>
    public static string Root { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arronix.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find 'Arronix.sln' above '{AppContext.BaseDirectory}'.");
    }
}
