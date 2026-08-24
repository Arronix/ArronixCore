using System.Collections.ObjectModel;
using Arronix.Abstractions.Plugins;

namespace Arronix.Plugins.Dependencies;

/// <summary>
/// What is wrong with one package's place in the dependency graph.
/// </summary>
/// <remarks>
/// A closed set. Every member names a state an operator can act on without knowing how the resolver works.
/// </remarks>
internal enum PackageDependencyDiagnosticKind
{
    /// <summary>Two or more installed packages carry the same identifier.</summary>
    DuplicatePackage = 0,

    /// <summary>One package states a requirement on the same identifier more than once.</summary>
    DuplicateRequirement = 1,

    /// <summary>A required package identifier is not installed at all.</summary>
    MissingDependency = 2,

    /// <summary>A required package is installed at a version the range does not admit.</summary>
    IncompatibleDependency = 3,

    /// <summary>The package lies on a dependency cycle.</summary>
    DependencyCycle = 4,

    /// <summary>The package depends on a package that is itself ineligible.</summary>
    IneligibleDependency = 5,

    /// <summary>The package is installed and its typed availability state refuses activation.</summary>
    UnavailablePackage = 6
}

/// <summary>
/// One reason one package cannot be activated, in terms an operator can act on.
/// </summary>
/// <remarks>
/// Every ineligible package carries at least one diagnostic naming it, so the complete explanation for a
/// package is the diagnostics whose <see cref="Package"/> is that package. <see cref="Message"/> is complete
/// on its own and is never composed with a caller-supplied prefix.
/// </remarks>
internal sealed class PackageDependencyDiagnostic
{
    internal PackageDependencyDiagnostic(
        PackageDependencyDiagnosticKind kind,
        PluginId package,
        PluginId? dependency,
        string message,
        IReadOnlyList<PluginId>? cyclePath = null)
    {
        Kind = kind;
        Package = package;
        Dependency = dependency;
        Message = message;
        CyclePath = (cyclePath is null ? new List<PluginId>() : [.. cyclePath]).AsReadOnly();
    }

    /// <summary>Gets what is wrong.</summary>
    public PackageDependencyDiagnosticKind Kind { get; }

    /// <summary>Gets the package the diagnostic is about.</summary>
    public PluginId Package { get; }

    /// <summary>Gets the dependency at fault, or <see langword="null"/> when the fault is not about one edge.</summary>
    public PluginId? Dependency { get; }

    /// <summary>Gets the complete, actionable explanation.</summary>
    public string Message { get; }

    /// <summary>
    /// Gets an actual cycle through <see cref="Package"/>, starting and ending at it, or an empty list.
    /// </summary>
    /// <remarks>A walk over real edges: a set of participants would leave the reader to find the edge to cut.</remarks>
    public ReadOnlyCollection<PluginId> CyclePath { get; }

    /// <inheritdoc />
    public override string ToString() => Message;
}
