using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Arronix.Abstractions.Wire;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;

namespace Arronix.Plugins.Registry;

/// <summary>What became of a request for one client-safe assembly's bytes.</summary>
public enum ClientContractOutcome
{
    /// <summary>The exact bytes this installation admitted are being served.</summary>
    Served = 0,

    /// <summary>
    /// No Active package offers a file by that name to a client. A browser asking for one is asking for
    /// something this host has never published, which includes every entry assembly and every shared
    /// contract outside the declared client facet.
    /// </summary>
    NotOffered = 1,

    /// <summary>
    /// The file is offered, but under a different content hash than the one asked for. The caller is
    /// holding a description of an installation this host is no longer running.
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
/// What a browser client may load from this host.
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
    /// Builds the current client loading manifest.
    /// </summary>
    /// <returns>The contract identity, installation hash, and publishing packages.</returns>
    ClientContractManifest Manifest();

    /// <summary>
    /// Reads the bytes of one client-safe assembly.
    /// </summary>
    /// <param name="packageId">The publishing package.</param>
    /// <param name="fileName">The bare file name, exactly as the manifest states it.</param>
    /// <param name="contentHash">The content hash the caller believes those bytes have.</param>
    /// <returns>The bytes, or why they are not being served.</returns>
    ClientContractContent Open(string packageId, string fileName, string contentHash);

    /// <summary>
    /// Gets the Active packages whose declared client facet cannot be served, and why.
    /// </summary>
    /// <remarks>
    /// A package reaches this list after admission, so it cannot be quarantined for it. Withholding its
    /// facet and saying so is the honest outcome: a browser handed part of a closure would fail to bind at
    /// whichever type it touched first, which is a much harder failure to read than an absent package.
    /// </remarks>
    /// <returns>One sentence per withheld package, ordered by identifier.</returns>
    IReadOnlyList<string> Withheld();
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientContractCatalog"/> class.
    /// </summary>
    /// <param name="registry">The registry whose Active packages are projected.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
    public ClientContractCatalog(PluginRuntimeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
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
        var index = Build();
        var packages = new List<ClientContractPackage>(index.Offering.Count);

        foreach (var entry in index.Offering.Values.OrderBy(entry => entry.Id, StringComparer.Ordinal))
        {
            var closure = index.ClosureOf(entry);
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
            packages.AsReadOnly());
    }

    /// <inheritdoc />
    public ClientContractContent Open(string packageId, string fileName, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        var index = Build();

        if (!index.Offering.TryGetValue(packageId, out var package))
        {
            return new ClientContractContent(ClientContractOutcome.NotOffered, default, null);
        }

        var offered = package.Assemblies.FirstOrDefault(assembly =>
            string.Equals(assembly.View.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (offered is null)
        {
            return new ClientContractContent(ClientContractOutcome.NotOffered, default, null);
        }

        // Ordinal-ignore-case rather than ordinal: the hash is hexadecimal text, and a caller that echoed it
        // back in the other case is holding the right bytes.
        return string.Equals(offered.View.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
            ? new ClientContractContent(ClientContractOutcome.Served, offered.Content, offered.View.Identity)
            : new ClientContractContent(ClientContractOutcome.Superseded, default, null);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Withheld() => Build().Withheld;

    /// <summary>
    /// Renders one package's closure canonically, so that two hosts running the same installation compute
    /// the same hash and any difference in what a client would load changes it.
    /// </summary>
    private static string ClosureHash(IReadOnlyList<OfferingPackage> closure)
    {
        var canonical = new StringBuilder();

        foreach (var package in closure)
        {
            canonical.Append("package\n").Append(package.Id).Append('\n')
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
            canonical.Append(package.Id).Append('\n').Append(package.ClosureHash).Append('\n');
        }

        return Hash(canonical.ToString());
    }

    private static string Hash(string canonical)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

    /// <summary>
    /// Projects the Active packages once, deciding for each whether its declared client facet is complete.
    /// </summary>
    private ClientContractIndex Build()
    {
        var active = _registry.Active;
        var admittedNames = new HashSet<string>(StagedAssembly.NameComparer);
        var candidates = new List<(InstalledPackage Package, string Name, IReadOnlyList<AdmittedContract> Published)>();

        foreach (var result in active)
        {
            if (result.PackageLease is not { } lease)
            {
                continue;
            }

            foreach (var contract in lease.Contracts.Published)
            {
                admittedNames.Add(contract.Identity.Name);
            }

            candidates.Add((lease.Receipt.Package, result.Manifest?.Name ?? lease.Receipt.Id.ToString(), lease.Contracts.Published));
        }

        var offering = new Dictionary<string, OfferingPackage>(StringComparer.Ordinal);
        var withheld = new List<string>();

        foreach (var candidate in candidates.Where(entry => entry.Package.ClientContractAssemblies.Count > 0))
        {
            var assemblies = new List<OfferedAssembly>(candidate.Package.ClientContractAssemblies.Count);
            var missing = new List<string>();

            foreach (var fileName in candidate.Package.ClientContractAssemblies)
            {
                var contract = candidate.Published.FirstOrDefault(published =>
                    string.Equals(
                        Path.GetFileName(published.SourcePath),
                        fileName,
                        StringComparison.OrdinalIgnoreCase));

                if (contract is null)
                {
                    missing.Add($"'{fileName}' is offered to clients but this installation admitted no such contract from it");
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
                    contract.Assembly.GetReferencedAssemblies()));
            }

            if (missing.Count > 0)
            {
                withheld.Add($"Package '{candidate.Package.Id}' offers a client facet this host cannot serve: {string.Join("; ", missing)}.");
                continue;
            }

            offering[candidate.Package.Id.ToString()] = new OfferingPackage(
                candidate.Package.Id.ToString(),
                candidate.Package.Version.ToString(),
                candidate.Name,
                candidate.Package.Requirements.Select(requirement => requirement.PackageId.ToString()).ToList().AsReadOnly(),
                assemblies.OrderBy(assembly => assembly.View.AssemblyName, StringComparer.Ordinal).ToList().AsReadOnly());
        }

        var index = new ClientContractIndex(offering, withheld);

        // A client facet is only meaningful if a browser can bind everything the offered assemblies name.
        // Every Arronix contract an offered assembly references must itself be offered, by this package or
        // by one in its closure; a reference to an admitted contract outside that set is a server-only
        // facet leaking into the browser, and the package is withheld rather than half-served.
        foreach (var package in offering.Values.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray())
        {
            var reachable = new HashSet<string>(
                index.ClosureOf(package).SelectMany(member => member.Assemblies.Select(assembly => assembly.View.AssemblyName)),
                StagedAssembly.NameComparer);

            var unreachable = package.Assemblies
                .SelectMany(assembly => assembly.References.Select(reference => reference.Name))
                .OfType<string>()
                .Where(name => admittedNames.Contains(name) && !reachable.Contains(name))
                .Distinct(StagedAssembly.NameComparer)
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (unreachable.Length == 0)
            {
                continue;
            }

            withheld.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Package '{package.Id}' offers a client facet that references {string.Join(", ", unreachable.Select(name => $"'{name}'"))}, which no package in its client closure offers. A browser would receive a closure it cannot bind."));

            offering.Remove(package.Id);
        }

        withheld.Sort(StringComparer.Ordinal);
        return index;
    }

    /// <summary>One offered assembly: what the manifest says about it, its bytes, and what it references.</summary>
    private sealed record OfferedAssembly(
        ClientContractAssembly View,
        ReadOnlyMemory<byte> Content,
        System.Reflection.AssemblyName[] References);

    /// <summary>One Active package with a complete client facet.</summary>
    private sealed record OfferingPackage(
        string Id,
        string Version,
        string Name,
        ReadOnlyCollection<string> Requires,
        ReadOnlyCollection<OfferedAssembly> Assemblies);

    /// <summary>The offering packages and the closures over them.</summary>
    private sealed class ClientContractIndex(
        Dictionary<string, OfferingPackage> offering,
        List<string> withheld)
    {
        public Dictionary<string, OfferingPackage> Offering { get; } = offering;

        public IReadOnlyList<string> Withheld { get; } = withheld.AsReadOnly();

        /// <summary>
        /// Walks one package's transitive client closure, dependency first.
        /// </summary>
        /// <remarks>
        /// Post-order over declared requirements, restricted to offering packages, so the result is the
        /// order a browser can load in: nothing appears before something it binds to. The graph is already
        /// proved acyclic by resolution, and the visited set makes a diamond one entry rather than two.
        /// </remarks>
        public IReadOnlyList<OfferingPackage> ClosureOf(OfferingPackage package)
        {
            var ordered = new List<OfferingPackage>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            Walk(package, ordered, visited);
            return ordered;
        }

        private void Walk(OfferingPackage package, List<OfferingPackage> ordered, HashSet<string> visited)
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
}
