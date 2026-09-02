using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Arronix.Installation;

/// <summary>One package this repository ships into an installation.</summary>
/// <param name="Id">The package identifier, exactly as its own manifest declares it.</param>
/// <param name="ProjectName">The project that publishes the package payload.</param>
/// <param name="Role">What an evaluator should understand the package to be.</param>
internal sealed record InstallablePackage(string Id, string ProjectName, PackageRole Role);

/// <summary>What a package is, for the one sentence printed beside it.</summary>
internal enum PackageRole
{
    /// <summary>An ordinary product package.</summary>
    Product,

    /// <summary>A product package that cannot answer until an operator supplies credentials.</summary>
    NeedsCredentials,

    /// <summary>A package shipped so the product can be evaluated without an account anywhere.</summary>
    Sample,

    /// <summary>
    /// A package named on the command line by its own project file rather than declared here. Never one of
    /// this repository's deliverables; used by a proof or fixture that needs a real composed installation
    /// beside a package this repository does not ship.
    /// </summary>
    Fixture,
}

/// <summary>
/// A package this run installs, resolved to the exact project file that publishes it.
/// </summary>
/// <param name="Id">The package identifier its own manifest declares.</param>
/// <param name="ProjectFile">The project file that publishes it.</param>
/// <param name="Role">What an evaluator should understand it to be.</param>
internal sealed record PackageSource(string Id, string ProjectFile, PackageRole Role);

/// <summary>
/// What an installation is made of.
/// </summary>
/// <remarks>
/// <para>
/// Declared, not discovered. Several projects in this repository carry a <c>plugin.json</c> and are
/// nonetheless not deliverables: three are loader fixtures owned by test suites, and three are media
/// extensions still on the legacy imperative seams that the typed migration has not reached. A composer
/// that globbed manifests would install all six and present test infrastructure as product, which is
/// exactly the confusion this route exists to end. The set is therefore a list with an owner, and an
/// architecture rule holds it to naming only projects that exist and ship a manifest.
/// </para>
/// <para>
/// The order is the installation order and is not arbitrary: a shared contract package is installed before
/// the packages that require it, so a partially written installation is never one where a dependant is
/// present and its contract is not.
/// </para>
/// <para>
/// A package's dependencies are read from its own <c>plugin.json</c> rather than restated here, because a
/// second hand-maintained dependency graph could disagree with the one the manifest declares and nothing
/// would notice. Selecting a package therefore selects its whole dependency closure: asking for
/// <c>movies</c> alone still installs the video package it requires, because an installation that had
/// forgotten it would be one no admission path could actually load.
/// </para>
/// </remarks>
internal static class Deliverables
{
    /// <summary>The project publishing the server.</summary>
    public const string ServerProject = "Arronix.Api";

    /// <summary>The entry assembly of the published server.</summary>
    public const string ServerEntryAssembly = "Arronix.Api.dll";

    /// <summary>The project publishing the browser client.</summary>
    public const string ClientProject = "Arronix.Client";

    /// <summary>The manifest file every installed package carries.</summary>
    public const string PackageManifestFileName = "plugin.json";

    /// <summary>Every package this repository installs, in installation order.</summary>
    public static IReadOnlyList<InstallablePackage> Packages { get; } =
    [
        new("arronix.format.video", "Arronix.Format.Video", PackageRole.Product),
        new("languages.reference", "Arronix.Language.Reference", PackageRole.Product),
        new("movies", "Arronix.Plugin.Movies", PackageRole.Product),
        new("tmdb", "Arronix.Provider.Tmdb", PackageRole.NeedsCredentials),
        new("sample.movie.catalog", "Arronix.Sample.MovieCatalog", PackageRole.Sample),
    ];

    /// <summary>
    /// Gets the packages installed for a run, honoring the sample choice and any explicit set, closed over
    /// every declared dependency.
    /// </summary>
    /// <param name="repositoryRoot">The repository root, used to read each candidate's own manifest.</param>
    /// <param name="includeSamples">Whether sample packages are installed.</param>
    /// <param name="only">When non-empty, the only package identifiers to install before closure.</param>
    /// <returns>The selected packages and their required dependencies, in installation order.</returns>
    /// <exception cref="InstallationException">
    /// <paramref name="only"/> names a package this repository does not ship, or a selected package declares
    /// a dependency this repository does not ship as an installable package.
    /// </exception>
    public static IReadOnlyList<InstallablePackage> Select(
        string repositoryRoot,
        bool includeSamples,
        IReadOnlyCollection<string> only)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(only);

        var unknown = only
            .Except(Packages.Select(static package => package.Id), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (unknown.Length > 0)
        {
            throw new InstallationException(
                $"No package named {string.Join(", ", unknown)} is shipped by this repository. "
                + $"It installs: {string.Join(", ", Packages.Select(static package => package.Id))}.");
        }

        var requested = Packages
            .Where(package => includeSamples || package.Role != PackageRole.Sample)
            .Where(package => only.Count == 0 || only.Contains(package.Id, StringComparer.Ordinal))
            .Select(static package => package.Id);

        var closed = CloseDependencies(repositoryRoot, requested);

        return Packages.Where(package => closed.Contains(package.Id)).ToArray();
    }

    /// <summary>Gets the project file a project name refers to.</summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <param name="projectName">The project.</param>
    /// <returns>The absolute project file path.</returns>
    public static string ProjectFile(string repositoryRoot, string projectName)
        => Path.Combine(repositoryRoot, "src", projectName, projectName + ".csproj");

    private static IReadOnlySet<string> CloseDependencies(string repositoryRoot, IEnumerable<string> seedIds)
    {
        var closure = new HashSet<string>(seedIds, StringComparer.Ordinal);
        var pending = new Queue<string>(closure);

        while (pending.TryDequeue(out var id))
        {
            var package = Packages.FirstOrDefault(candidate => candidate.Id == id);

            if (package is null)
            {
                continue;
            }

            foreach (var dependency in DeclaredDependencyIds(repositoryRoot, package))
            {
                if (Packages.All(candidate => candidate.Id != dependency))
                {
                    throw new InstallationException(
                        $"'{package.ProjectName}' declares a dependency on '{dependency}', which this "
                        + "repository does not ship as an installable package.");
                }

                if (closure.Add(dependency))
                {
                    pending.Enqueue(dependency);
                }
            }
        }

        return closure;
    }

    private static IEnumerable<string> DeclaredDependencyIds(string repositoryRoot, InstallablePackage package)
    {
        var manifestFile = Path.Combine(
            Path.GetDirectoryName(ProjectFile(repositoryRoot, package.ProjectName))
                ?? throw new InstallationException($"'{package.ProjectName}' has no project directory."),
            PackageManifestFileName);

        if (!File.Exists(manifestFile))
        {
            yield break;
        }

        JsonNode? declared;

        try
        {
            declared = JsonNode.Parse(File.ReadAllText(manifestFile));
        }
        catch (JsonException error)
        {
            throw new InstallationException($"'{manifestFile}' is not readable JSON.", error);
        }

        if (declared?["dependencies"] is not JsonArray dependencies)
        {
            yield break;
        }

        foreach (var dependency in dependencies)
        {
            if ((string?)dependency?["package"] is { Length: > 0 } id)
            {
                yield return id;
            }
        }
    }
}
