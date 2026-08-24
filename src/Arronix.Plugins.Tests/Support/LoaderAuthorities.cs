using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Registry;

namespace Arronix.Plugins.Tests.Support;

/// <summary>
/// The three authorities every loader requires, built together so a test cannot compose half of them.
/// </summary>
internal sealed class LoaderAuthorities
{
    internal LoaderAuthorities(PluginPublicationGate publication)
    {
        Graph = new PackageDependencyResolver();
        Contracts = new SharedContractStore();
        Dependencies = new PackageDependencyRegistry(publication);
    }

    /// <summary>Gets the one production dependency resolver.</summary>
    internal IPackageGraphSource Graph { get; }

    /// <summary>Gets the installation's shared contract authority.</summary>
    internal SharedContractStore Contracts { get; }

    /// <summary>Gets which package attempts are rooted and pinned.</summary>
    internal PackageDependencyRegistry Dependencies { get; }
}
