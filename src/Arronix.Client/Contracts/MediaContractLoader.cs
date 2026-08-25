using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Wire;
using Arronix.Client.Serialization;

namespace Arronix.Client.Contracts;

/// <summary>
/// Loads the media contract assemblies the host admitted into this browser.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the only place in the client where an installed media kind stops being data and starts
/// being types.</strong> The client compiles against the universal contract assembly and against nothing
/// else — no Movies, no Video, no media package of any kind — and it acquires the rest at run time from
/// whichever host it was served by. That is what makes one client able to render an installation whose
/// media kinds it has never heard of without reducing them to a bag of strings.
/// </para>
/// <para>
/// Three things are proved before the runtime is allowed near any bytes, and the order matters:
/// </para>
/// <list type="number">
///   <item><description>The host's universal contract identity is exactly this client's. A media contract
///   compiled against a different contract line cannot bind here, so an installation carrying one is
///   refused whole rather than in the parts that happen to resolve.</description></item>
///   <item><description>The bytes received hash to the content hash the host published, wherever they came
///   from. A store hit is treated with exactly the same suspicion as a network response.</description></item>
///   <item><description>What the runtime loaded is what was published: the same CLR identity, the same
///   module version identifier, and a reference to the universal contract that resolves — by object
///   identity — to this client's own compiled contract assembly.</description></item>
/// </list>
/// <para>
/// Nothing here enumerates a loaded assembly's types or reads its properties. Discovery of what a media
/// contract <i>contains</i> belongs to the generated metadata a later gate adds; this gate is the identity
/// and the transport, and it stays inside the small, annotated reflection surface a trimmed client can
/// keep.
/// </para>
/// <para>
/// A browser cannot unload an assembly. That is stated here because it shapes the design rather than
/// because it is an incidental limitation: contracts are loaded into the default context once per page, a
/// second pass reuses what a first pass loaded, and an installation that changes underneath a live tab is a
/// condition to report rather than to reconcile.
/// </para>
/// </remarks>
public sealed class MediaContractLoader
{
    private const string ManifestPath = "api/v1/client-contracts";

    private readonly HttpClient _http;
    private readonly ContractStore _store;
    private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaContractLoader"/> class.
    /// </summary>
    /// <param name="http">The connection to the host that served this client.</param>
    /// <param name="store">This browser's contract store.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public MediaContractLoader(HttpClient http, ContractStore store)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(store);

        _http = http;
        _store = store;
    }

    /// <summary>
    /// Gets the universal contract identity this client was compiled against.
    /// </summary>
    /// <remarks>
    /// Read from a type the client genuinely uses, so that the identity reported is the identity of the
    /// assembly actually in this application rather than of a name written down twice.
    /// </remarks>
    public static string ClientContractIdentity { get; } =
        typeof(IMediaEntity).Assembly.GetName().FullName;

    /// <summary>Gets the result of the last load, or <see langword="null"/> before the first.</summary>
    public ContractLoadReport? Report { get; private set; }

    /// <summary>
    /// Occurs when a load has completed and <see cref="Report"/> has been replaced.
    /// </summary>
    public event EventHandler? Loaded;

    /// <summary>
    /// Gets the assembly loaded for a simple name, or <see langword="null"/> when this page has not loaded it.
    /// </summary>
    /// <param name="assemblyName">The simple assembly name.</param>
    /// <returns>The loaded assembly.</returns>
    public Assembly? Find(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        return _loaded.GetValueOrDefault(assemblyName);
    }

    /// <summary>
    /// Reads what the host publishes and loads everything this client is entitled to.
    /// </summary>
    /// <param name="cancellationToken">Abandons the load.</param>
    /// <returns>What was published and what became of it.</returns>
    public async Task<ContractLoadReport> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var report = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            Report = report;
            Loaded?.Invoke(this, EventArgs.Empty);
            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ContractLoadReport> LoadCoreAsync(CancellationToken cancellationToken)
    {
        ClientContractManifest? manifest;

        try
        {
            manifest = await _http
                .GetFromJsonAsync<ClientContractManifest>(ManifestPath, ApiJsonOptions.Default, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            return Refused(ContractCompatibility.Unreachable, null, null, failure.Message);
        }

        if (manifest is null)
        {
            return Refused(
                ContractCompatibility.Unreachable,
                null,
                null,
                "The host answered the contract manifest with an empty body.");
        }

        // Case-insensitive because the two sides render a public key token in different cases and mean the
        // same eight bytes by it. Every other part of the identity is exact.
        if (!string.Equals(manifest.ContractIdentity, ClientContractIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return Refused(
                ContractCompatibility.ContractIdentityMismatch,
                manifest.ContractIdentity,
                manifest.InstallationHash,
                $"This host publishes media contracts against '{manifest.ContractIdentity}', and this client was "
                + $"compiled against '{ClientContractIdentity}'. Nothing was loaded: a contract assembly built "
                + "against a different contract line cannot bind here, and loading the part of an installation "
                + "that happens to resolve would render values whose meaning nothing has agreed on.");
        }

        var packages = manifest.Packages.ToDictionary(package => package.Id, StringComparer.Ordinal);
        var loadedPackages = new List<LoadedContractPackage>(manifest.Packages.Count);
        var results = new Dictionary<string, List<LoadedContractAssembly>>(StringComparer.Ordinal);

        // Closure order, so a dependency is loaded before anything that binds to it. The host states the
        // order; this client does not recompute it from reference tables, which describe what an assembly
        // names rather than what a package is entitled to.
        foreach (var package in manifest.Packages)
        {
            foreach (var member in package.Closure)
            {
                if (results.ContainsKey(member) || !packages.TryGetValue(member, out var source))
                {
                    continue;
                }

                var assemblies = new List<LoadedContractAssembly>(source.Assemblies.Count);

                foreach (var assembly in source.Assemblies)
                {
                    assemblies.Add(await LoadAssemblyAsync(source.Id, assembly, cancellationToken).ConfigureAwait(false));
                }

                results[member] = assemblies;
            }
        }

        foreach (var package in manifest.Packages)
        {
            loadedPackages.Add(new LoadedContractPackage(
                package.Id,
                package.Version,
                package.Name,
                package.ClosureHash,
                package.Closure,
                results.TryGetValue(package.Id, out var assemblies) ? assemblies : []));
        }

        return new ContractLoadReport(
            ContractCompatibility.Compatible,
            manifest.ContractIdentity,
            ClientContractIdentity,
            manifest.InstallationHash,
            loadedPackages,
            _store.IsAvailable,
            null);
    }

    private async Task<LoadedContractAssembly> LoadAssemblyAsync(
        string packageId,
        ClientContractAssembly published,
        CancellationToken cancellationToken)
    {
        if (_loaded.TryGetValue(published.AssemblyName, out var already))
        {
            return Describe(published, ContractLoadOutcome.AlreadyLoaded, ContractByteSource.Store, published.ContentHash, already);
        }

        byte[]? content = null;
        var source = ContractByteSource.Store;

        try
        {
            content = await _store.ReadAsync(published.ContentHash).ConfigureAwait(false);

            if (content is null)
            {
                source = ContractByteSource.Network;
                content = await _http
                    .GetByteArrayAsync(AddressOf(packageId, published), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception failure)
        {
            return new LoadedContractAssembly(
                published,
                ContractLoadOutcome.Failed,
                source,
                null,
                null,
                null,
                false,
                failure.Message);
        }

        // Hashed wherever it came from. A store this client wrote is still a store a browser extension, a
        // shared machine or a bug could have written to, and the content hash is the only thing that makes
        // "these are the bytes the host admitted" a checkable statement rather than a hopeful one.
        var observed = Convert.ToHexString(SHA256.HashData(content));

        if (!string.Equals(observed, published.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            if (source == ContractByteSource.Store)
            {
                await _store.RemoveAsync(published.ContentHash).ConfigureAwait(false);
            }

            return new LoadedContractAssembly(
                published,
                ContractLoadOutcome.ContentHashMismatch,
                source,
                observed,
                null,
                null,
                false,
                $"'{published.FileName}' hashed to {observed}; the host published {published.ContentHash}. Nothing was loaded.");
        }

        Assembly assembly;

        try
        {
            assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(content, writable: false));
        }
        catch (Exception failure)
        {
            return new LoadedContractAssembly(
                published,
                ContractLoadOutcome.Failed,
                source,
                observed,
                null,
                null,
                false,
                failure.Message);
        }

        if (source == ContractByteSource.Network)
        {
            await _store.WriteAsync(published.ContentHash, content).ConfigureAwait(false);
        }

        _loaded[published.AssemblyName] = assembly;
        return Describe(published, ContractLoadOutcome.Loaded, source, observed, assembly);
    }

    /// <summary>
    /// Compares what loaded against what was published, and asks the decisive question: does its reference
    /// to the universal contract resolve to this client's own copy?
    /// </summary>
    private static LoadedContractAssembly Describe(
        ClientContractAssembly published,
        ContractLoadOutcome outcome,
        ContractByteSource source,
        string? observedContentHash,
        Assembly assembly)
    {
        var identity = assembly.GetName().FullName;
        var module = assembly.ManifestModule.ModuleVersionId;

        var identityAgrees = string.Equals(identity, published.Identity, StringComparison.OrdinalIgnoreCase)
            && module == published.ModuleVersionId;

        var contract = typeof(IMediaEntity).Assembly;
        var binds = false;
        string? failure = null;

        try
        {
            var reference = assembly.GetReferencedAssemblies()
                .FirstOrDefault(name => string.Equals(name.Name, contract.GetName().Name, StringComparison.OrdinalIgnoreCase));

            // Object identity, not name equality. Two assemblies with one name are exactly the failure a
            // shared contract exists to prevent, and only the loader can say whether this one avoided it.
            binds = reference is not null
                && ReferenceEquals(AssemblyLoadContext.Default.LoadFromAssemblyName(reference), contract);
        }
        catch (Exception resolution)
        {
            failure = resolution.Message;
        }

        if (identityAgrees && binds)
        {
            return new LoadedContractAssembly(published, outcome, source, observedContentHash, identity, module, true, null);
        }

        return new LoadedContractAssembly(
            published,
            ContractLoadOutcome.IdentityMismatch,
            source,
            observedContentHash,
            identity,
            module,
            binds,
            failure ?? (identityAgrees
                ? $"'{published.FileName}' loaded, but its reference to '{contract.GetName().Name}' does not resolve to this client's own contract assembly."
                : $"'{published.FileName}' loaded as '{identity}' (module {module}); the host published '{published.Identity}' (module {published.ModuleVersionId})."));
    }

    private static string AddressOf(string packageId, ClientContractAssembly published)
        => $"{ManifestPath}/{Uri.EscapeDataString(packageId)}/{Uri.EscapeDataString(published.ContentHash)}/{Uri.EscapeDataString(published.FileName)}";

    private ContractLoadReport Refused(
        ContractCompatibility compatibility,
        string? publishedIdentity,
        string? installationHash,
        string failure)
        => new(compatibility, publishedIdentity, ClientContractIdentity, installationHash, [], _store.IsAvailable, failure);
}
