using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Plugins.Loading;

namespace Arronix.Plugins.Registry;

/// <summary>What became of a request for one client-safe assembly's bytes.</summary>
public enum ClientContractOutcome
{
    /// <summary>
    /// No Active package offers a file by that name to a client: every entry assembly, every shared contract
    /// outside a declared client facet, and every package whose facet is withheld. First so that a default
    /// value refuses rather than serves.
    /// </summary>
    NotOffered = 0,

    /// <summary>The exact bytes this installation admitted are being served.</summary>
    Served = 1,

    /// <summary>
    /// The file is offered under a different content hash than the one asked for, so the caller is holding a
    /// description of an installation this host is no longer running.
    /// </summary>
    Superseded = 2
}

/// <summary>The answer to a request for one client-safe assembly's bytes.</summary>
/// <param name="Outcome">Whether the bytes are being served, and if not, why not.</param>
/// <param name="Content">The exact admitted bytes when <see cref="Outcome"/> is served; otherwise empty.</param>
/// <param name="Identity">The complete CLR identity of those bytes when they are being served.</param>
public readonly record struct ClientContractContent(
    ClientContractOutcome Outcome,
    ReadOnlyMemory<byte> Content,
    string? Identity);

/// <summary>
/// What a browser may load from this host.
/// </summary>
/// <remarks>
/// <para>
/// A projection of the running installation and nothing else. A package appears here because it is Active
/// in this host and its declaration named a client facet; it disappears the moment either stops being true.
/// There is no separate registry to keep in step, no build-time list, and no route by which a file this
/// host did not admit can be served.
/// </para>
/// <para>
/// The bytes served are the bytes admitted. They are held from the shared-contract admission rather than
/// re-read from the package's folder, because the loader's own rule is that a file a package owns is read
/// once: a second read is a race in which the file inspected and the file served need not be the same file,
/// and here that race ends with a browser holding an assembly the host never proved.
/// </para>
/// </remarks>
public interface IClientContractCatalog
{
    /// <summary>
    /// Builds the current client loading manifest, including the facets this host will not serve.
    /// </summary>
    /// <returns>The contract identity, installation hash, publishing packages, and refusals.</returns>
    ClientContractManifest Manifest();

    /// <summary>
    /// Reads the bytes of one client-safe assembly.
    /// </summary>
    /// <param name="package">The publishing package.</param>
    /// <param name="fileName">The bare file name, exactly as the manifest states it.</param>
    /// <param name="contentHash">The content hash the caller believes those bytes have.</param>
    /// <returns>The bytes, or why they are not being served.</returns>
    ClientContractContent Open(PluginId package, string fileName, string contentHash);
}

/// <summary>
/// The one client contract catalog, projected from the plugin runtime registry.
/// </summary>
/// <remarks>
/// Recomputed per call rather than cached. The registry's Active set is the authority and it moves with
/// publication and teardown; a cache here would be a second answer to a question that already has one, and
/// the set it walks is an installation's shared contract assemblies, which is small by construction.
/// </remarks>
public sealed class ClientContractCatalog : IClientContractCatalog
{
    private readonly PluginRuntimeRegistry _registry;
    private readonly PluginPublicationGate _publication;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientContractCatalog"/> class.
    /// </summary>
    /// <param name="registry">The registry whose Active packages are projected.</param>
    /// <param name="publication">The boundary a package is published and withdrawn across.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ClientContractCatalog(PluginRuntimeRegistry registry, PluginPublicationGate publication)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(publication);

        _registry = registry;
        _publication = publication;
    }

    /// <summary>
    /// Gets the complete CLR identity of the universal contract assembly this host is running.
    /// </summary>
    /// <remarks>
    /// Read from the contract assembly itself rather than from a constant. A version written down twice is
    /// a version that can disagree with itself, and this value is the one thing a client checks before it
    /// binds anything.
    /// </remarks>
    public static string ContractIdentity { get; } =
        typeof(ClientContractManifest).Assembly.GetName().FullName;

    /// <inheritdoc />
    public ClientContractManifest Manifest()
    {
        // Held for the whole projection. A package's lease owns its admitted contracts, and reading half an
        // installation either side of a withdrawal would describe one that never existed.
        using var read = _publication.EnterRead();

        var resolved = Resolve();
        var packages = new List<ClientContractPackage>(resolved.Offering.Count);

        foreach (var entry in resolved.InIdentifierOrder())
        {
            var closure = resolved.ClosureOf(entry);
            packages.Add(new ClientContractPackage(
                entry.Id,
                entry.Version,
                entry.Name,
                entry.Assemblies.Select(assembly => assembly.View).ToList().AsReadOnly(),
                closure.Select(member => member.Id).ToList().AsReadOnly(),
                ClosureHash(closure)));
        }

        return new ClientContractManifest(
            ContractIdentity,
            InstallationHash(packages),
            packages.AsReadOnly(),
            resolved.Refusals);
    }

    /// <inheritdoc />
    public ClientContractContent Open(PluginId package, string fileName, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        // Held across the projection AND the byte copy below. Releasing between them would hand out a view
        // over bytes whose lease had already been released.
        using var read = _publication.EnterRead();

        var resolved = Resolve();

        if (!resolved.Offering.TryGetValue(package, out var offering))
        {
            return new ClientContractContent(ClientContractOutcome.NotOffered, default, null);
        }

        var offered = offering.Assemblies.FirstOrDefault(assembly =>
            string.Equals(assembly.View.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (offered is null)
        {
            return new ClientContractContent(ClientContractOutcome.NotOffered, default, null);
        }

        // Ordinal-ignore-case rather than ordinal: the hash is hexadecimal text, and a caller that echoed it
        // back in the other case is holding the right bytes.
        return string.Equals(offered.View.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
            ? new ClientContractContent(
                ClientContractOutcome.Served,

                // Copied under the gate. The caller writes these bytes to a socket long after this method
                // returns, and by then the package may have been withdrawn.
                offered.Content.ToArray(),
                offered.View.Identity)
            : new ClientContractContent(ClientContractOutcome.Superseded, default, null);
    }

    /// <summary>
    /// Renders one package's closure canonically, so that two hosts running the same installation compute
    /// the same hash and any difference in what a client would load changes it.
    /// </summary>
    private static string ClosureHash(IReadOnlyList<ClientFacetCandidate> closure)
    {
        var canonical = new StringBuilder();

        foreach (var package in closure)
        {
            canonical.Append("package\n").Append(package.Id.Value).Append('\n')
                .Append(package.Version).Append('\n');

            foreach (var assembly in package.Assemblies)
            {
                canonical.Append("assembly\n").Append(assembly.View.Identity).Append('\n')
                    .Append(assembly.View.ContentHash).Append('\n');
            }
        }

        return Hash(canonical.ToString());
    }

    private static string InstallationHash(IReadOnlyList<ClientContractPackage> packages)
    {
        var canonical = new StringBuilder();

        foreach (var package in packages)
        {
            canonical.Append(package.Id.Value).Append('\n').Append(package.ClosureHash).Append('\n');
        }

        return Hash(canonical.ToString());
    }

    private static string Hash(string canonical)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

    /// <summary>
    /// Reads the Active set once and resolves what a browser may load from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One snapshot, deliberately. The candidates and the admitted-contract names are two views of the same
    /// installation, and taking them separately means a package can be Active in the first and gone in the
    /// second — after which an assembly reference is "admitted but unreachable" because the package that
    /// published it withdrew between two reads, and a working installation reports itself broken.
    /// </para>
    /// <para>
    /// The admitted set is what an offered assembly's references are checked against. A reference to one of
    /// these names that the referring package's client closure does not offer is a server-only facet leaking
    /// into a browser; a reference to anything else is a framework assembly the browser already has.
    /// </para>
    /// </remarks>
    private ResolvedClientFacets Resolve()
    {
        var active = _registry.Active;
        var admitted = new HashSet<string>(StagedAssembly.NameComparer);
        var candidates = new List<ClientFacetCandidate>();

        foreach (var result in active)
        {
            if (result.PackageLease is not { } lease)
            {
                continue;
            }

            foreach (var contract in lease.Contracts.Published)
            {
                admitted.Add(contract.Identity.Name);
            }
        }

        foreach (var result in active)
        {
            if (result.PackageLease is not { } lease)
            {
                continue;
            }

            var package = lease.Receipt.Package;

            if (package.ClientContractAssemblies.Count == 0)
            {
                continue;
            }

            var assemblies = new List<OfferedAssembly>(package.ClientContractAssemblies.Count);
            var unadmitted = new List<string>();

            foreach (var fileName in package.ClientContractAssemblies)
            {
                var contract = lease.Contracts.Published.FirstOrDefault(published =>
                    string.Equals(
                        Path.GetFileName(published.SourcePath),
                        fileName,
                        StringComparison.OrdinalIgnoreCase));

                if (contract is null)
                {
                    unadmitted.Add(fileName);
                    continue;
                }

                assemblies.Add(new OfferedAssembly(
                    new ClientContractAssembly(
                        contract.Identity.Name,
                        fileName,
                        contract.Identity.ToString(),
                        contract.ContentHash,
                        contract.ModuleVersionId,
                        contract.Content.Length),
                    contract.Content,
                    new ReadOnlyCollection<string>(
                        [.. contract.Assembly.GetReferencedAssemblies()
                            .Select(reference => reference.Name)
                            .OfType<string>()])));
            }

            candidates.Add(new ClientFacetCandidate(
                package.Id,
                package.Version.ToString(),
                result.Manifest?.Name ?? package.Id.Value,
                new ReadOnlyCollection<PluginId>(
                    [.. package.Requirements.Select(requirement => requirement.PackageId)]),
                new ReadOnlyCollection<OfferedAssembly>(
                    [.. assemblies.OrderBy(assembly => assembly.View.AssemblyName, StringComparer.Ordinal)]),
                new ReadOnlyCollection<string>([.. unadmitted.Order(StringComparer.Ordinal)])));
        }

        return ClientFacetResolver.Resolve(candidates, admitted);
    }
}

/// <summary>One offered assembly: what the manifest says about it, its bytes, and what it references.</summary>
/// <param name="View">The published description.</param>
/// <param name="Content">The exact admitted bytes.</param>
/// <param name="References">The simple names of every assembly it references.</param>
internal sealed record OfferedAssembly(
    ClientContractAssembly View,
    ReadOnlyMemory<byte> Content,
    ReadOnlyCollection<string> References);

/// <summary>One Active package that declared a client facet, before any withholding rule is applied.</summary>
/// <param name="Id">The package identifier.</param>
/// <param name="Version">The installed version.</param>
/// <param name="Name">The name an operator sees.</param>
/// <param name="Requires">Its direct requirements, canonically ordered and deduplicated.</param>
/// <param name="Assemblies">The assemblies it offers, ordered by simple name.</param>
/// <param name="Unadmitted">
/// File names it offers which this installation admitted no contract for. A non-empty list withholds the
/// package before any closure rule runs.
/// </param>
internal sealed record ClientFacetCandidate(
    PluginId Id,
    string Version,
    string Name,
    ReadOnlyCollection<PluginId> Requires,
    ReadOnlyCollection<OfferedAssembly> Assemblies,
    ReadOnlyCollection<string> Unadmitted);

/// <summary>The facets this host will serve, the ones it will not, and the closures over them.</summary>
internal sealed class ResolvedClientFacets
{
    internal ResolvedClientFacets(
        Dictionary<PluginId, ClientFacetCandidate> offering,
        IReadOnlyList<ClientContractRefusal> refusals)
    {
        Offering = offering;
        Refusals = refusals;
    }

    /// <summary>Gets the packages whose facet this host will serve.</summary>
    public Dictionary<PluginId, ClientFacetCandidate> Offering { get; }

    /// <summary>Gets the withheld facets, ordered by identifier.</summary>
    public IReadOnlyList<ClientContractRefusal> Refusals { get; }

    /// <summary>Gets the offering packages in identifier order.</summary>
    public IEnumerable<ClientFacetCandidate> InIdentifierOrder()
        => Offering.Values.OrderBy(entry => entry.Id.Value, StringComparer.Ordinal);

    /// <summary>
    /// Walks one package's transitive client closure, dependency first.
    /// </summary>
    /// <remarks>
    /// Post-order over canonically ordered requirements, restricted to offering packages, so the result is
    /// the order a browser can load in: nothing appears before something it binds to. The graph is already
    /// proved acyclic by resolution, and the visited set makes a diamond one entry rather than two.
    /// </remarks>
    public IReadOnlyList<ClientFacetCandidate> ClosureOf(ClientFacetCandidate package)
    {
        var ordered = new List<ClientFacetCandidate>();
        var visited = new HashSet<PluginId>();
        Walk(package, ordered, visited);
        return ordered;
    }

    private void Walk(ClientFacetCandidate package, List<ClientFacetCandidate> ordered, HashSet<PluginId> visited)
    {
        if (!visited.Add(package.Id))
        {
            return;
        }

        foreach (var requirement in package.Requires)
        {
            if (Offering.TryGetValue(requirement, out var dependency))
            {
                Walk(dependency, ordered, visited);
            }
        }

        ordered.Add(package);
    }
}

/// <summary>
/// Decides which declared client facets this host will actually serve.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and separated from the registry so the rule can be tested directly rather than believed.
/// </para>
/// <para>
/// A facet is servable only if a browser can bind everything its assemblies name — every admitted contract
/// they reference must be offered by that package or one in its client closure — and only if every required
/// facet it declares is itself being served. Withholding a package empties its assemblies out of every
/// closure containing it, which can make a dependant unservable in turn, so the rule runs to a fixed point:
/// a single pass lets the package examined first survive with a closure a later withdrawal already emptied.
/// </para>
/// </remarks>
internal static class ClientFacetResolver
{
    /// <summary>
    /// Resolves the servable facets and the refusals.
    /// </summary>
    /// <param name="candidates">Every Active package that declared a client facet.</param>
    /// <param name="admittedNames">The simple names of every shared contract this installation admitted.</param>
    /// <returns>The offering packages and the withheld ones.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    internal static ResolvedClientFacets Resolve(
        IReadOnlyList<ClientFacetCandidate> candidates,
        IReadOnlyCollection<string> admittedNames)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(admittedNames);

        var admitted = new HashSet<string>(admittedNames, StagedAssembly.NameComparer);
        var offering = new Dictionary<PluginId, ClientFacetCandidate>();

        // Canonical requirement order is established here rather than trusted from the caller, because it is
        // this rule's own invariant: a closure is walked in requirement order, and a closure hash is
        // computed from the walk. Two hosts running one installation must agree on that hash whatever order
        // an author wrote the declaration in.
        candidates = [.. candidates.Select(candidate => candidate with
        {
            Requires = new ReadOnlyCollection<PluginId>(
                [.. candidate.Requires.Distinct().OrderBy(id => id.Value, StringComparer.Ordinal)]),
        })];

        var refusals = new List<ClientContractRefusal>();
        var withheld = new HashSet<PluginId>();
        var declared = candidates.Select(candidate => candidate.Id).ToHashSet();

        // A facet naming a file this installation admitted no contract for is withheld before any closure
        // rule runs: there is nothing to reason about its references with.
        foreach (var candidate in candidates)
        {
            if (candidate.Unadmitted.Count == 0)
            {
                offering[candidate.Id] = candidate;
                continue;
            }

            refusals.Add(new ClientContractRefusal(
                candidate.Id,
                $"Package '{candidate.Id}' offers a client facet this host cannot serve: this installation "
                + "admitted no shared contract under "
                + string.Join(", ", candidate.Unadmitted.Select(name => $"'{name}'")) + ".",
                candidate.Unadmitted,
                null));

            withheld.Add(candidate.Id);
        }

        bool changed;
        do
        {
            changed = false;
            var view = new ResolvedClientFacets(offering, refusals);

            foreach (var candidate in view.InIdentifierOrder().ToArray())
            {
                // Two ways one facet can stop being servable, computed together so a refusal can say
                // everything that is wrong with it rather than whichever check happened to run first.
                //
                // A required package that declared a client facet and lost it takes its dependants with it,
                // whether or not this package's own assemblies happen to name anything of that package's. A
                // client closure is what a browser is told to load; serving a dependant out of a closure this
                // host has already refused would publish an installation the host does not stand behind, and
                // "it does not bind that assembly today" is a property of the current build rather than of
                // the dependency.
                var lost = candidate.Requires
                    .Where(requirement => declared.Contains(requirement) && withheld.Contains(requirement))
                    .OrderBy(requirement => requirement.Value, StringComparer.Ordinal)
                    .ToList();

                // And an assembly this facet binds which nothing in its closure offers, which is a
                // server-only contract leaking into a browser whether or not anything was withdrawn.
                var reachable = new HashSet<string>(
                    view.ClosureOf(candidate).SelectMany(member =>
                        member.Assemblies.Select(assembly => assembly.View.AssemblyName)),
                    StagedAssembly.NameComparer);

                var unreachable = candidate.Assemblies
                    .SelectMany(assembly => assembly.References)
                    .Where(name => admitted.Contains(name) && !reachable.Contains(name))
                    .Distinct(StagedAssembly.NameComparer)
                    .Order(StringComparer.Ordinal)
                    .ToList();

                if (lost.Count == 0 && unreachable.Count == 0)
                {
                    continue;
                }

                var reason = new StringBuilder();

                if (lost.Count > 0)
                {
                    reason.Append(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Package '{candidate.Id}' requires {string.Join(", ", lost.Select(id => $"'{id}'"))}, whose client facet this host withheld. A client closure this host will not serve in full is not served in part."));
                }

                if (unreachable.Count > 0)
                {
                    reason.Append(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{(reason.Length > 0 ? " It also" : $"Package '{candidate.Id}'")} offers a client facet that references {string.Join(", ", unreachable.Select(name => $"'{name}'"))}, which no package in its client closure offers. A browser would receive a closure it cannot bind."));
                }

                refusals.Add(new ClientContractRefusal(
                    candidate.Id,
                    reason.ToString(),
                    new ReadOnlyCollection<string>(unreachable),
                    lost.Count > 0 ? lost[0] : null));

                offering.Remove(candidate.Id);
                withheld.Add(candidate.Id);
                changed = true;
            }
        }
        while (changed);

        return new ResolvedClientFacets(
            offering,
            refusals.OrderBy(refusal => refusal.Package.Value, StringComparer.Ordinal).ToList().AsReadOnly());
    }
}
