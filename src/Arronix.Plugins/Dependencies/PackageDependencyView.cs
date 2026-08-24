using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Versioning;


namespace Arronix.Plugins.Dependencies;

/// <summary>
/// One published dependency edge, as an operator sees it.
/// </summary>
/// <remarks>
/// Identifiers, a declared range and a resolved version — and nothing else. Receipts, attempts, load
/// contexts and assemblies are Host lifecycle mechanics: an operator diagnosing why a package will not
/// stop needs to know which package still depends on it, not which object holds which reference.
/// </remarks>
/// <param name="Dependant">The package that declared the dependency.</param>
/// <param name="Dependency">The package it depends on.</param>
/// <param name="DeclaredRange">The version range the dependant declared.</param>
/// <param name="ResolvedVersion">The version of the dependency that satisfied it.</param>
internal sealed record PackageDependencyView(
    PluginId Dependant,
    PluginId Dependency,
    string DeclaredRange,
    SemanticVersion ResolvedVersion);
