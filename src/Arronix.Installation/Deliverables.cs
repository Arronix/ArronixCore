using System.IO;
using System.Linq;

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
}

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

    /// <summary>Gets the packages installed for a run, honoring the sample choice and any explicit set.</summary>
    /// <param name="includeSamples">Whether sample packages are installed.</param>
    /// <param name="only">When non-empty, the only package identifiers to install.</param>
    /// <returns>The selected packages, in installation order.</returns>
    public static IReadOnlyList<InstallablePackage> Select(
        bool includeSamples,
        IReadOnlyCollection<string> only)
    {
        var selected = Packages
            .Where(package => includeSamples || package.Role != PackageRole.Sample)
            .Where(package => only.Count == 0 || only.Contains(package.Id, StringComparer.Ordinal))
            .ToArray();

        var unknown = only
            .Except(Packages.Select(static package => package.Id), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return unknown.Length > 0
            ? throw new InstallationException(
                $"No package named {string.Join(", ", unknown)} is shipped by this repository. "
                + $"It installs: {string.Join(", ", Packages.Select(static package => package.Id))}.")
            : selected;
    }

    /// <summary>Gets the project file a project name refers to.</summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <param name="projectName">The project.</param>
    /// <returns>The absolute project file path.</returns>
    public static string ProjectFile(string repositoryRoot, string projectName)
        => Path.Combine(repositoryRoot, "src", projectName, projectName + ".csproj");
}
