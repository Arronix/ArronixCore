using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;
using Arronix.Client.Diagnostics;
using Arronix.Client.Serialization;

namespace Arronix.Client.Contracts;

/// <summary>
/// Loads the media contract assemblies the host admitted into this browser.
/// </summary>
/// <remarks>
/// <para>
/// The client compiles against the universal contract assembly and nothing else, and acquires installed
/// media contracts at run time from the host that served it.
/// </para>
/// <para>
/// The browser's default load context cannot unload, which decides the shape of this class. Every question
/// about a payload — length, SHA-256, declared CLR identity, declared module version identifier, declared
/// universal-contract reference — is answered while it is still a byte array, and the whole required closure
/// is answered for before the first load. Verifying one payload at a time would admit the good half of a
/// closure and only then discover the bad half.
/// </para>
/// <para>
/// <see cref="ContractLoadReport.CanProject"/> is true only when every required assembly is verified and
/// resident. Diagnostics stay visible either way.
/// </para>
/// </remarks>
internal sealed class MediaContractLoader
{
    private const string ManifestPath = "api/v1/client-contracts";

    private readonly HttpClient _http;
    private readonly ContractStore _store;
    private readonly Func<byte[], Assembly> _load;
    private readonly Dictionary<string, ResidentContract> _resident = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _terminal;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaContractLoader"/> class.
    /// </summary>
    /// <param name="http">The connection to the host that served this client.</param>
    /// <param name="store">This browser's contract store.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public MediaContractLoader(HttpClient http, ContractStore store)
        : this(http, store, LoadIntoDefaultContext)
    {
    }

    /// <summary>
    /// Initializes a loader over a supplied runtime load.
    /// </summary>
    /// <param name="http">The connection to the host that served this client.</param>
    /// <param name="store">This browser's contract store.</param>
    /// <param name="load">
    /// How verified bytes become an assembly. The production value is
    /// <see cref="AssemblyLoadContext.LoadFromStream(System.IO.Stream)"/> on the default context; a test
    /// substitutes a runtime that returns an assembly other than the bytes it was handed, which is the only
    /// way to drive the post-load disagreement branch.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    internal MediaContractLoader(HttpClient http, ContractStore store, Func<byte[], Assembly> load)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(load);

        _http = http;
        _store = store;
        _load = load;
    }

    private static Assembly LoadIntoDefaultContext(byte[] content)
        => AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(content, writable: false));

    /// <summary>
    /// Gets the universal contract identity this client was compiled against.
    /// </summary>
    /// <remarks>
    /// Read from a type the client genuinely uses, so the identity reported is the identity of the assembly
    /// actually in this application rather than of a name written down twice.
    /// </remarks>
    public static string ClientContractIdentity { get; } =
        typeof(IMediaEntity).Assembly.GetName().FullName;

    /// <summary>Gets the simple name of the universal contract assembly.</summary>
    public static string ContractAssemblyName { get; } =
        typeof(IMediaEntity).Assembly.GetName().Name ?? "Arronix.Abstractions";

    /// <summary>Gets the result of the last load, or <see langword="null"/> before the first.</summary>
    public ContractLoadReport? Report { get; private set; }

    /// <summary>
    /// Gets the assembly verified and loaded for a simple name, or <see langword="null"/>.
    /// </summary>
    /// <param name="assemblyName">The simple assembly name.</param>
    /// <returns>The loaded assembly, when this page holds a complete verified installation.</returns>
    /// <remarks>
    /// <para>
    /// Answers nothing unless the last load proved the entire required set. A caller reaching for one
    /// verified assembly out of an installation that failed elsewhere is exactly the partial projection this
    /// design refuses.
    /// </para>
    /// <para>
    /// Nor for a name the installation this loader last read no longer carries: that would project a
    /// contract the host does not admit, which no caller downstream could detect.
    /// </para>
    /// </remarks>
    public Assembly? Find(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);

        return Current(assemblyName) is { } resident ? resident.Assembly : null;
    }

    /// <summary>Gets the client contracts one loaded assembly was admitted with, in published order.</summary>
    /// <param name="assemblyName">The simple assembly name.</param>
    /// <returns>The contracts, or empty when this page holds no complete verified installation.</returns>
    /// <remarks>
    /// The instances the post-load proof accepted, never a second reading or resolution, and gated exactly
    /// like <see cref="Find"/>. An orphan's declarations are withheld with its assembly: they are the other
    /// door into a contract the host has withdrawn.
    /// </remarks>
    internal IReadOnlyList<VerifiedClientContract> ContractsOf(string assemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);

        return Current(assemblyName) is { } resident ? resident.Contracts : [];
    }

    /// <summary>Lists every client contract this page holds, in the order the host published them.</summary>
    /// <returns>The contracts, or empty when this page holds no complete verified installation.</returns>
    /// <remarks>
    /// Gated like <see cref="Find"/> and built from the published order rather than from the order this
    /// loader happened to fill its own table in.
    /// </remarks>
    internal IReadOnlyList<AdmittedContract> Admitted()
    {
        if (Report is not { CanProject: true } report)
        {
            return [];
        }

        var admitted = new List<AdmittedContract>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in report.Packages)
        {
            foreach (var assembly in package.Assemblies)
            {
                var name = assembly.Published.AssemblyName;

                if (!seen.Add(name) || Current(name) is not { } resident)
                {
                    continue;
                }

                foreach (var contract in resident.Contracts)
                {
                    admitted.Add(new AdmittedContract(name, contract));
                }
            }
        }

        return admitted;
    }

    /// <summary>Gets the resident entry this page may serve under a simple name, or <see langword="null"/>.</summary>
    /// <remarks>Did the last pass prove the whole required set, and did it still name this assembly.</remarks>
    private ResidentContract? Current(string assemblyName)
        => Report?.CanProject == true
            && _resident.TryGetValue(assemblyName, out var resident)
            && !resident.Orphaned
                ? resident
                : null;

    /// <summary>
    /// Reads what the host publishes, verifies everything this client would need, and loads it.
    /// </summary>
    /// <param name="cancellationToken">Abandons the load.</param>
    /// <returns>What was published and what became of it.</returns>
    /// <remarks>
    /// Reading is all this does. Telling anyone belongs to the transaction that also sheds the bytes the
    /// installation no longer names, because a signal raised here would describe a page mid-sweep.
    /// </remarks>
    internal async Task<ContractLoadReport> LoadInstallationAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        ContractLoadReport report;

        try
        {
            report = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            Report = report;
        }
        finally
        {
            _gate.Release();
        }

        return report;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller abandoned the load, which says nothing about the host. Only its own token says
            // so: a timeout arrives as the same type and is an ordinary outcome that replaces the report.
            throw;
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            return Refused(ContractCompatibility.Unreachable, null, null, [], [], failure.Message);
        }

        if (manifest is null)
        {
            return Refused(
                ContractCompatibility.Unreachable,
                null,
                null,
                [],
                [],
                "The host answered the contract manifest with an empty body.");
        }

        // The document is untrusted input, and this is the first thing done with it. Everything below reads
        // it as a description of an installation - it renders it, indexes it by identifier, walks closures,
        // keys assemblies by simple name - and every one of those steps has a quietly wrong answer for a
        // document that is merely well-formed JSON. Even the contract-identity refusal below renders the
        // packages, so validation cannot come after it.
        if (ContractManifestValidator.Describe(manifest) is { } malformed)
        {
            return Refused(
                ContractCompatibility.ManifestInvalid,
                manifest.ContractIdentity,
                null,
                [],
                [],
                "This host's contract manifest does not describe an installation, so nothing was fetched and "
                + "nothing was loaded: " + malformed);
        }

        // Case-insensitive because the two sides render a public key token in different cases and mean the
        // same eight bytes by it. Every other part of the identity is exact.
        if (!string.Equals(manifest.ContractIdentity, ClientContractIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return Refused(
                ContractCompatibility.ContractIdentityMismatch,
                manifest.ContractIdentity,
                manifest.InstallationHash,
                Untouched(manifest),
                manifest.Refused,
                $"This host publishes media contracts against '{manifest.ContractIdentity}', and this client was "
                + $"compiled against '{ClientContractIdentity}'. Nothing was loaded: a contract assembly built "
                + "against a different contract line cannot bind here, and loading the part of an installation "
                + "that happens to resolve would render values whose meaning nothing has agreed on.");
        }

        if (_terminal is { } held)
        {
            return Refused(
                ContractCompatibility.Terminal,
                manifest.ContractIdentity,
                manifest.InstallationHash,
                Untouched(manifest),
                manifest.Refused,
                held);
        }

        // The required set is the union of every published package's closure, in the load order the host
        // stated, with each package taken once.
        var required = RequiredAssemblies(manifest);

        // Intended, not applied: a pass abandoned mid-fetch must leave this page describing the
        // installation it last actually read.
        var orphaning = Orphaning(required);

        var report = await ProveAsync(manifest, required, orphaning, cancellationToken).ConfigureAwait(false);

        // One transition: this pass's bookkeeping becomes visible with the report it publishes.
        Adopt(required, orphaning);
        return report;
    }

    /// <summary>Verifies the required set, commits what passes, and describes the result.</summary>
    /// <remarks>
    /// Adds to <see cref="_resident"/> as it loads, because an assembly the runtime took is resident
    /// whether or not this pass finishes. Orphan and owner bookkeeping is its caller's.
    /// </remarks>
    private async Task<ContractLoadReport> ProveAsync(
        ClientContractManifest manifest,
        List<(ContractOwner Owner, ClientContractAssembly Published)> required,
        HashSet<string> orphaning,
        CancellationToken cancellationToken)
    {
        if (Unchanged(manifest, required, orphaning) is { } unchanged)
        {
            return unchanged;
        }

        // Pass one: verify everything, load nothing.
        var verified = new Dictionary<string, Preflight>(StringComparer.OrdinalIgnoreCase);

        foreach (var (owner, published) in required)
        {
            verified[published.AssemblyName] =
                await PreflightAsync(owner.Id, published, cancellationToken).ConfigureAwait(false);
        }

        var blocked = verified.Values.Where(entry => !entry.IsVerified).ToArray();

        if (blocked.Length > 0)
        {
            var terminal = blocked.Any(entry => entry.Outcome == ContractLoadOutcome.NameAlreadyResident);

            if (terminal)
            {
                _terminal = "This page already holds a contract assembly the host no longer publishes, and a "
                    + "browser cannot unload one. Reload the page to load the installation this host is "
                    + "running now.";
            }

            return Refused(
                terminal ? ContractCompatibility.Terminal : ContractCompatibility.Refused,
                manifest.ContractIdentity,
                manifest.InstallationHash,
                Project(manifest, verified),
                manifest.Refused,
                terminal
                    ? _terminal
                    : $"{blocked.Length} of {verified.Count} required contract assemblies failed verification, "
                        + "so none was loaded and nothing may be projected. Each failure is listed against the "
                        + "assembly it belongs to.",
                Orphans(manifest, orphaning));
        }

        // Pass two: commit. Everything below this line is irreversible in a browser, which is why nothing
        // above it touched the runtime.
        foreach (var (owner, published) in required)
        {
            var entry = verified[published.AssemblyName];

            if (entry.Outcome == ContractLoadOutcome.AlreadyLoaded)
            {
                continue;
            }

            Assembly assembly;

            try
            {
                assembly = _load(entry.Content!);
            }
            catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
            {
                _terminal = $"'{published.FileName}' passed every check and the runtime still refused it: "
                    + failure.Message
                    + " Contract assemblies loaded before it cannot be unloaded, so this page can no longer "
                    + "match the installation. Reload the page.";

                verified[published.AssemblyName] = entry.RuntimeRefused(failure.Message);

                return Refused(
                    ContractCompatibility.Terminal,
                    manifest.ContractIdentity,
                    manifest.InstallationHash,
                    Project(manifest, verified),
                    manifest.Refused,
                    _terminal,
                    Orphans(manifest, orphaning));
            }

            var content = entry.Content!;

            // Preflight proved what the bytes declare; this proves what the runtime did with them. A load
            // context may return an occupant rather than the bytes it was handed.
            if (RuntimeDisagreement(assembly, published, cancellationToken, out var contracts)
                is { } disagreement)
            {
                _terminal = $"'{published.FileName}' was verified and the runtime produced something else: "
                    + disagreement
                    + " Contract assemblies loaded before it cannot be unloaded, so this page can no longer "
                    + "match the installation. Reload the page.";

                verified[published.AssemblyName] = entry.RuntimeRefused(disagreement);

                return Refused(
                    ContractCompatibility.Terminal,
                    manifest.ContractIdentity,
                    manifest.InstallationHash,
                    Project(manifest, verified),
                    manifest.Refused,
                    _terminal,
                    Orphans(manifest, orphaning));
            }

            // Only now, and with the contracts the proof captured, so reuse hands back what passed. The
            // owner travels with it: an entry whose package later leaves can only be attributed from what
            // was captured while it was still admitted.
            _resident[published.AssemblyName] = new ResidentContract(assembly, published, contracts, owner);

            // Loaded, and only now. Until this line the report said Verified, which is what was true.
            verified[published.AssemblyName] = entry.Committed();

            if (entry.Source == ContractByteSource.Network)
            {
                await _store.WriteContractAsync(published.ContentHash, content).ConfigureAwait(false);
            }
        }

        return new ContractLoadReport(
            ContractCompatibility.Compatible,
            manifest.ContractIdentity,
            ClientContractIdentity,
            manifest.InstallationHash,
            Project(manifest, verified),
            manifest.Refused,
            Orphans(manifest, orphaning),
            _store.IsAvailable,
            null);
    }

    /// <summary>
    /// Verifies one published assembly without letting the runtime near it.
    /// </summary>
    private async Task<Preflight> PreflightAsync(
        PluginId packageId,
        ClientContractAssembly published,
        CancellationToken cancellationToken)
    {
        // Already resident from an earlier pass of this page. A browser cannot unload it, so the only two
        // answers are "the same one the host is publishing" and "this page can never satisfy this host".
        if (_resident.TryGetValue(published.AssemblyName, out var resident))
        {
            return Matches(resident.Verified, published)
                ? Preflight.Reused(resident.Verified)
                : Preflight.Failed(
                    ContractLoadOutcome.NameAlreadyResident,
                    ContractByteSource.None,
                    $"'{published.AssemblyName}' is already loaded in this page as "
                    + $"{resident.Verified.Identity} (module {resident.Verified.ModuleVersionId}, content "
                    + $"{resident.Verified.ContentHash}, "
                    + $"{Count(resident.Verified)}); the host now publishes {published.Identity} (module "
                    + $"{published.ModuleVersionId}, content {published.ContentHash}, "
                    + $"{Count(published)}).");
        }

        if (AssemblyLoadContext.Default.Assemblies.Any(loaded =>
                string.Equals(loaded.GetName().Name, published.AssemblyName, StringComparison.OrdinalIgnoreCase)))
        {
            return Preflight.Failed(
                ContractLoadOutcome.NameAlreadyResident,
                ContractByteSource.None,
                $"This page already holds an assembly named '{published.AssemblyName}' which this client did "
                + "not load and cannot unload.");
        }

        byte[] content;
        var source = ContractByteSource.Store;

        try
        {
            var held = await _store.ReadContractAsync(published.ContentHash).ConfigureAwait(false);

            if (held is not null)
            {
                content = held;
            }
            else
            {
                source = ContractByteSource.Network;

                using var response = await _http
                    .GetAsync(AddressOf(packageId, published), cancellationToken)
                    .ConfigureAwait(false);

                if (Withdrawn(response.StatusCode, published) is { } withdrawn)
                {
                    return withdrawn;
                }

                response.EnsureSuccessStatusCode();
                content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            // A timeout lands here: the host failed to answer, rather than this caller withdrawing.
            return Preflight.Failed(ContractLoadOutcome.Unavailable, source, failure.Message);
        }

        if (content.Length != published.Length)
        {
            await DiscardIfStoredAsync(source, published).ConfigureAwait(false);

            return Preflight.Failed(
                ContractLoadOutcome.LengthMismatch,
                source,
                $"'{published.FileName}' arrived as {content.Length} bytes; the host published "
                + $"{published.Length}. Nothing was loaded.",
                observedLength: content.Length);
        }

        // Hashed wherever it came from. A store this client wrote is still a store a browser extension, a
        // shared machine or a bug could have written to, and the content hash is the only thing that makes
        // "these are the bytes the host admitted" a checkable statement rather than a hopeful one.
        var observedHash = Convert.ToHexString(SHA256.HashData(content));

        if (!string.Equals(observedHash, published.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            await DiscardIfStoredAsync(source, published).ConfigureAwait(false);

            return Preflight.Failed(
                ContractLoadOutcome.ContentHashMismatch,
                source,
                $"'{published.FileName}' hashed to {observedHash}; the host published {published.ContentHash}. "
                + "Nothing was loaded.",
                observedLength: content.Length,
                observedContentHash: observedHash);
        }

        if (!ContractMetadataReader.TryRead(content, ContractAssemblyName, out var metadata, out var unreadable))
        {
            await DiscardIfStoredAsync(source, published).ConfigureAwait(false);

            return Preflight.Failed(
                ContractLoadOutcome.Unavailable,
                source,
                $"'{published.FileName}' could not be described without loading it: {unreadable}",
                observedLength: content.Length,
                observedContentHash: observedHash);
        }

        if (!string.Equals(metadata!.Identity, published.Identity, StringComparison.OrdinalIgnoreCase))
        {
            return Preflight.Failed(
                ContractLoadOutcome.IdentityMismatch,
                source,
                $"'{published.FileName}' declares the identity '{metadata.Identity}'; the host published "
                + $"'{published.Identity}'. Nothing was loaded.",
                observedLength: content.Length,
                observedContentHash: observedHash,
                observedIdentity: metadata.Identity,
                observedModuleVersionId: metadata.ModuleVersionId,
                observedContractReference: metadata.ContractReference);
        }

        if (metadata.ModuleVersionId != published.ModuleVersionId)
        {
            return Preflight.Failed(
                ContractLoadOutcome.ModuleVersionMismatch,
                source,
                $"'{published.FileName}' declares module {metadata.ModuleVersionId}; the host published "
                + $"module {published.ModuleVersionId}. The bytes hash correctly and are a different build. "
                + "Nothing was loaded.",
                observedLength: content.Length,
                observedContentHash: observedHash,
                observedIdentity: metadata.Identity,
                observedModuleVersionId: metadata.ModuleVersionId,
                observedContractReference: metadata.ContractReference);
        }

        if (!string.Equals(metadata.ContractReference, ClientContractIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return Preflight.Failed(
                ContractLoadOutcome.ContractReferenceMismatch,
                source,
                $"'{published.FileName}' declares "
                + (metadata.ContractReference is null
                    ? $"no single reference to '{ContractAssemblyName}'"
                    : $"a reference to '{metadata.ContractReference}'")
                + $", and this client carries '{ClientContractIdentity}'. Binding it would give this page two "
                + "meanings for one contract. Nothing was loaded.",
                observedLength: content.Length,
                observedContentHash: observedHash,
                observedIdentity: metadata.Identity,
                observedModuleVersionId: metadata.ModuleVersionId,
                observedContractReference: metadata.ContractReference);
        }

        // The last thing the bytes are asked, and the first that is about their contents rather than their
        // identity. The host read the same blob when it admitted this file and published what it found; this
        // read it again from what arrived, and a published declaration is something to check against.
        if (DeclarationDisagreement(metadata, published) is { } declarations)
        {
            return Preflight.Failed(
                ContractLoadOutcome.DeclarationMismatch,
                source,
                $"'{published.FileName}' {declarations} Nothing was loaded.",
                observedLength: content.Length,
                observedContentHash: observedHash,
                observedIdentity: metadata.Identity,
                observedModuleVersionId: metadata.ModuleVersionId,
                observedContractReference: metadata.ContractReference,
                observedDeclarations: metadata.Declarations);
        }

        return Preflight.Verified(published, source, content, metadata);
    }

    /// <summary>
    /// Reads what the host said about an address it declined to serve, or nothing when it served it.
    /// </summary>
    /// <remarks>
    /// 410 means the file moved and the next manifest read resolves it; 404 means nothing is there under
    /// any hash; anything else is a transport failure left to the caller's catch.
    /// </remarks>
    private static Preflight? Withdrawn(HttpStatusCode status, ClientContractAssembly published)
        => status switch
        {
            HttpStatusCode.Gone => Preflight.Failed(
                ContractLoadOutcome.Superseded,
                ContractByteSource.Network,
                $"The host no longer serves '{published.FileName}' at content {published.ContentHash} and "
                + "publishes it under a different hash: this client's manifest was overtaken between "
                + "reading it and fetching these bytes. Re-reading it recovers. Nothing was loaded."),
            HttpStatusCode.NotFound => Preflight.Failed(
                ContractLoadOutcome.NotOffered,
                ContractByteSource.Network,
                $"The host offers no '{published.FileName}' to a client under any content hash, and its own "
                + "manifest named this one. Nothing was loaded."),
            _ => null,
        };

    /// <summary>
    /// Drops a stored entry that did not survive verification, so a poisoned store repairs itself.
    /// </summary>
    /// <remarks>
    /// Not a commit of loader state: it removes an entry rather than adding one, and the next pass refetches
    /// over the network and verifies again.
    /// </remarks>
    private async Task DiscardIfStoredAsync(ContractByteSource source, ClientContractAssembly published)
    {
        if (source == ContractByteSource.Store)
        {
            await _store.RemoveContractAsync(published.ContentHash).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lists every assembly this client must hold, in the order the host says it may be loaded.
    /// </summary>
    /// <remarks>
    /// The union of every published package's closure, each package taken once. The host states the order;
    /// this client does not recompute it from reference tables, which describe what an assembly names rather
    /// than what a package is entitled to.
    /// </remarks>
    private static List<(ContractOwner Owner, ClientContractAssembly Published)> RequiredAssemblies(
        ClientContractManifest manifest)
    {
        var byId = manifest.Packages.ToDictionary(package => package.Id);
        var seen = new HashSet<PluginId>();
        var required = new List<(ContractOwner, ClientContractAssembly)>();

        foreach (var package in manifest.Packages)
        {
            foreach (var member in package.Closure)
            {
                if (!seen.Add(member))
                {
                    continue;
                }

                // Every closure member is a published package: the validator proved it before this ran, so
                // there is no "unknown member" branch here to skip one silently.
                var source = byId[member];
                var owner = new ContractOwner(source.Id, source.Name, source.Version);

                foreach (var assembly in source.Assemblies)
                {
                    required.Add((owner, assembly));
                }
            }
        }

        return required;
    }

    /// <summary>Names this page holds which the installation just read does not.</summary>
    /// <remarks>
    /// A name the manifest does state is never here, however badly it then verifies — that is the separate
    /// <see cref="ContractLoadOutcome.NameAlreadyResident"/> question, and one name under both headings
    /// would describe two problems where there is one.
    /// </remarks>
    private HashSet<string> Orphaning(List<(ContractOwner Owner, ClientContractAssembly Published)> required)
    {
        var named = new HashSet<string>(
            required.Select(entry => entry.Published.AssemblyName),
            StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            _resident.Keys.Where(name => !named.Contains(name)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Makes one completed pass's bookkeeping this page's, in a single step.</summary>
    /// <remarks>Never a removal: nothing leaves <see cref="_resident"/> and a browser unloads nothing.</remarks>
    private void Adopt(
        List<(ContractOwner Owner, ClientContractAssembly Published)> required,
        HashSet<string> orphaning)
    {
        foreach (var (owner, published) in required)
        {
            if (_resident.TryGetValue(published.AssemblyName, out var named) && named.Owner != owner)
            {
                _resident[published.AssemblyName] = named with { Owner = owner };
            }
        }

        foreach (var name in _resident.Keys.ToArray())
        {
            var entry = _resident[name];
            var orphaned = orphaning.Contains(name);

            if (entry.Orphaned != orphaned)
            {
                _resident[name] = entry with { Orphaned = orphaned };
            }
        }
    }

    /// <summary>
    /// Answers the pass on which the installation this page confirmed is the one the host is publishing,
    /// or <see langword="null"/> when the ordinary pass has to run.
    /// </summary>
    /// <remarks>
    /// <see cref="ClientContractManifest.InstallationHash"/> decides whether to look, never what is true:
    /// an equal hash only permits the question, and every required assembly must still match the exact
    /// description this manifest states — the reuse gate's own comparison, over values already in memory,
    /// so no bytes are fetched and a restated declaration is still refused. Only a
    /// <see cref="ContractCompatibility.Compatible"/> previous result qualifies, because a refusal over an
    /// unchanged installation may have been a transport failure.
    /// </remarks>
    private ContractLoadReport? Unchanged(
        ClientContractManifest manifest,
        List<(ContractOwner Owner, ClientContractAssembly Published)> required,
        HashSet<string> orphaning)
    {
        if (Report is not { Compatibility: ContractCompatibility.Compatible, InstallationHash: { } confirmed }
            || !string.Equals(confirmed, manifest.InstallationHash, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var reused = new Dictionary<string, Preflight>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, published) in required)
        {
            if (!_resident.TryGetValue(published.AssemblyName, out var resident)
                || !Matches(resident.Verified, published))
            {
                return null;
            }

            reused[published.AssemblyName] = Preflight.Reused(published);
        }

        return new ContractLoadReport(
            ContractCompatibility.Compatible,
            manifest.ContractIdentity,
            ClientContractIdentity,
            manifest.InstallationHash,
            Project(manifest, reused),
            manifest.Refused,
            Orphans(manifest, orphaning),
            _store.IsAvailable,
            null);
    }

    /// <summary>Renders what this page holds and the installation just read does not name.</summary>
    /// <remarks>
    /// Labeled from that manifest alone: the host keeps no history, so why a package left is not a fact a
    /// client has.
    /// </remarks>
    private IReadOnlyList<OrphanedContract> Orphans(
        ClientContractManifest manifest,
        HashSet<string> orphaning)
        => orphaning
            .Select(name => _resident[name])
            .OrderBy(resident => resident.Verified.AssemblyName, StringComparer.Ordinal)
            .Select(resident => Describe(manifest, resident))
            .ToList()
            .AsReadOnly();

    private static OrphanedContract Describe(ClientContractManifest manifest, ResidentContract resident)
    {
        var owner = resident.Owner;
        var refusal = manifest.Refused.FirstOrDefault(entry => entry.Package == owner.Id);

        var state = refusal is not null
            ? OrphanedContractOwner.Withheld
            : manifest.Packages.Any(package => package.Id == owner.Id)
                ? OrphanedContractOwner.Offered
                : OrphanedContractOwner.Unpublished;

        return new OrphanedContract(resident.Verified, owner.Id, owner.Name, owner.Version, state, refusal);
    }

    /// <summary>Renders the per-package view from the verification results.</summary>
    private static IReadOnlyList<LoadedContractPackage> Project(
        ClientContractManifest manifest,
        IReadOnlyDictionary<string, Preflight> verified)
        => manifest.Packages
            .Select(package => new LoadedContractPackage(
                package.Id,
                package.Version,
                package.Name,
                package.ClosureHash,
                package.Closure,
                package.Assemblies
                    .Select(assembly => verified.TryGetValue(assembly.AssemblyName, out var entry)
                        ? entry.ToView(assembly)
                        : Preflight.NotAttempted(assembly))
                    .ToList()
                    .AsReadOnly()))
            .ToList()
            .AsReadOnly();

    /// <summary>Renders the per-package view for an installation nothing was attempted against.</summary>
    private static IReadOnlyList<LoadedContractPackage> Untouched(ClientContractManifest manifest)
        => manifest.Packages
            .Select(package => new LoadedContractPackage(
                package.Id,
                package.Version,
                package.Name,
                package.ClosureHash,
                package.Closure,
                package.Assemblies.Select(Preflight.NotAttempted).ToList().AsReadOnly()))
            .ToList()
            .AsReadOnly();

    /// <summary>
    /// Describes how a loaded assembly differs from what was published, or nothing when it does not.
    /// </summary>
    /// <remarks>
    /// Three questions, and the third is the one no byte inspection can answer. What identity did the
    /// runtime bind these bytes as; which build is the module it produced; and does the reference this
    /// assembly makes to the universal contract resolve, in this load context, to the very
    /// <see cref="Assembly"/> object this client compiled against? Object identity, not name equality: two
    /// assemblies with one name is exactly the failure a shared contract exists to prevent, and only the
    /// loader can say whether a particular load avoided it.
    /// </remarks>
    private static string? RuntimeDisagreement(
        Assembly assembly,
        ClientContractAssembly published,
        CancellationToken cancellationToken,
        out IReadOnlyList<VerifiedClientContract> contracts)
    {
        contracts = [];
        var loaded = assembly.GetName();

        if (!string.Equals(loaded.FullName, published.Identity, StringComparison.OrdinalIgnoreCase))
        {
            return $"it loaded as '{loaded.FullName}' rather than '{published.Identity}'.";
        }

        if (assembly.ManifestModule.ModuleVersionId != published.ModuleVersionId)
        {
            return $"its loaded module is {assembly.ManifestModule.ModuleVersionId} rather than "
                + $"{published.ModuleVersionId}.";
        }

        var contract = typeof(IMediaEntity).Assembly;

        try
        {
            var reference = assembly.GetReferencedAssemblies()
                .FirstOrDefault(name => string.Equals(
                    name.Name, ContractAssemblyName, StringComparison.OrdinalIgnoreCase));

            if (reference is null)
            {
                return $"it references no '{ContractAssemblyName}' once loaded.";
            }

            if (!ReferenceEquals(AssemblyLoadContext.Default.LoadFromAssemblyName(reference), contract))
            {
                return $"its reference to '{ContractAssemblyName}' resolves to a different assembly object "
                    + "than this client's own contract.";
            }
        }
        catch (Exception resolution) when (!ProcessFailure.IsFatal(resolution))
        {
            return $"its reference to '{ContractAssemblyName}' could not be resolved: {resolution.Message}";
        }

        return DeclarationDisagreement(assembly, published, cancellationToken, out contracts);
    }

    /// <summary>
    /// Admits the loaded assembly's declarations, or says why none of them may be, and yields what was
    /// proved when they all are.
    /// </summary>
    /// <remarks>
    /// Runs the contract's own code, so it is contained. Each value is read exactly once and kept; nothing
    /// is admitted until every declaration on the assembly has passed.
    /// </remarks>
    private static string? DeclarationDisagreement(
        Assembly assembly,
        ClientContractAssembly published,
        CancellationToken cancellationToken,
        out IReadOnlyList<VerifiedClientContract> verified)
    {
        verified = [];

        try
        {
            var resolved = ResolveDeclarations(assembly);

            if (resolved.Length != published.Declarations.Count)
            {
                return $"it declares {resolved.Length} client contract(s) once loaded; the host published "
                    + $"{published.Declarations.Count}.";
            }

            var admitted = new List<VerifiedClientContract>(resolved.Length);

            for (var index = 0; index < resolved.Length; index++)
            {
                if (Admit(assembly, resolved[index], published.Declarations[index], out var contract)
                    is { } refusal)
                {
                    return refusal;
                }

                admitted.Add(contract!);
            }

            verified = admitted;
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller abandoned the load; that is not a statement about the contract.
            throw;
        }
        catch (Exception failure) when (!ProcessFailure.IsFatal(failure))
        {
            return $"its client contract declarations could not be read once loaded: {failure.Message}";
        }
    }

    /// <summary>Resolves the exact declarations an assembly carries, in published order.</summary>
    /// <remarks>One targeted attribute read; nothing enumerates types or members.</remarks>
    private static ClientContractEntryPointAttribute[] ResolveDeclarations(Assembly assembly)
        => [.. assembly
            .GetCustomAttributes<ClientContractEntryPointAttribute>()
            .OrderBy(entry => entry.GetType().FullName, StringComparer.Ordinal)];

    /// <summary>Proves one declaration, reading each of its values exactly once.</summary>
    private static string? Admit(
        Assembly assembly,
        ClientContractEntryPointAttribute declaration,
        ClientContractDeclaration expected,
        out VerifiedClientContract? contract)
    {
        contract = null;
        var implementation = declaration.GetType();

        if (!string.Equals(implementation.FullName, expected.EntryPointType, StringComparison.Ordinal))
        {
            return $"it loaded the client contract entry point '{implementation.FullName}' where the host "
                + $"published '{expected.EntryPointType}'.";
        }

        if (!ReferenceEquals(implementation.Assembly, assembly))
        {
            return $"'{expected.EntryPointType}' is implemented in "
                + $"'{implementation.Assembly.GetName().Name}' rather than in the assembly that declared it.";
        }

        // Directly, so the four members the proof reads are the ones this assembly wrote.
        if (implementation.BaseType != typeof(ClientContractEntryPointAttribute))
        {
            return $"'{expected.EntryPointType}' does not derive directly from the client contract "
                + "declaration.";
        }

        var entityType = declaration.EntityType;

        if (!ReferenceEquals(entityType.Assembly, assembly))
        {
            return $"'{expected.EntryPointType}' resolves its entity type to "
                + $"'{entityType.Assembly.GetName().Name}' rather than to the assembly that declared it.";
        }

        if (!string.Equals(entityType.FullName, expected.EntityTypeName, StringComparison.Ordinal))
        {
            return $"'{expected.EntryPointType}' resolves its entity type to '{entityType.FullName}'; the "
                + $"host published '{expected.EntityTypeName}'.";
        }

        // Once each. A getter asked twice may answer twice, and everything below is proved about these.
        var context = declaration.SerializationContext;
        var root = declaration.EntityTypeInfo;
        var schema = declaration.Schema;

        if (context is null || root is null || schema is null)
        {
            return $"'{expected.EntryPointType}' answers null for its serialization context, its entity "
                + "metadata or its projection schema.";
        }

        if (!ReferenceEquals(context.GetType().Assembly, assembly))
        {
            return $"'{expected.EntryPointType}' serializes through a context from "
                + $"'{context.GetType().Assembly.GetName().Name}' rather than from the assembly that "
                + "declared it.";
        }

        if (root.Type != entityType)
        {
            return $"'{expected.EntryPointType}' offers metadata for '{root.Type}' as the entity metadata "
                + $"of '{entityType.FullName}'.";
        }

        // The context's own answer for that type, not merely one that describes it.
        if (!ReferenceEquals(context.GetTypeInfo(entityType), root))
        {
            return $"'{expected.EntryPointType}' offers entity metadata its own context does not hold for "
                + $"'{entityType.FullName}'.";
        }

        // Renders the whole reachable graph through that exact context, and refuses what it cannot describe.
        var serialization = ClientContractDigest.OfSerialization(context, root);

        // The schema whole, in one bounded read, before anything hashes it. Reading the root list once
        // leaves every field's components and choices as lists the contract still owns, so what was hashed
        // here and what a payload is later rendered against would be two separate reads of them.
        if (ClientContractSchema.Freeze(schema, out var admitted) is { } undescribable)
        {
            return $"'{expected.EntryPointType}' declares a projection schema this client cannot describe: "
                + undescribable.Message;
        }

        // Over the frozen copy, so the published hash covers exactly what will be rendered.
        var projection = ClientContractDigest.OfProjection(entityType, admitted!.Frozen);

        if (!string.Equals(serialization, declaration.GeneratedMetadataHash, StringComparison.Ordinal)
            || !string.Equals(projection, declaration.ProjectionSchemaHash, StringComparison.Ordinal))
        {
            return $"'{expected.EntryPointType}' does not hash to what it declares: its wire is "
                + $"{serialization} and its schema {projection}.";
        }

        if (!string.Equals(serialization, expected.GeneratedMetadataHash, StringComparison.Ordinal)
            || !string.Equals(projection, expected.ProjectionSchemaHash, StringComparison.Ordinal))
        {
            return $"'{expected.EntryPointType}' hashes to {serialization} and {projection}; the host "
                + $"published {expected.GeneratedMetadataHash} and {expected.ProjectionSchemaHash}.";
        }

        contract = new VerifiedClientContract(declaration, entityType, context, root, admitted);
        return null;
    }

    /// <summary>
    /// Describes how the bytes' declarations differ from what was published, or nothing when they do not.
    /// </summary>
    /// <remarks>
    /// Exact, in both directions and in order. A payload declaring a contract the host did not publish is a
    /// surface a browser was never told about; a payload missing one the host did publish is a host
    /// describing a build it is not serving. Both are the same failure of agreement, and neither is a case
    /// where projecting the intersection would be safe.
    /// </remarks>
    private static string? DeclarationDisagreement(ContractMetadata metadata, ClientContractAssembly published)
    {
        if (metadata.DeclarationDefect is { } defect)
        {
            return $"carries a client contract declaration this client could not read: {defect}";
        }

        var declared = metadata.Declarations;
        var expected = published.Declarations;

        if (declared.Count != expected.Count)
        {
            return $"declares {declared.Count} client contract(s); the host published {expected.Count}.";
        }

        for (var index = 0; index < declared.Count; index++)
        {
            if (declared[index] != expected[index])
            {
                return $"declares the client contract '{declared[index].EntryPointType}' as "
                    + $"({declared[index].EntityTypeName}, {declared[index].GeneratedMetadataHash}, "
                    + $"{declared[index].ProjectionSchemaHash}); the host published "
                    + $"'{expected[index].EntryPointType}' as ({expected[index].EntityTypeName}, "
                    + $"{expected[index].GeneratedMetadataHash}, {expected[index].ProjectionSchemaHash}).";
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether what this page already holds is what the host is publishing now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The declarations are part of the answer, and leaving them out was a hole rather than an omission. A
    /// reused entry skips both declaration checks — the bytes are not fetched, so there is nothing to
    /// preflight, and the commit does not load it, so the runtime is never asked again. A host could then
    /// publish different contracts, different entity types or different hashes for bytes this page had
    /// already verified, and the page would project them under the description it agreed to on a previous
    /// pass.
    /// </para>
    /// <para>
    /// Ordered, because the published order is the order both sides sorted into: two lists holding the same
    /// declarations in a different order describe two different documents, and the cheapest way to be sure
    /// they mean the same thing is to require them to be the same.
    /// </para>
    /// </remarks>
    private static bool Matches(ClientContractAssembly resident, ClientContractAssembly published)
        => string.Equals(resident.ContentHash, published.ContentHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(resident.Identity, published.Identity, StringComparison.OrdinalIgnoreCase)
            && resident.ModuleVersionId == published.ModuleVersionId
            && resident.Length == published.Length
            && resident.Declarations.SequenceEqual(published.Declarations);

    private static string Count(ClientContractAssembly assembly)
        => assembly.Declarations.Count == 1
            ? "1 declaration"
            : $"{assembly.Declarations.Count} declarations";

    private static string AddressOf(PluginId packageId, ClientContractAssembly published)
        => $"{ManifestPath}/{Uri.EscapeDataString(packageId.Value)}/{Uri.EscapeDataString(published.ContentHash)}/{Uri.EscapeDataString(published.FileName)}";

    /// <summary>Renders a refusal.</summary>
    /// <remarks>
    /// <paramref name="orphaned"/> defaults to empty: a pass that returned before the required set was
    /// known has nothing to say about what this page holds.
    /// </remarks>
    private ContractLoadReport Refused(
        ContractCompatibility compatibility,
        string? publishedIdentity,
        string? installationHash,
        IReadOnlyList<LoadedContractPackage> packages,
        IReadOnlyList<ClientContractRefusal> refused,
        string? failure,
        IReadOnlyList<OrphanedContract>? orphaned = null)
        => new(
            compatibility,
            publishedIdentity,
            ClientContractIdentity,
            installationHash,
            packages,
            refused,
            orphaned ?? [],
            _store.IsAvailable,
            failure);

    /// <summary>One admitted client contract, and the assembly that declared it.</summary>
    /// <param name="AssemblyName">The declaring assembly's simple name.</param>
    /// <param name="Contract">What the admission proof captured.</param>
    internal sealed record AdmittedContract(string AssemblyName, VerifiedClientContract Contract);

    /// <summary>The package a resident contract was last admitted under.</summary>
    /// <param name="Id">The package identifier.</param>
    /// <param name="Name">Its name, as the host stated it.</param>
    /// <param name="Version">Its version, as the host stated it.</param>
    private readonly record struct ContractOwner(PluginId Id, string Name, string Version);

    /// <summary>One contract this page has loaded, and the exact description it was verified against.</summary>
    /// <param name="Assembly">The loaded assembly.</param>
    /// <param name="Verified">What the host published for it, proved against its bytes and its runtime.</param>
    /// <param name="Contracts">What that proof captured, in published order.</param>
    /// <param name="Owner">The package that published it, so an orphan can be attributed.</param>
    /// <param name="Orphaned">Whether the installation last read stopped naming this assembly.</param>
    private sealed record ResidentContract(
        Assembly Assembly,
        ClientContractAssembly Verified,
        IReadOnlyList<VerifiedClientContract> Contracts,
        ContractOwner Owner,
        bool Orphaned = false);

    /// <summary>What verification decided about one published assembly, before anything was loaded.</summary>
    private sealed record Preflight(
        ContractLoadOutcome Outcome,
        ContractByteSource Source,
        byte[]? Content,
        int? ObservedLength,
        string? ObservedContentHash,
        string? ObservedIdentity,
        Guid? ObservedModuleVersionId,
        string? ObservedContractReference,
        IReadOnlyList<ClientContractDeclaration> ObservedDeclarations,
        string? Failure)
    {
        /// <summary>Gets whether this assembly may be handed to the runtime.</summary>
        public bool IsVerified =>
            Outcome is ContractLoadOutcome.Verified or ContractLoadOutcome.AlreadyLoaded;

        /// <summary>Records that the runtime accepted these bytes.</summary>
        public Preflight Committed() => this with { Outcome = ContractLoadOutcome.Loaded, Content = null };

        public static Preflight Verified(
            ClientContractAssembly published,
            ContractByteSource source,
            byte[] content,
            ContractMetadata metadata)
            => new(
                ContractLoadOutcome.Verified,
                source,
                content,
                content.Length,
                published.ContentHash,
                metadata.Identity,
                metadata.ModuleVersionId,
                metadata.ContractReference,
                metadata.Declarations,
                null);

        public static Preflight Reused(ClientContractAssembly published)
            => new(
                ContractLoadOutcome.AlreadyLoaded,
                ContractByteSource.Resident,
                null,
                published.Length,
                published.ContentHash,
                published.Identity,
                published.ModuleVersionId,
                ClientContractIdentity,
                published.Declarations,
                null);

        public static Preflight Failed(
            ContractLoadOutcome outcome,
            ContractByteSource source,
            string failure,
            int? observedLength = null,
            string? observedContentHash = null,
            string? observedIdentity = null,
            Guid? observedModuleVersionId = null,
            string? observedContractReference = null,
            IReadOnlyList<ClientContractDeclaration>? observedDeclarations = null)
            => new(
                outcome,
                source,
                null,
                observedLength,
                observedContentHash,
                observedIdentity,
                observedModuleVersionId,
                observedContractReference,
                observedDeclarations ?? [],
                failure);

        public static LoadedContractAssembly NotAttempted(ClientContractAssembly published)
            => new(
                published,
                ContractLoadOutcome.NotAttempted,
                ContractByteSource.None,
                null,
                null,
                null,
                null,
                null,
                [],
                null);

        public Preflight RuntimeRefused(string failure)
            => this with { Outcome = ContractLoadOutcome.RuntimeRefused, Content = null, Failure = failure };

        public LoadedContractAssembly ToView(ClientContractAssembly published)
            => new(
                published,
                Outcome,
                Source,
                ObservedLength,
                ObservedContentHash,
                ObservedIdentity,
                ObservedModuleVersionId,
                ObservedContractReference,
                ObservedDeclarations,
                Failure);
    }
}
