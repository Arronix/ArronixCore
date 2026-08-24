using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Plugins.Manifest;
using Arronix.Abstractions.Plugins;

namespace Arronix.Plugins.Dependencies;

/// <summary>
/// One package the resolver refused before any of its code could run.
/// </summary>
/// <remarks>
/// Missing, incompatible, duplicated, cyclic and disabled packages are all decided from installed
/// declarations, so the verdict is terminal. The loader records it and never re-derives it.
/// </remarks>
internal sealed class PackageResolutionRefusal
{
    /// <summary>Initializes a new instance of the <see cref="PackageResolutionRefusal"/> class.</summary>
    /// <param name="package">The refused package.</param>
    /// <param name="errorCode">The failure class an operator sees.</param>
    /// <param name="reason">Why the package was refused, in one actionable sentence.</param>
    /// <param name="defects">Every individual fault, each naming the member at fault.</param>
    /// <param name="copies">
    /// Every installed copy of the identifier, in canonical order. A duplicated identifier has more than one.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public PackageResolutionRefusal(
        PluginId package,
        CoreErrorCode errorCode,
        string reason,
        IReadOnlyList<ManifestDefect>? defects = null,
        IReadOnlyList<InstalledPackage>? copies = null)
    {
        ArgumentNullException.ThrowIfNull(reason);

        Package = package;
        ErrorCode = errorCode;
        Reason = reason;
        Defects = (defects is null ? new List<ManifestDefect>() : [.. defects]).AsReadOnly();
        Copies = (copies is null ? new List<InstalledPackage>() : [.. copies]).AsReadOnly();
    }

    /// <summary>Gets the refused package.</summary>
    public PluginId Package { get; }

    /// <summary>Gets the failure class.</summary>
    public CoreErrorCode ErrorCode { get; }

    /// <summary>Gets why the package was refused.</summary>
    public string Reason { get; }

    /// <summary>Gets every individual fault, each naming the member to edit.</summary>
    public ReadOnlyCollection<ManifestDefect> Defects { get; }

    /// <summary>Gets every installed copy of the identifier, in canonical order.</summary>
    public ReadOnlyCollection<InstalledPackage> Copies { get; }
}

/// <summary>
/// The installation's one resolved package graph: what may be admitted, in what order, and what may not.
/// </summary>
/// <remarks>
/// <para>
/// Total over the packages it was built from: every identifier appears exactly once, in
/// <see cref="AdmissionOrder"/> or in <see cref="Refused"/>. The order is the ordinally smallest valid
/// topological order, so it is a property of the graph rather than of the walk that discovered the packages.
/// </para>
/// <para>
/// Acyclicity and requirement closure of the admissible set are preconditions rather than diagnostics: a
/// cycle among admitted packages is a defect in the resolver, so it throws here instead of quarantining an
/// extension for someone else's mistake.
/// </para>
/// </remarks>
internal sealed class ResolvedPackageGraph
{
    private readonly Dictionary<PluginId, InstalledPackage> _byId;
    private readonly Dictionary<PluginId, PackageResolutionRefusal> _refusedById;
    private readonly FrozenDictionary<PluginId, FrozenSet<PluginId>> _closures;
    private readonly FrozenDictionary<PluginId, FrozenSet<PluginId>> _dependants;

    /// <summary>Initializes a new instance of the <see cref="ResolvedPackageGraph"/> class.</summary>
    /// <param name="admissible">Every package the resolver is prepared to admit, in admission order.</param>
    /// <param name="refused">Every package the resolver refused, with its terminal diagnosis.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// An entry is <see langword="null"/> or duplicated, a package is both admissible and refused, a
    /// requirement names a package that is not admissible, or the admissible edges contain a cycle.
    /// </exception>
    public ResolvedPackageGraph(
        IReadOnlyList<InstalledPackage> admissible,
        IReadOnlyList<PackageResolutionRefusal>? refused = null)
    {
        ArgumentNullException.ThrowIfNull(admissible);

        _byId = [];
        _refusedById = [];

        foreach (var package in admissible)
        {
            if (package is null)
            {
                throw new ArgumentException(
                    "An admissible package collection must not contain null entries.",
                    nameof(admissible));
            }

            if (!_byId.TryAdd(package.Id, package))
            {
                throw new ArgumentException(
                    $"Package '{package.Id}' appears more than once in one resolved graph. A resolver that "
                    + "cannot choose between two copies refuses the identifier rather than admitting it twice.",
                    nameof(admissible));
            }
        }

        foreach (var refusal in refused ?? [])
        {
            if (refusal is null)
            {
                throw new ArgumentException(
                    "A refusal collection must not contain null entries.",
                    nameof(refused));
            }

            if (_byId.ContainsKey(refusal.Package))
            {
                throw new ArgumentException(
                    $"Package '{refusal.Package}' is both admissible and refused.",
                    nameof(refused));
            }

            if (!_refusedById.TryAdd(refusal.Package, refusal))
            {
                throw new ArgumentException(
                    $"Package '{refusal.Package}' is refused more than once.",
                    nameof(refused));
            }
        }

        foreach (var package in _byId.Values)
        {
            foreach (var requirement in package.Requirements)
            {
                if (!_byId.ContainsKey(requirement.PackageId))
                {
                    throw new ArgumentException(
                        $"Package '{package.Id}' requires '{requirement.PackageId}', which this graph does "
                        + "not admit. A dependant of an unresolvable dependency is refused by the resolver, "
                        + "never admitted with a dangling edge.",
                        nameof(admissible));
                }
            }
        }

        AdmissionOrder = SortTopologically(_byId);
        Refused = _refusedById.Values
            .OrderBy(refusal => refusal.Package, PackageIdentity.Order)
            .ToList()
            .AsReadOnly();
        _closures = BuildClosures(_byId);
        _dependants = BuildDependants(_byId, _closures);
    }

    /// <summary>Gets the graph of an installation with nothing installed.</summary>
    public static ResolvedPackageGraph Empty { get; } = new([]);

    /// <summary>
    /// Gets every admissible package in the order it must be admitted: a package always follows everything
    /// it requires, and ties are broken by ordinal identifier.
    /// </summary>
    public ReadOnlyCollection<InstalledPackage> AdmissionOrder { get; }

    /// <summary>Gets every refused package, ordered by identifier.</summary>
    public ReadOnlyCollection<PackageResolutionRefusal> Refused { get; }

    /// <summary>Finds one admissible package.</summary>
    /// <param name="package">The identifier.</param>
    /// <param name="resolved">The package when the graph admits it; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the graph admits it.</returns>
    public bool TryGet(PluginId package, [NotNullWhen(true)] out InstalledPackage? resolved)
        => _byId.TryGetValue(package, out resolved);

    /// <summary>Finds one terminal refusal.</summary>
    /// <param name="package">The identifier.</param>
    /// <param name="refusal">The refusal when the resolver refused it; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the resolver refused it.</returns>
    public bool TryGetRefusal(PluginId package, [NotNullWhen(true)] out PackageResolutionRefusal? refusal)
        => _refusedById.TryGetValue(package, out refusal);

    /// <summary>
    /// Gets everything a package requires, directly or transitively.
    /// </summary>
    /// <param name="package">The identifier.</param>
    /// <returns>The closure, excluding the package itself, or an empty set when the graph does not admit it.</returns>
    /// <remarks>
    /// This is the visibility rule for shared contracts. A package may bind to a contract published by
    /// itself or by a package in this set, and to nothing else the installation happens to have admitted.
    /// </remarks>
    public FrozenSet<PluginId> ClosureOf(PluginId package)
        => _closures.TryGetValue(package, out var closure) ? closure : FrozenSet<PluginId>.Empty;

    /// <summary>
    /// Gets every package that requires this one, directly or transitively.
    /// </summary>
    /// <param name="package">The identifier.</param>
    /// <returns>The dependants, excluding the package itself.</returns>
    /// <remarks>
    /// The declared package edge is the authority on what a refusal takes with it. A dependant that never
    /// mentions its dependency's assemblies in its own metadata is still a dependant, and admitting its
    /// contracts after its dependency was refused would leave inert bytes in the installation's context.
    /// </remarks>
    public FrozenSet<PluginId> DependantsOf(PluginId package)
        => _dependants.TryGetValue(package, out var dependants) ? dependants : FrozenSet<PluginId>.Empty;

    /// <summary>Produces the ordinally smallest topological order.</summary>
    private static ReadOnlyCollection<InstalledPackage> SortTopologically(
        Dictionary<PluginId, InstalledPackage> packages)
    {
        var remaining = new Dictionary<PluginId, int>(packages.Count);
        var dependants = new Dictionary<PluginId, List<PluginId>>(packages.Count);

        foreach (var package in packages.Values)
        {
            remaining[package.Id] = package.Requirements.Count;
            dependants[package.Id] = [];
        }

        foreach (var package in packages.Values)
        {
            foreach (var requirement in package.Requirements)
            {
                dependants[requirement.PackageId].Add(package.Id);
            }
        }

        var ready = new PriorityQueue<PluginId, PluginId>(PackageIdentity.Order);

        foreach (var (id, count) in remaining)
        {
            if (count == 0)
            {
                ready.Enqueue(id, id);
            }
        }

        var ordered = new List<InstalledPackage>(packages.Count);

        while (ready.TryDequeue(out var next, out _))
        {
            ordered.Add(packages[next]);

            foreach (var dependant in dependants[next])
            {
                if (--remaining[dependant] == 0)
                {
                    ready.Enqueue(dependant, dependant);
                }
            }
        }

        if (ordered.Count != packages.Count)
        {
            var cyclic = packages.Keys
                .Where(id => remaining[id] > 0)
                .Select(id => id.Value)
                .Order(StringComparer.Ordinal);

            throw new ArgumentException(
                $"The resolved graph contains a dependency cycle among [{string.Join(", ", cyclic)}]. A "
                + "cycle is diagnosed by the resolver and its members are refused; it never reaches admission.",
                nameof(packages));
        }

        return ordered.AsReadOnly();
    }

    /// <summary>Computes each package's exact transitive dependency closure.</summary>
    private static FrozenDictionary<PluginId, FrozenSet<PluginId>> BuildClosures(
        Dictionary<PluginId, InstalledPackage> packages)
    {
        var closures = new Dictionary<PluginId, FrozenSet<PluginId>>(packages.Count);

        foreach (var package in packages.Values)
        {
            var seen = new HashSet<PluginId>();
            var pending = new Queue<PluginId>();

            foreach (var requirement in package.Requirements)
            {
                if (seen.Add(requirement.PackageId))
                {
                    pending.Enqueue(requirement.PackageId);
                }
            }

            while (pending.Count > 0)
            {
                foreach (var requirement in packages[pending.Dequeue()].Requirements)
                {
                    if (seen.Add(requirement.PackageId))
                    {
                        pending.Enqueue(requirement.PackageId);
                    }
                }
            }

            closures[package.Id] = seen.ToFrozenSet();
        }

        return closures.ToFrozenDictionary();
    }

    /// <summary>Inverts the closure map so a refusal can find everything that requires the refused package.</summary>
    private static FrozenDictionary<PluginId, FrozenSet<PluginId>> BuildDependants(
        Dictionary<PluginId, InstalledPackage> packages,
        FrozenDictionary<PluginId, FrozenSet<PluginId>> closures)
    {
        var dependants = new Dictionary<PluginId, HashSet<PluginId>>(packages.Count);

        foreach (var id in packages.Keys)
        {
            dependants[id] = [];
        }

        foreach (var (dependant, closure) in closures)
        {
            foreach (var dependency in closure)
            {
                dependants[dependency].Add(dependant);
            }
        }

        return dependants.ToFrozenDictionary(
            entry => entry.Key,
            entry => entry.Value.ToFrozenSet());
    }
}

/// <summary>
/// The one authority on what the installed packages require of each other.
/// </summary>
/// <remarks>
/// Required infrastructure. A loader composed without a resolution authority does not fall back to an
/// assumed shape of the installation; it does not compose. Resolution runs once per load pass, from
/// validated declarations, before any load context exists.
/// </remarks>
internal interface IPackageGraphSource
{
    /// <summary>Resolves what the installation requires of itself.</summary>
    /// <param name="installed">Every package whose declaration was read and proved well-formed.</param>
    /// <returns>The resolved graph.</returns>
    ResolvedPackageGraph Resolve(IReadOnlyList<InstalledPackage> installed);
}
