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

    /// <summary>The video format domain: the shared representation and quality facts a release carries.</summary>
    public const string VideoFormat = "Arronix.Format.Video";

    /// <summary>The isolated half of the video package: recognition vocabulary, family data, policy defaults.</summary>
    public const string VideoFormatContributions = "Arronix.Format.Video.Contributions";

    /// <summary>The movies media domain: the item type a separately shipped provider pairs with.</summary>
    public const string MoviesDomain = "Arronix.Media.Movies";

    /// <summary>The isolated entry assembly of the movies package.</summary>
    public const string MoviesExtension = "Arronix.Plugin.Movies";

    /// <summary>The stand-in for a separately shipped movie provider package.</summary>
    public const string MovieCatalogerFixture = "Arronix.Architecture.Tests.MovieCatalogerFixture";

    /// <summary>The reference language implementation assembly.</summary>
    public const string ReferenceLanguages = "Arronix.Language.Reference";

    /// <summary>The compile-time media-shape generator.</summary>
    public const string Generators = "Arronix.Generators";

    /// <summary>The prefix every media extension project shares.</summary>
    public const string ExtensionPrefix = "Arronix.Plugin.";

    /// <summary>The prefix a media domain assembly shares, one per media kind that publishes one.</summary>
    public const string MediaDomainPrefix = "Arronix.Media.";

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
    /// Gets the assemblies a package may ship for sharing: one per capability that publishes a contract.
    /// </summary>
    /// <remarks>
    /// Listed rather than globbed, and the two are deliberately unalike. A shared contract assembly binds
    /// its release cadence to every dependant of its package, so publishing one is a deliberate act with an
    /// owner behind it. Discovering them by a name pattern would let a project take on that cadence by
    /// being named a certain way, which is the opposite of the rule.
    /// </remarks>
    public static IReadOnlyList<string> SharedContractProjects { get; } =
    [
        MoviesDomain,
        VideoFormat
    ];

    /// <summary>
    /// Gets the media domain assembly a media extension publishes, when it publishes one.
    /// </summary>
    /// <param name="extensionProject">The media extension project, for example <c>Arronix.Plugin.Movies</c>.</param>
    /// <returns>The domain project name, or <see langword="null"/> when that kind publishes none.</returns>
    /// <remarks>
    /// The relationship is by convention rather than by declaration, and only for reading: the rule that
    /// uses it says an extension may reference <i>its own</i> media domain, not any media domain, so it
    /// needs to know which one is its own. Books, Music and Television publish none yet - their item types
    /// are still legacy shapes - and a null result means the extension may reference no media domain at all.
    /// </remarks>
    public static string? MediaDomainOf(string extensionProject)
    {
        if (!extensionProject.StartsWith(ExtensionPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var candidate = MediaDomainPrefix + extensionProject[ExtensionPrefix.Length..];

        return SharedContractProjects.Contains(candidate, StringComparer.Ordinal) ? candidate : null;
    }

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
    /// Locates one file in a project's build output.
    /// </summary>
    /// <param name="projectName">The project.</param>
    /// <param name="fileName">The bare file name.</param>
    /// <returns>The absolute path, or <see langword="null"/> when the project has not been built.</returns>
    public static string? BuildOutputFile(string projectName, string fileName)
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        var framework = here.Name;
        var configuration = here.Parent?.Name;

        if (configuration is null || !string.Equals(here.Parent?.Parent?.Name, "bin", StringComparison.Ordinal))
        {
            return null;
        }

        return Path.Combine(ProjectDirectory(projectName), "bin", configuration, framework, fileName);
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
