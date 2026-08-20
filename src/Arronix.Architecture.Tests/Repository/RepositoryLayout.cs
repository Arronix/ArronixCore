using System.IO;
using System.Linq;

namespace Arronix.Architecture.Tests.Repository;

/// <summary>
/// Locates the repository and the projects inside it.
/// </summary>
/// <remarks>
/// <para>
/// Everything here reads the working tree rather than the build output. That is deliberate: a governance
/// rule that can only be checked against a compiled assembly stops being checked exactly when a project
/// stops compiling, which is the moment it is most likely to be broken. Reading the repository also lets
/// one fixture speak about projects it must never reference - the browser client and the HTTP server are
/// held to the same discipline as an extension, so this project cannot reference either of them.
/// </para>
/// <para>
/// The root is found by walking up from the test binary until the solution file appears, the same way
/// <c>Arronix.Common.Tests</c> finds it. No environment variable, no relative path baked into a constant.
/// </para>
/// </remarks>
internal static class RepositoryLayout
{
    /// <summary>The file that marks the repository root.</summary>
    public const string SolutionFileName = "Arronix.sln";

    /// <summary>The contract assembly's project name.</summary>
    public const string Abstractions = "Arronix.Abstractions";

    /// <summary>The platform library's project name.</summary>
    public const string Common = "Arronix.Common";

    /// <summary>The extension loader's project name.</summary>
    public const string Plugins = "Arronix.Plugins";

    /// <summary>The runtime's project name.</summary>
    public const string Host = "Arronix.Host";

    /// <summary>The HTTP surface's project name.</summary>
    public const string Api = "Arronix.Api";

    /// <summary>The browser client's project name.</summary>
    public const string Client = "Arronix.Client";

    /// <summary>The typed video representation assembly.</summary>
    public const string VideoFormat = "Arronix.Format.Video";

    /// <summary>The reference language implementation assembly.</summary>
    public const string ReferenceLanguages = "Arronix.Language.Reference";

    /// <summary>The compile-time media-shape generator.</summary>
    public const string Generators = "Arronix.Generators";

    /// <summary>The prefix every media extension project shares.</summary>
    public const string ExtensionPrefix = "Arronix.Plugin.";

    private static readonly string[] ExcludedDirectorySegments = ["obj", "bin"];

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// Gets the repository root.
    /// </summary>
    public static string Root => RepositoryRoot;

    /// <summary>
    /// Gets the folder every project in this delivery lives under.
    /// </summary>
    public static string SourceRoot => Path.Combine(RepositoryRoot, "src");

    /// <summary>
    /// Gets the six assemblies invariant 1 applies to: everything that is not a media extension.
    /// </summary>
    /// <remarks>
    /// Listed rather than globbed. A media-neutrality rule that discovered its own subjects would stop
    /// covering a project the day someone renamed it, and would report success while checking nothing.
    /// </remarks>
    public static IReadOnlyList<string> MediaNeutralProjects { get; } =
    [
        Abstractions,
        Common,
        Plugins,
        Host,
        Api,
        Client
    ];

    /// <summary>
    /// Gets every media extension project, discovered from the working tree.
    /// </summary>
    /// <remarks>
    /// Globbed rather than listed, because the rule is about the shape of the category: a fifth media
    /// extension added tomorrow must be governed on the day it appears, without anyone remembering to
    /// add it here. Test projects are excluded - they are consumers of an extension, not extensions.
    /// </remarks>
    public static IReadOnlyList<string> MediaExtensionProjects { get; } = DiscoverMediaExtensions();

    /// <summary>
    /// Gets every project this delivery owns, test projects included.
    /// </summary>
    /// <remarks>
    /// Globbed, and deliberately wider than <see cref="MediaNeutralProjects"/>. The rules that speak about
    /// architecture are about the shipped assemblies; the spelling rule is about the whole delivery,
    /// because a test method name is read by the same people as a contract member name.
    /// </remarks>
    public static IReadOnlyList<string> AllProjects { get; } = DiscoverAllProjects();

    /// <summary>
    /// Gets the folder a project lives in.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <returns>The absolute folder path.</returns>
    public static string ProjectDirectory(string projectName) => Path.Combine(SourceRoot, projectName);

    /// <summary>
    /// Gets the path of a project file.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <returns>The absolute project file path.</returns>
    public static string ProjectFilePath(string projectName)
        => Path.Combine(ProjectDirectory(projectName), projectName + ".csproj");

    /// <summary>
    /// Determines whether a project exists in the working tree.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <returns><see langword="true"/> when its project file is present.</returns>
    public static bool ProjectExists(string projectName) => File.Exists(ProjectFilePath(projectName));

    /// <summary>
    /// Lists the files of a project that match a pattern, ignoring build intermediates.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <param name="searchPattern">The pattern, for example <c>*.cs</c>.</param>
    /// <returns>The absolute file paths, in a stable order.</returns>
    public static IReadOnlyList<string> Files(string projectName, string searchPattern)
    {
        var directory = ProjectDirectory(projectName);

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories)
            .Where(static path => !IsBuildIntermediate(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Renders a path relative to the repository root, so a failure message is readable.
    /// </summary>
    /// <param name="absolutePath">The path.</param>
    /// <returns>The repository-relative path.</returns>
    public static string Relative(string absolutePath)
        => Path.GetRelativePath(RepositoryRoot, absolutePath).Replace('\\', '/');

    private static bool IsBuildIntermediate(string path)
    {
        var relative = Path.GetRelativePath(SourceRoot, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(segment => ExcludedDirectorySegments.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> DiscoverAllProjects()
    {
        if (!Directory.Exists(SourceRoot))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(SourceRoot, "Arronix.*")
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Where(ProjectExists)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> DiscoverMediaExtensions()
    {
        if (!Directory.Exists(SourceRoot))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(SourceRoot, ExtensionPrefix + "*")
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Select(static name => name!)
            .Where(static name => !name.EndsWith(".Tests", StringComparison.Ordinal))
            .Where(ProjectExists)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                $"Could not locate '{SolutionFileName}' above '{AppContext.BaseDirectory}'.");
    }
}
