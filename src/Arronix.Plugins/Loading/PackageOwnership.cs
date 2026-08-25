using System.Collections.Frozen;
using System.Linq;
using System.Reflection;
using Arronix.Plugins.Dependencies;

namespace Arronix.Plugins.Loading;

/// <summary>
/// The exact set of assemblies one package owns: its entry assembly and the contracts it publishes.
/// </summary>
/// <remarks>
/// <para>
/// Ownership is enumerated, never inferred. A package's load context also holds the private assemblies it
/// shipped alongside its entry module, and its contract scope also makes its dependencies' contracts
/// visible; neither is a thing the package owns. Subscription, publication and delivery all ask this one
/// object, so a type is the package's own at all three boundaries or at none.
/// </para>
/// <para>
/// One immutable object per load attempt, held by the package lifetime and released with it.
/// </para>
/// </remarks>
internal sealed class PackageOwnership
{
    private readonly FrozenSet<Assembly> _owned;

    /// <summary>Initializes a new instance of the <see cref="PackageOwnership"/> class.</summary>
    /// <param name="entry">The loaded entry assembly.</param>
    /// <param name="publishedContracts">The contract assemblies this package publishes.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal PackageOwnership(Assembly entry, IEnumerable<Assembly> publishedContracts)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(publishedContracts);

        _owned = new[] { entry }.Concat(publishedContracts).ToFrozenSet();
    }

    /// <summary>Gets the assemblies this package owns.</summary>
    internal IReadOnlyCollection<Assembly> Assemblies => _owned;

    /// <summary>Reads a package's ownership off its lifetime, once its entry assembly is loaded.</summary>
    /// <param name="entry">The loaded entry assembly.</param>
    /// <param name="lease">The package lifetime, whose scope names the contracts this package published.</param>
    /// <returns>The ownership authority for this load attempt.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal static PackageOwnership Of(Assembly entry, PackageAdmissionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        return new PackageOwnership(entry, lease.Contracts.Published.Select(contract => contract.Assembly));
    }

    /// <summary>Determines whether this package owns an assembly.</summary>
    /// <param name="assembly">The assembly a type is declared in.</param>
    /// <returns><see langword="true"/> when the package owns it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is <see langword="null"/>.</exception>
    internal bool Owns(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return _owned.Contains(assembly);
    }

    /// <summary>Determines whether this package owns the assembly a type is declared in.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> when the package owns it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    internal bool Owns(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Owns(type.Assembly);
    }
}
