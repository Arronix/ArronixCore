using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Plugins.Manifest;
using Arronix.Abstractions.Plugins;

namespace Arronix.Plugins.Dependencies;

/// <summary>
/// The installation's one production dependency resolver.
/// </summary>
/// <remarks>
/// <para>
/// It runs <see cref="PackageDependencyEngine"/> over the installed packages and turns its diagnostics into
/// the failure classes and member paths an operator acts on. Identity, availability and dependencies are
/// decided together, once, before any assembly is opened.
/// </para>
/// <para>
/// There is no edgeless or default resolution. Once a package can declare a dependency, an installation
/// with no resolved graph is a composition fault rather than an installation of independent packages.
/// </para>
/// </remarks>
internal sealed class PackageDependencyResolver : IPackageGraphSource
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="installed"/> is <see langword="null"/>.</exception>
    public ResolvedPackageGraph Resolve(IReadOnlyList<InstalledPackage> installed)
    {
        ArgumentNullException.ThrowIfNull(installed);

        if (installed.Any(static package => package is null))
        {
            throw new ArgumentException(
                "The installed packages must not contain a null entry.",
                nameof(installed));
        }

        var copies = installed
            .GroupBy(package => package.Id)
            .ToDictionary(group => group.Key, Canonical);

        PackageDependencyEngine.Resolve(
            installed,
            out var activationOrder,
            out var ineligible,
            out var diagnostics);

        var refusals = ineligible
            .Select(package => Refuse(package, copies[package], diagnostics))
            .ToArray();

        return new ResolvedPackageGraph(activationOrder, refusals);
    }

    /// <summary>
    /// Puts one identifier's installed copies in the order everything observable about them is written.
    /// </summary>
    /// <remarks>
    /// The second key is the exact text a duplicate diagnostic lists, so the order copies are recorded in
    /// and the order the message names them in cannot disagree; the declaration path separates two copies
    /// that render identically, which is the last thing about them a caller can observe.
    /// </remarks>
    private static IReadOnlyList<InstalledPackage> Canonical(IEnumerable<InstalledPackage> copies)
        =>
        [
            .. copies
                .OrderBy(package => package.Version)
                .ThenBy(package => package.Described, StringComparer.Ordinal)
                .ThenBy(package => package.Source, StringComparer.Ordinal)
        ];

    private static PackageResolutionRefusal Refuse(
        PluginId package,
        IReadOnlyList<InstalledPackage> copies,
        IReadOnlyList<PackageDependencyDiagnostic> diagnostics)
    {
        var own = diagnostics.Where(diagnostic => diagnostic.Package == package).ToArray();

        // The declaration a defect points at is the one this package wrote, which a duplicated identifier
        // does not have exactly one of. Its own diagnostics never carry a dependency, so the lookup is
        // never consulted for one.
        var declared = copies.Count == 1
            ? (IReadOnlyList<PackageRequirement>)copies[0].Requirements
            : [];

        var availability = copies.Count == 1 ? copies[0].Availability : PackageAvailability.Available;
        var (code, message) = Summarize(package, availability, own);

        // The unavailability diagnostic is the summary, said once. Repeating it as a defect would tell an
        // operator who switched a package off that it is switched off, under a member path they cannot edit
        // to change it. Genuine declaration faults stay listed: disabling a package does not repair them.
        return new PackageResolutionRefusal(
            package,
            code,
            message,
            [
                .. own
                    .Where(diagnostic => diagnostic.Kind != PackageDependencyDiagnosticKind.UnavailablePackage)
                    .Select(diagnostic => Describe(diagnostic, declared))
            ],
            copies);
    }

    /// <summary>
    /// Chooses the one failure class and summary an operator is shown for a package with several faults.
    /// </summary>
    /// <remarks>
    /// A precedence rather than a search: what the package did wrong itself comes before what it inherited.
    /// A duplicated identifier keeps the failure class the loader has always reported for it, and outranks
    /// being disabled because an installation with two copies of one identifier has a problem the operator
    /// must fix whichever copy they meant to switch off.
    /// </remarks>
    private static (CoreErrorCode Code, string Message) Summarize(
        PluginId package,
        PackageAvailability availability,
        IReadOnlyList<PackageDependencyDiagnostic> own)
    {
        if (own.Any(diagnostic => diagnostic.Kind == PackageDependencyDiagnosticKind.DuplicatePackage))
        {
            return (
                CoreErrorCode.PluginIdConflict,
                $"More than one installed extension claims the identifier '{package}'. Identity must be unique across an installation.");
        }

        // Switched on the typed state, not on the text of a diagnostic. A refusal class is a decision about
        // what an operator is told; reading a message back to decide it would make the message the contract.
        if (availability == PackageAvailability.DisabledByConfiguration)
        {
            return (
                CoreErrorCode.PluginDisabled,
                $"Extension '{package}' is installed but disabled by configuration.");
        }

        if (own.Any(diagnostic => diagnostic.Kind
                is PackageDependencyDiagnosticKind.MissingDependency
                or PackageDependencyDiagnosticKind.IncompatibleDependency
                or PackageDependencyDiagnosticKind.DuplicateRequirement))
        {
            return (
                CoreErrorCode.PluginDependencyUnsatisfied,
                $"The package dependencies extension '{package}' declares cannot be satisfied by this installation.");
        }

        if (own.Any(diagnostic => diagnostic.Kind == PackageDependencyDiagnosticKind.DependencyCycle))
        {
            return (
                CoreErrorCode.PluginDependencyCycle,
                $"Extension '{package}' lies on a package dependency cycle.");
        }

        return (
            CoreErrorCode.PluginDependencyUnavailable,
            $"Extension '{package}' requires a package which cannot itself be activated.");
    }

    /// <remarks>
    /// The member path is the declared entry that carried the requirement, so the fix is the line the
    /// operator is pointed at. A fault about the package as a whole has no single entry to name.
    /// </remarks>
    private static ManifestDefect Describe(
        PackageDependencyDiagnostic diagnostic,
        IReadOnlyList<PackageRequirement> declared)
    {
        var code = diagnostic.Kind switch
        {
            PackageDependencyDiagnosticKind.DuplicatePackage => CoreErrorCode.PluginIdConflict,
            PackageDependencyDiagnosticKind.DependencyCycle => CoreErrorCode.PluginDependencyCycle,
            PackageDependencyDiagnosticKind.IneligibleDependency => CoreErrorCode.PluginDependencyUnavailable,
            _ => CoreErrorCode.PluginDependencyUnsatisfied
        };

        if (diagnostic.Kind == PackageDependencyDiagnosticKind.DuplicatePackage)
        {
            return new ManifestDefect("id", diagnostic.Message, code);
        }

        if (diagnostic.Dependency is not { } dependency)
        {
            return new ManifestDefect("dependencies", diagnostic.Message, code);
        }

        var index = IndexOf(declared, dependency);
        var member = diagnostic.Kind switch
        {
            PackageDependencyDiagnosticKind.IncompatibleDependency => "range",
            PackageDependencyDiagnosticKind.MissingDependency => "package",
            _ => null
        };

        var path = index < 0
            ? "dependencies"
            : member is null ? $"dependencies[{index}]" : $"dependencies[{index}].{member}";

        return new ManifestDefect(path, diagnostic.Message, code);
    }

    private static int IndexOf(IReadOnlyList<PackageRequirement> declared, PluginId dependency)
    {
        for (var index = 0; index < declared.Count; index++)
        {
            if (declared[index].PackageId == dependency)
            {
                return index;
            }
        }

        return -1;
    }
}
