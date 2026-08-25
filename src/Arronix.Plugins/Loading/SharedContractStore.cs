using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;
using Microsoft.Extensions.Logging;

namespace Arronix.Plugins.Loading;

/// <summary>One shared contract assembly, loaded once for the whole installation.</summary>
/// <param name="Publisher">The package that shipped the file.</param>
/// <param name="Identity">The CLR assembly identity every publisher and dependant must bind to.</param>
/// <param name="SourcePath">The file the bytes were staged from.</param>
/// <param name="ContentHash">The SHA-256 of the staged bytes.</param>
/// <param name="ModuleVersionId">The module identifier of that exact build.</param>
/// <param name="Assembly">The one loaded assembly object every dependant must receive.</param>
internal sealed record AdmittedContract(
    PluginId Publisher,
    AssemblyIdentity Identity,
    string SourcePath,
    string ContentHash,
    Guid ModuleVersionId,
    Assembly Assembly);

/// <summary>Why a package's shared-contract declaration was refused.</summary>
/// <param name="Code">The failure class.</param>
/// <param name="Reason">The sentence an operator reads first.</param>
/// <param name="Defects">Everything wrong, in a deterministic order.</param>
internal readonly record struct SharedContractRefusal(
    CoreErrorCode Code,
    string Reason,
    ReadOnlyCollection<string> Defects);

/// <summary>What became of an installation's shared contract assemblies.</summary>
internal sealed class SharedContractAdmission
{
    internal SharedContractAdmission(
        IReadOnlyList<AdmittedContract> admitted,
        IReadOnlyDictionary<PluginId, SharedContractRefusal> refusals)
    {
        Admitted = admitted.ToList().AsReadOnly();
        Refusals = refusals.ToFrozenDictionary();
    }

    /// <summary>Gets the contracts that were loaded, in graph order.</summary>
    public ReadOnlyCollection<AdmittedContract> Admitted { get; }

    /// <summary>Gets the packages whose declarations were refused, and why.</summary>
    public FrozenDictionary<PluginId, SharedContractRefusal> Refusals { get; }
}

/// <summary>
/// One package's view of the installation's shared contracts: what it published, what it may bind to, and
/// its releasable hold on the contract context.
/// </summary>
/// <remarks>
/// <para>
/// Global admission is not global visibility. The scope resolves only contracts published by this package
/// or by a package in its exact transitive dependency closure, so a package cannot bind to a contract it
/// never declared a dependency on merely because some other package published that simple name.
/// </para>
/// <para>
/// The scope is the receipt as well as the resolver. It is released by reference, so a reload that produced
/// a second attempt for one identifier cannot release the first attempt's hold.
/// </para>
/// </remarks>
internal sealed class PackageContractScope
{
    private readonly SharedContractStore? _store;
    private readonly FrozenDictionary<string, AdmittedContract> _visible;
    private readonly Lock _scopeGate = new();
    private bool _released;

    private readonly FrozenDictionary<string, PluginId> _admittedElsewhere;

    internal PackageContractScope(
        SharedContractStore? store,
        PluginId package,
        IReadOnlyList<AdmittedContract> published,
        IReadOnlyDictionary<string, AdmittedContract> visible,
        IReadOnlyDictionary<string, PluginId> admittedElsewhere)
    {
        _store = store;
        Package = package;
        Published = published.ToList().AsReadOnly();
        _visible = visible.ToFrozenDictionary(StagedAssembly.NameComparer);
        _admittedElsewhere = admittedElsewhere.ToFrozenDictionary(StagedAssembly.NameComparer);
    }

    /// <summary>
    /// A scope over nothing, for a caller that deliberately shares no contracts.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <returns>The scope. It holds nothing and releasing it does nothing.</returns>
    /// <remarks>
    /// A stated empty authority rather than an absent one, so a load context always has a scope to consult
    /// and "no contracts" is something a caller says rather than something a null means.
    /// </remarks>
    internal static PackageContractScope Empty(PluginId package)
        => new(
            store: null,
            package,
            published: [],
            visible: new Dictionary<string, AdmittedContract>(StagedAssembly.NameComparer),
            admittedElsewhere: new Dictionary<string, PluginId>(StagedAssembly.NameComparer));

    /// <summary>Gets the package this scope was issued to.</summary>
    public PluginId Package { get; }

    /// <summary>Gets the contracts this package published, in declaration order.</summary>
    public ReadOnlyCollection<AdmittedContract> Published { get; }

    /// <summary>Gets a value indicating whether the hold is still held.</summary>
    public bool IsHeld
    {
        get
        {
            lock (_scopeGate)
            {
                return !_released;
            }
        }
    }

    /// <summary>Gets the contract simple names visible to this package, ordered, for diagnostics.</summary>
    public IReadOnlyList<string> VisibleNames
        => [.. _visible.Values.Select(contract => contract.Identity.Name!).Order(StringComparer.Ordinal)];

    /// <summary>
    /// Returns the one loaded assembly for a contract this package may bind to.
    /// </summary>
    /// <param name="assemblyName">The identity being requested.</param>
    /// <returns>The shared assembly, or <see langword="null"/> when the name is outside this scope.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assemblyName"/> is <see langword="null"/>.</exception>
    /// <exception cref="SharedContractIdentityException">
    /// The name is visible, but the requested identity is not the admitted one.
    /// </exception>
    /// <exception cref="InvalidOperationException">The hold has already been released.</exception>
    public Assembly? Resolve(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        // The gate orders this against Release: a release cannot complete while a resolve is in flight, and
        // no resolve can begin once one has.
        lock (_scopeGate)
        {
            if (_released)
            {
                throw new InvalidOperationException(
                    $"Package '{Package}' asked for a shared contract after its contract hold was released. A "
                    + "released hold serves nothing: its types may already be unloadable.");
            }

            if (assemblyName.Name is not { } name)
            {
                return null;
            }

            if (!_visible.TryGetValue(name, out var contract))
            {
                // An admitted contract this package did not declare a dependency on is refused rather than
                // resolved from its own payload or the default context. Returning null here is what would
                // give one installation two identities for one shared type.
                if (_admittedElsewhere.TryGetValue(name, out var publisher))
                {
                    throw new PluginIsolationException(
                        $"Package '{Package}' requested shared contract '{name}', which this installation "
                        + $"admitted from '{publisher}'. Package '{Package}' does not depend on it: global "
                        + "admission is not global visibility.");
                }

                return null;
            }

            if (!StagedAssembly.SameIdentity(contract.Identity, assemblyName))
            {
                throw new SharedContractIdentityException(
                    StagedAssembly.Describe(AssemblyIdentity.From(assemblyName)),
                    StagedAssembly.Describe(contract.Identity),
                    contract.Publisher.ToString());
            }

            return contract.Assembly;
        }
    }

    /// <summary>Releases exactly this hold. Releasing twice does nothing.</summary>
    public void Release()
    {
        bool first;

        lock (_scopeGate)
        {
            first = !_released;
            _released = true;
        }

        // Outside this scope's gate so the store's own lock is never taken beneath it.
        if (first)
        {
            _store?.ReleaseScope(this);
        }
    }
}

/// <summary>
/// The lifecycle of the installation's one shared contract context.
/// </summary>
/// <remarks>
/// Admission and release are mutually exclusive and each happens once. The state is changed under the
/// store's gate before any external code runs, so no caller can slip a hold into the gap between deciding
/// to release and releasing.
/// </remarks>
internal enum SharedContractState
{
    /// <summary>Nothing has been admitted yet.</summary>
    Fresh = 0,

    /// <summary>Admission is in progress on another call.</summary>
    Admitting = 1,

    /// <summary>The installation's contracts are admitted and serving.</summary>
    Active = 2,

    /// <summary>Release has been decided and the unload request is in flight.</summary>
    Unloading = 3,

    /// <summary>Unload has been requested. The context can never serve another load.</summary>
    Released = 4,

    /// <summary>Admission or release failed. The store is terminal and reports why.</summary>
    Failed = 5
}

/// <summary>
/// The installation's shared contract assemblies: one collectible context, one loaded copy each, and the
/// rule that every publisher and every dependant in its closure receives that exact copy.
/// </summary>
/// <remarks>
/// <para>
/// Type identity is a function of assembly identity and load context and of nothing else, so sharing means
/// handing every requester the same <see cref="Assembly"/> object. Matching names, versions or bytes do not
/// produce it, and the failure when they are relied on is silent: a type check returns false and the
/// operator is told the package contributed nothing.
/// </para>
/// <para>
/// The store is required production infrastructure. An installation that shares nothing has an empty
/// admitted set; it does not have an absent authority.
/// </para>
/// </remarks>
internal sealed class SharedContractStore
{
    /// <summary>The name the installation's one shared contract context carries.</summary>
    public const string ContextName = "arronix-shared-contracts";

    private readonly Dictionary<string, AdmittedContract> _admitted = new(StagedAssembly.NameComparer);
    private readonly List<AdmittedContract> _live = [];
    private readonly HashSet<PackageContractScope> _scopes = new(ReferenceEqualityComparer.Instance);
    private readonly ILogger? _log;
    private readonly Lock _gate = new();
    private SharedContractLoadContext? _context;
    private SharedContractAdmission? _admission;
    private ResolvedPackageGraph? _graph;
    private SharedContractState _state = SharedContractState.Fresh;
    private string? _unloadFailure;

    /// <summary>Initializes a new instance of the <see cref="SharedContractStore"/> class.</summary>
    /// <param name="log">The host's own diagnostics.</param>
    internal SharedContractStore(ILogger? log = null) => _log = log;

    /// <summary>Gets the contracts this installation currently shares, in admission order.</summary>
    /// <remarks>
    /// What is loaded now, not what was once loaded. Releasing the context empties this, because a released
    /// context can never serve another dependant.
    /// </remarks>
    public ReadOnlyCollection<AdmittedContract> Admitted
    {
        get
        {
            lock (_gate)
            {
                return _live.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>Gets the number of admitted shared contracts.</summary>
    public int AdmittedCount
    {
        get
        {
            lock (_gate)
            {
                return _admitted.Count;
            }
        }
    }

    /// <summary>Gets a value indicating whether unload has been requested for the shared context.</summary>
    /// <remarks>
    /// Requested, never completed. <see cref="AssemblyLoadContext.Unload"/> asks the runtime to release the
    /// context once nothing refers to it; it frees nothing while a dependant holds a type and does not fail
    /// while one does. Collection is never reported.
    /// </remarks>
    public bool UnloadRequested
    {
        get
        {
            lock (_gate)
            {
                return _state == SharedContractState.Released;
            }
        }
    }

    /// <summary>Gets the store's lifecycle state.</summary>
    public SharedContractState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Gets the packages currently holding a scope, ordered by identifier.</summary>
    public IReadOnlyList<PluginId> Holders
    {
        get
        {
            lock (_gate)
            {
                return [.. _scopes.Select(scope => scope.Package).OrderBy(id => id.Value, StringComparer.Ordinal)];
            }
        }
    }

    /// <summary>
    /// Requests release of the shared contract context.
    /// </summary>
    /// <param name="refusal">Why release was refused, or <see langword="null"/> when it was not.</param>
    /// <returns><see langword="true"/> when unload was requested.</returns>
    /// <remarks>
    /// The runtime enforces nothing here, so the refusal is the platform's. Unloading while a dependant
    /// still holds types releases nothing and permanently stops the context serving another load.
    /// </remarks>
    public bool TryRequestUnload(out string? refusal)
    {
        SharedContractLoadContext? context;

        lock (_gate)
        {
            switch (_state)
            {
                case SharedContractState.Released:
                    refusal = null;
                    return true;

                case SharedContractState.Failed:
                    refusal = _unloadFailure ?? "The shared contract context is in a terminal failed state.";
                    return false;

                case SharedContractState.Unloading:
                    refusal = "The shared contract context is already being released.";
                    return false;

                case SharedContractState.Admitting:
                    refusal = "The installation's shared contracts are still being admitted.";
                    return false;

                case SharedContractState.Fresh:
                    _state = SharedContractState.Released;
                    refusal = null;
                    return true;

                default:
                    break;
            }

            if (_scopes.Count > 0)
            {
                var holders = string.Join(
                    ", ",
                    _scopes.Select(scope => scope.Package.Value).OrderBy(value => value, StringComparer.Ordinal));

                refusal = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The shared contract context cannot be released: {_scopes.Count} active dependant(s) still hold its types ({holders}). Dependants withdraw first.");
                return false;
            }

            // Entered before the external call, so no scope can be opened in the gap and no second caller
            // can decide to release the same context.
            _state = SharedContractState.Unloading;
            context = _context;
        }

        try
        {
            // Outside the gate: an Unloading handler is code the installation's packages registered, and
            // running it while holding the registry lock would let a third party stall every other caller.
            context?.Unload();
        }
        // An external-callback boundary, not the staged-file one: an Unloading handler is code a package
        // registered and may throw any type it likes, so the file boundary's closed allowlist would let one
        // escape and skip the terminal-state transition below. Only a process-fatal condition propagates.
#pragma warning disable CA1031
        catch (Exception failure) when (LoadFailurePolicy.IsContainablePackageFailure(failure))
#pragma warning restore CA1031
        {
            refusal = $"The shared contract context could not be released: {failure.Message}";

            lock (_gate)
            {
                _state = SharedContractState.Failed;
                _unloadFailure = refusal;
            }

            return false;
        }

        lock (_gate)
        {
            _state = SharedContractState.Released;
            _admitted.Clear();
            _live.Clear();
            _context = null;
        }

        refusal = null;
        return true;
    }

    /// <summary>
    /// Loads the installation's shared contracts, once, in graph order, from bytes staged before anything
    /// executes.
    /// </summary>
    /// <param name="graph">The resolved package graph. Its admission order is the staging order.</param>
    /// <returns>What was admitted, and which packages were refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    internal SharedContractAdmission Admit(ResolvedPackageGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        lock (_gate)
        {
            switch (_state)
            {
                case SharedContractState.Fresh:
                    _state = SharedContractState.Admitting;
                    break;

                case SharedContractState.Active when ReferenceEquals(_graph, graph):
                    return _admission!;

                case SharedContractState.Active when SharesNothing(graph):
                    // The only second graph an admitted store will take. Nothing was loaded, nothing was
                    // refused, there is no context, and the new graph declares no contract assembly either,
                    // so there is no admitted state for it to borrow. Anything else needs a real
                    // reconciliation of staged bytes, identities and closures.
                    _graph = graph;
                    return _admission!;

                case SharedContractState.Active:
                    throw new InvalidOperationException(
                        "The installation's shared contracts were admitted from a different resolved graph. "
                        + "A second graph may reuse an admission only when neither shares anything: matching "
                        + "identifiers, versions and file names do not prove matching bytes, identities or "
                        + "dependency closures.");

                case SharedContractState.Admitting:
                    throw new InvalidOperationException(
                        "The installation's shared contracts are already being admitted. Admission runs once "
                        + "per load pass and is not re-entrant.");

                default:
                    throw new InvalidOperationException(
                        "The installation's shared contract context has been released or failed; it can never "
                        + "admit again.");
            }
        }

        SharedContractAdmission admission;
        SharedContractLoadContext? surviving;

        try
        {
            // Staging, metadata validation and the provisional loads all happen outside the gate. Loading an
            // assembly raises AssemblyLoad and Resolving, and discarding a provisional context raises
            // Unloading; all three run code this process did not write.
            admission = AdmitGraph(graph, out surviving);
        }
        catch
        {
            lock (_gate)
            {
                _state = SharedContractState.Failed;
                _unloadFailure = "The installation's shared contracts could not be admitted.";
            }

            throw;
        }

        lock (_gate)
        {
            _graph = graph;
            _context = surviving;

            foreach (var contract in admission.Admitted)
            {
                _admitted[contract.Identity.Name] = contract;
                _live.Add(contract);
            }

            _admission = admission;
            _state = SharedContractState.Active;
        }

        foreach (var contract in admission.Admitted)
        {
            SharedContractLog.Admitted(
                _log,
                StagedAssembly.Describe(contract.Identity),
                contract.Publisher.ToString(),
                contract.ContentHash);
        }

        return admission;
    }

    /// <summary>
    /// Determines whether an admitted store holds nothing and the new graph asks for nothing.
    /// </summary>
    /// <remarks>
    /// Deliberately not a comparison of the two graphs. Equal identifiers, versions and file names do not
    /// prove equal bytes, equal CLR identities or equal dependency closures — and closures are what decide
    /// which package may see which contract — so a graph that merely looks the same must never adopt
    /// another graph's loaded assemblies.
    /// </remarks>
    private bool SharesNothing(ResolvedPackageGraph graph)
        => _admission is { Admitted.Count: 0, Refusals.Count: 0 }
            && _context is null
            && _live.Count == 0
            && _admitted.Count == 0
            && graph.AdmissionOrder.All(package => package.ContractAssemblies.Count == 0);

    /// <summary>
    /// Opens one package's scope over the contracts it published and those its closure published.
    /// </summary>
    /// <param name="package">The package being admitted.</param>
    /// <returns>The scope, which is also this package's releasable hold on the contract context.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="package"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Admission has not run.</exception>
    internal PackageContractScope OpenScope(InstalledPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        lock (_gate)
        {
            if (_state != SharedContractState.Active)
            {
                throw new InvalidOperationException(
                    $"Package '{package.Id}' asked for a contract scope while the installation's shared "
                    + $"contract context was {_state}. A scope is issued only while it is serving.");
            }

            var graph = _graph!;

            // The exact object, not an identifier and not a structurally equal clone. Two installation
            // attempts of one package are precisely what a reload produces, and the incumbent's visibility
            // must not be handed to the challenger.
            if (!graph.TryGet(package.Id, out var admitted) || !ReferenceEquals(admitted, package))
            {
                throw new InvalidOperationException(
                    $"Package '{package.Id}' is not the exact installed package this installation resolved. "
                    + "A contract scope belongs to one admitted package object.");
            }

            if (_admission is not null && _admission.Refusals.ContainsKey(package.Id))
            {
                throw new InvalidOperationException(
                    $"Package '{package.Id}' was refused by shared-contract admission and has no scope.");
            }

            var closure = graph.ClosureOf(package.Id);
            var visible = new Dictionary<string, AdmittedContract>(StagedAssembly.NameComparer);
            var elsewhere = new Dictionary<string, PluginId>(StagedAssembly.NameComparer);
            var published = new List<AdmittedContract>();

            foreach (var contract in _live)
            {
                if (contract.Publisher == package.Id)
                {
                    published.Add(contract);
                    visible[contract.Identity.Name] = contract;
                }
                else if (closure.Contains(contract.Publisher))
                {
                    visible[contract.Identity.Name] = contract;
                }
                else
                {
                    elsewhere[contract.Identity.Name] = contract.Publisher;
                }
            }

            var scope = new PackageContractScope(this, package.Id, published, visible, elsewhere);
            _scopes.Add(scope);
            return scope;
        }
    }

    internal void ReleaseScope(PackageContractScope scope)
    {
        lock (_gate)
        {
            _scopes.Remove(scope);
        }
    }

    /// <summary>
    /// Checks a package's files against everything the installation shares, before it is loaded.
    /// </summary>
    /// <param name="package">The package about to be loaded.</param>
    /// <param name="code">The failure class when the package is refused.</param>
    /// <param name="defects">Everything wrong, in a deterministic order.</param>
    /// <returns><see langword="true"/> when the package may be loaded.</returns>
    /// <remarks>
    /// The duplicate rule is installation-wide rather than closure-scoped on purpose. A private copy of an
    /// admitted contract is a second CLR identity for a shared type whatever the package declared, and the
    /// failure it causes surfaces somewhere else entirely.
    /// </remarks>
    internal bool TryCheckPackage(InstalledPackage package, out CoreErrorCode code, out IReadOnlyList<string> defects)
    {
        code = CoreErrorCode.PluginIsolationViolation;
        defects = [];

        FrozenSet<PluginId> closure;
        FrozenDictionary<string, AdmittedContract> admittedNow;
        FrozenSet<string> admittedSources;

        // One snapshot for the whole walk. Reading the live map per file would let a release running
        // concurrently erase half a verdict, so half the package's files would be checked against an
        // installation that shares something and half against one that shares nothing.
        lock (_gate)
        {
            if (_state != SharedContractState.Active)
            {
                throw new InvalidOperationException(
                    $"Package '{package.Id}' was checked against the installation's shared contracts while "
                    + $"the contract context was {_state}.");
            }

            if (_admitted.Count == 0)
            {
                return true;
            }

            closure = _graph!.ClosureOf(package.Id);
            admittedNow = _admitted.ToFrozenDictionary(StagedAssembly.NameComparer);
            admittedSources = _admitted.Values
                .Select(contract => contract.SourcePath)
                .ToFrozenSet(StringComparer.Ordinal);
        }

        IReadOnlyList<string> files;

        try
        {
            files = ManagedFiles(package.Folder);
        }
// Listing a package's own folder is file-boundary work on a path that package controls, so it uses the
// closed allowlist. Containing it here is what keeps one unreadable folder from ending the load pass: this
// check runs before the per-package try, so an escaping failure would abort every package not yet attempted
// rather than refusing the one whose folder cannot be read.
#pragma warning disable CA1031
        catch (Exception failure) when (LoadFailurePolicy.IsContainableContractFailure(failure))
#pragma warning restore CA1031
        {
            code = CoreErrorCode.PluginLoadFailure;
            defects =
            [
                $"The managed files in '{package.Folder}' could not be listed, so this package cannot be "
                + $"checked against the contracts this installation shares: {failure.Message}",
            ];
            return false;
        }

        var duplicates = new List<string>();
        var mismatches = new List<string>();
        var undeclared = new List<string>();

        foreach (var file in files)
        {
            if (admittedSources.Contains(file))
            {
                continue;
            }

            if (!StagedAssembly.TryStage(file, out var staged, out _))
            {
                // Native libraries and unreadable files are the private resolver's problem, not identity's.
                continue;
            }

            var fileName = Path.GetFileName(file);

            if (admittedNow.TryGetValue(staged!.Identity.Name, out var shadowed))
            {
                duplicates.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{fileName}' is a private copy of shared contract '{StagedAssembly.Describe(shadowed.Identity)}' published by '{shadowed.Publisher}'. The private copy is MVID {staged.ModuleVersionId} SHA-256 {staged.ContentHash}; the admitted copy is MVID {shadowed.ModuleVersionId} SHA-256 {shadowed.ContentHash}. A package consumes a shared contract through its dependency, never by carrying it."));
            }

            foreach (var reference in staged.References)
            {
                if (!admittedNow.TryGetValue(reference.Name, out var admitted))
                {
                    continue;
                }

                if (!StagedAssembly.SameIdentity(admitted.Identity, reference))
                {
                    mismatches.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{fileName}' requires '{StagedAssembly.Describe(reference)}', but this installation admitted '{StagedAssembly.Describe(admitted.Identity)}' from '{admitted.Publisher}' (MVID {admitted.ModuleVersionId} SHA-256 {admitted.ContentHash}). The requesting file is MVID {staged.ModuleVersionId} SHA-256 {staged.ContentHash}."));
                    continue;
                }

                // Exact identity is not permission. A package binds to the contracts it declared a
                // dependency on; one that references an admitted contract outside its closure would
                // otherwise be caught only when the runtime asked for the name, which is late and silent.
                if (admitted.Publisher != package.Id && !closure.Contains(admitted.Publisher))
                {
                    undeclared.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{fileName}' requires '{StagedAssembly.Describe(reference)}', published by '{admitted.Publisher}' (MVID {admitted.ModuleVersionId} SHA-256 {admitted.ContentHash}), which package '{package.Id}' does not depend on. Declare the dependency, or stop referencing the contract."));
                }
            }
        }

        if (duplicates.Count > 0)
        {
            code = CoreErrorCode.PluginIsolationViolation;
            defects = [.. duplicates, .. undeclared, .. mismatches];
            return false;
        }

        if (undeclared.Count > 0)
        {
            code = CoreErrorCode.PluginIsolationViolation;
            defects = [.. undeclared, .. mismatches];
            return false;
        }

        if (mismatches.Count > 0)
        {
            code = CoreErrorCode.PluginContractMismatch;
            defects = mismatches.AsReadOnly();
            return false;
        }

        return true;
    }

    /// <summary>The managed files a package carries, ordered so every diagnostic is reproducible.</summary>
    /// <remarks>
    /// Enumeration is materialized rather than returned lazily, so a folder that cannot be listed fails at
    /// the one call the caller contains rather than partway through a walk it has already started reporting
    /// on.
    /// </remarks>
    private static IReadOnlyList<string> ManagedFiles(string folder)
        => Directory.Exists(folder)
            ? [.. Directory
                .EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)]
            : [];

    /// <summary>
    /// Stages and validates every eligible package's contracts in graph order, then loads the accepted set
    /// as one transaction over a provisional context.
    /// </summary>
    /// <remarks>
    /// Admission cannot be made atomic by withholding entries from a dictionary. A load context keeps its
    /// own binding cache and that cache answers before any resolver runs, so an assembly loaded into the
    /// live context and left out of the admitted map is still there and still answers its own name. The
    /// transaction is therefore the context itself.
    /// </remarks>
    private SharedContractAdmission AdmitGraph(
        ResolvedPackageGraph graph,
        out SharedContractLoadContext? surviving)
    {
        var refusals = new Dictionary<PluginId, SharedContractRefusal>();
        var planned = new List<PlannedPublisher>();
        var declared = new Dictionary<string, (AssemblyIdentity Identity, PluginId Publisher)>(StagedAssembly.NameComparer);

        foreach (var package in graph.AdmissionOrder)
        {
            // A package whose dependency was already refused is refused with it, and is never planned:
            // dependants come later in graph order, so the refusal recorded when the dependency failed is
            // what keeps them out of the plan.
            if (refusals.ContainsKey(package.Id) || package.ContractAssemblies.Count == 0)
            {
                continue;
            }

            if (!TryPlanPublisher(package, graph, declared, out var candidate, out var code, out var defects))
            {
                refusals[package.Id] = Refusal(package.Id, code, defects);
                RefuseDependants(graph, planned, refusals, package.Id, FrozenSet<string>.Empty);
                continue;
            }

            planned.Add(candidate!);

            foreach (var contract in candidate!.Contracts)
            {
                declared[contract.Staged.Identity.Name!] = (contract.Staged.Identity, package.Id);
            }
        }

        var admitted = LoadTransactionally(graph, planned, refusals, out surviving);

        return new SharedContractAdmission(admitted, refusals);
    }

    /// <summary>
    /// Loads the planned set into a provisional context, retrying without a failed publisher and everything
    /// that bound to it, until one complete set loads or nothing is left.
    /// </summary>
    private static IReadOnlyList<AdmittedContract> LoadTransactionally(
        ResolvedPackageGraph graph,
        List<PlannedPublisher> planned,
        Dictionary<PluginId, SharedContractRefusal> refusals,
        out SharedContractLoadContext? surviving)
    {
        surviving = null;

        while (planned.Count > 0)
        {
            var provisional = new SharedContractLoadContext();

            if (TryLoadAll(provisional, planned, out var loaded, out var failed, out var loadDefect))
            {
                surviving = provisional;
                return loaded;
            }

            // Every assembly this context loaded goes with it, including the failed publisher's siblings.
            // Leaving it live would leave those siblings answering their own names from its binding cache,
            // where no map of ours governs them.
            provisional.Unload();

            refusals[failed!.Publisher] = Refusal(failed.Publisher, CoreErrorCode.PluginLoadFailure, [loadDefect!]);
            planned.Remove(failed);

            RefuseDependants(
                graph,
                planned,
                refusals,
                failed.Publisher,
                new HashSet<string>(failed.OwnedNames, StagedAssembly.NameComparer));
        }

        return [];
    }

    /// <summary>
    /// Refuses every package that requires a refused one, over the declared package edges.
    /// </summary>
    /// <remarks>
    /// The declared dependency is the semantic edge, so a dependant is refused whether or not its own
    /// contract bytes happen to name the refused package's assemblies, and whether or not it publishes any
    /// contract of its own. Admitting a dependant's contracts after its dependency was refused would leave
    /// inert bytes in the installation's context, able to conflict on a name or to satisfy another
    /// contract's closure. Any assembly names it genuinely lost are reported as secondary detail.
    /// </remarks>
    private static void RefuseDependants(
        ResolvedPackageGraph graph,
        List<PlannedPublisher> planned,
        Dictionary<PluginId, SharedContractRefusal> refusals,
        PluginId cause,
        IReadOnlySet<string> lost)
    {
        foreach (var dependant in graph.DependantsOf(cause).OrderBy(id => id, PackageIdentity.Order))
        {
            if (refusals.ContainsKey(dependant))
            {
                continue;
            }

            var defects = new List<string>
            {
                $"Package '{cause}' could not publish the shared contracts it declares, and package "
                + $"'{dependant}' requires it. A package is not admitted against a dependency that is not there.",
            };

            var candidate = planned.Find(publisher => publisher.Publisher == dependant);

            if (candidate is not null)
            {
                var bound = candidate.ConsumedNames.Where(lost.Contains).Order(StringComparer.Ordinal).ToArray();

                if (bound.Length > 0)
                {
                    defects.Add(
                        $"Its contracts also bind to {string.Join(", ", bound.Select(name => $"'{name}'"))}, "
                        + "which this installation could not admit.");
                }

                planned.Remove(candidate);
            }

            // A dependant's headline is that its dependency's contracts were refused. Reusing the
            // publisher summary would tell a package with no contract assemblies at all that it failed to
            // publish one.
            refusals[dependant] = new SharedContractRefusal(
                CoreErrorCode.PluginDependencyUnavailable,
                $"Extension '{dependant}' requires package '{cause}', whose shared contracts this installation could not admit.",
                defects.AsReadOnly());
        }
    }

    private static bool TryLoadAll(
        SharedContractLoadContext provisional,
        IReadOnlyList<PlannedPublisher> planned,
        out IReadOnlyList<AdmittedContract> loaded,
        out PlannedPublisher? failed,
        out string? defect)
    {
        var contracts = new List<AdmittedContract>();

        foreach (var publisher in planned)
        {
            foreach (var contract in publisher.Contracts)
            {
                Assembly assembly;

                try
                {
                    assembly = provisional.Add(contract.Staged);
                }
// Staging and loading a file is a bounded operation, so this boundary uses the closed allowlist: a failure
// type it does not name is not one this platform knows how to contain, and admitting the rest of an
// installation after it would be guesswork.
#pragma warning disable CA1031
                catch (Exception failure) when (LoadFailurePolicy.IsContainableContractFailure(failure))
#pragma warning restore CA1031
                {
                    loaded = [];
                    failed = publisher;
                    defect = LoadDefect(contract, failure);
                    return false;
                }

                contracts.Add(new AdmittedContract(
                    publisher.Publisher,
                    contract.Staged.Identity,
                    contract.Path,
                    contract.Staged.ContentHash,
                    contract.Staged.ModuleVersionId,
                    assembly));
            }
        }

        loaded = contracts.AsReadOnly();
        failed = null;
        defect = null;
        return true;
    }

    private static string LoadDefect(PlannedContract contract, Exception failure)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"'{contract.FileName}' passed inspection as '{StagedAssembly.Describe(contract.Staged.Identity)}' but could not be loaded: {failure.Message}");

    private static SharedContractRefusal Refusal(PluginId publisher, CoreErrorCode code, IReadOnlyList<string> defects)
        => new(
            code,
            $"Extension '{publisher}' could not publish the shared contract it declares.",
            defects.ToList().AsReadOnly());

    /// <summary>
    /// Stages and validates every contract one package declares, without loading any of them.
    /// </summary>
    /// <remarks>
    /// Every defect is reported rather than the first; the first failing rule decides the failure class.
    /// </remarks>
    private bool TryPlanPublisher(
        InstalledPackage package,
        ResolvedPackageGraph graph,
        IReadOnlyDictionary<string, (AssemblyIdentity Identity, PluginId Publisher)> declared,
        out PlannedPublisher? planned,
        out CoreErrorCode code,
        out IReadOnlyList<string> defects)
    {
        planned = null;

        var closure = graph.ClosureOf(package.Id);
        var contracts = new List<PlannedContract>(package.ContractAssemblies.Count);
        var pending = new Dictionary<string, AssemblyIdentity>(StagedAssembly.NameComparer);
        var consumed = new HashSet<string>(StagedAssembly.NameComparer);
        var failures = new List<string>();
        var firstCode = (CoreErrorCode?)null;

        foreach (var fileName in package.ContractAssemblies)
        {
            if (!TryValidateOne(fileName, package, closure, declared, pending, consumed, out var staged, out var candidateCode, out var defect))
            {
                firstCode ??= candidateCode;
                failures.Add(defect!);
                continue;
            }

            pending[staged!.Identity.Name!] = staged.Identity;
            contracts.Add(new PlannedContract(fileName, staged, Path.Combine(package.Folder, fileName)));
        }

        if (failures.Count > 0)
        {
            code = firstCode ?? CoreErrorCode.PluginLoadFailure;
            defects = failures.AsReadOnly();
            return false;
        }

        planned = new PlannedPublisher(
            package.Id,
            contracts.AsReadOnly(),
            [.. contracts.Select(contract => contract.Staged.Identity.Name!)],
            [.. consumed]);
        code = CoreErrorCode.PluginLoadFailure;
        defects = [];
        return true;
    }

    private bool TryValidateOne(
        string fileName,
        InstalledPackage package,
        FrozenSet<PluginId> closure,
        IReadOnlyDictionary<string, (AssemblyIdentity Identity, PluginId Publisher)> declared,
        IReadOnlyDictionary<string, AssemblyIdentity> pending,
        HashSet<string> consumed,
        out StagedAssembly? candidate,
        out CoreErrorCode code,
        out string? defect)
    {
        candidate = null;
        code = CoreErrorCode.PluginLoadFailure;
        defect = null;

        var path = Path.Combine(package.Folder, fileName);

        if (!StagedAssembly.TryStage(path, out var staged, out var error))
        {
            defect = $"'{fileName}' could not be staged: {error}";
            return false;
        }

        var simpleName = staged!.Identity.Name;

        if (string.IsNullOrEmpty(simpleName))
        {
            defect = $"'{fileName}' declares no assembly name.";
            return false;
        }

        if (PluginLoadContext.IsBlocked(simpleName)
            || PluginLoadContext.IsHostContract(simpleName)
            || PluginLoadContext.IsSharedFramework(simpleName))
        {
            code = CoreErrorCode.PluginIsolationViolation;
            defect =
                $"'{simpleName}' is a host or framework assembly. Those already resolve to the host's own instance and must not be republished as a package contract.";
            return false;
        }

        if (staged.Identity.Version == new Version(0, 0, 0, 0))
        {
            code = CoreErrorCode.PluginContractMismatch;
            defect =
                $"'{simpleName}' declares no assembly version. A shared contract binds by exact CLR identity, so its AssemblyVersion is the compatibility identity and must be declared and kept stable.";
            return false;
        }

        if (staged.HasModuleInitializer)
        {
            code = CoreErrorCode.PluginIsolationViolation;
            defect =
                $"'{simpleName}' carries a module initializer, which the runtime runs as part of loading the assembly. A shared contract loads once for the whole installation into a context nothing may unload while a dependant lives; it may carry pure owner semantics, never code that runs because it was loaded.";
            return false;
        }

        if (staged.HasEntryPoint)
        {
            code = CoreErrorCode.PluginIsolationViolation;
            defect = $"'{simpleName}' declares a managed entry point. A shared contract is not an executable facet.";
            return false;
        }

        if (TryFindDeclared(simpleName, declared, pending, out var existing, out var existingOwner))
        {
            code = CoreErrorCode.PluginIdConflict;
            defect = string.Equals(existing.Name, simpleName, StringComparison.Ordinal)
                ? $"'{simpleName}' is already published{existingOwner} as '{StagedAssembly.Describe(existing)}'. One installation admits exactly one copy of a shared contract."
                : $"'{simpleName}' differs only in letter case from '{existing.Name}', already published{existingOwner}. Assembly simple names bind case-insensitively, so both would answer the same request and neither can be the one this installation shares.";
            return false;
        }

        foreach (var reference in staged.References)
        {
            var referenceName = reference.Name;

            if (PluginLoadContext.IsBlocked(referenceName))
            {
                code = CoreErrorCode.PluginIsolationViolation;
                defect = $"'{simpleName}' references forbidden implementation assembly '{referenceName}'.";
                return false;
            }

            if (PluginLoadContext.IsHostContract(referenceName)
                || PluginLoadContext.IsSharedFramework(referenceName))
            {
                continue;
            }

            var dependency = default(AssemblyIdentity);
            var owner = string.Empty;
            PluginId? publisher = null;
            var published = referenceName is not null
                && TryFindDeclared(referenceName, declared, pending, out dependency, out owner, out publisher);

            // Publisher is null for this package's own earlier declarations, which are always visible to it.
            if (published && (publisher is null || publisher == package.Id || closure.Contains(publisher.Value)))
            {
                if (!StagedAssembly.SameIdentity(dependency, reference))
                {
                    code = CoreErrorCode.PluginContractMismatch;
                    defect =
                        $"'{simpleName}' requires '{StagedAssembly.Describe(reference)}', but this installation admitted '{StagedAssembly.Describe(dependency)}'{owner}.";
                    return false;
                }

                if (!pending.ContainsKey(referenceName!))
                {
                    // Consumed from another package, so this package cannot be admitted without it.
                    consumed.Add(referenceName!);
                }

                continue;
            }

            code = CoreErrorCode.PluginIsolationViolation;
            defect = published
                ? $"'{simpleName}' references '{referenceName}', published{owner}, which is outside package '{package.Id}'s declared dependency closure. Global admission is not global visibility: declare the dependency, or stop referencing the contract."
                : $"'{simpleName}' references '{referenceName}', which is neither the host contract assembly, the shared framework, nor a contract published by this package or one it depends on.";
            return false;
        }

        candidate = staged;
        return true;
    }

    /// <summary>Finds a name among planned contracts and this package's pending ones.</summary>
    private bool TryFindDeclared(
        string simpleName,
        IReadOnlyDictionary<string, (AssemblyIdentity Identity, PluginId Publisher)> declared,
        IReadOnlyDictionary<string, AssemblyIdentity> pending,
        out AssemblyIdentity identity,
        out string owner)
        => TryFindDeclared(simpleName, declared, pending, out identity, out owner, out _);

    private bool TryFindDeclared(
        string simpleName,
        IReadOnlyDictionary<string, (AssemblyIdentity Identity, PluginId Publisher)> declared,
        IReadOnlyDictionary<string, AssemblyIdentity> pending,
        out AssemblyIdentity identity,
        out string owner,
        out PluginId? publisher)
    {
        if (pending.TryGetValue(simpleName, out var own))
        {
            identity = own;
            owner = " by this package";
            publisher = null;
            return true;
        }

        if (declared.TryGetValue(simpleName, out var planned))
        {
            identity = planned.Identity;
            owner = $" by '{planned.Publisher}'";
            publisher = planned.Publisher;
            return true;
        }

        lock (_gate)
        {
            if (_admitted.TryGetValue(simpleName, out var committed))
            {
                identity = committed.Identity;
                owner = $" by '{committed.Publisher}'";
                publisher = committed.Publisher;
                return true;
            }
        }

        identity = default;
        owner = string.Empty;
        publisher = null;
        return false;
    }

    /// <summary>One contract a package declares, staged and validated but not yet loaded.</summary>
    private sealed record PlannedContract(string FileName, StagedAssembly Staged, string Path);

    /// <summary>
    /// One package's validated contract set, with what it publishes and what it binds to.
    /// </summary>
    /// <param name="Publisher">The package.</param>
    /// <param name="Contracts">Its contracts, in declaration order.</param>
    /// <param name="OwnedNames">The simple names it publishes.</param>
    /// <param name="ConsumedNames">
    /// The simple names it binds to from other packages, so that withdrawing a package which failed to load
    /// also withdraws the packages whose contracts were validated against it.
    /// </param>
    private sealed record PlannedPublisher(
        PluginId Publisher,
        IReadOnlyList<PlannedContract> Contracts,
        IReadOnlyList<string> OwnedNames,
        IReadOnlyList<string> ConsumedNames);

    /// <summary>
    /// The Host-owned collectible context every shared contract is loaded into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not the default context: yielding a contract to the default context would give every
    /// dependant one identity and also make the assembly permanently unloadable.
    /// </para>
    /// <para>
    /// Resolution is fail-closed. Only the host contract assembly and the shared framework fall through to
    /// the default context; every other unrecognized request throws, because returning <see langword="null"/>
    /// would hand it to the default context where Host and the API already live.
    /// </para>
    /// </remarks>
    private sealed class SharedContractLoadContext : AssemblyLoadContext
    {
        private readonly ConcurrentDictionary<string, (AssemblyIdentity Identity, Assembly Assembly)> _contracts =
            new(StagedAssembly.NameComparer);

        internal SharedContractLoadContext()
            : base(ContextName, isCollectible: true)
        {
        }

        /// <summary>Loads one staged contract into this context and records what its name means here.</summary>
        internal Assembly Add(StagedAssembly staged)
        {
            var assembly = staged.LoadInto(this);
            _contracts[staged.Identity.Name!] = (staged.Identity, assembly);
            return assembly;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            ArgumentNullException.ThrowIfNull(assemblyName);

            var name = assemblyName.Name;

            if (PluginLoadContext.IsBlocked(name))
            {
                throw new PluginIsolationException(name!, ContextName);
            }

            if (PluginLoadContext.IsHostContract(name) || PluginLoadContext.IsSharedFramework(name))
            {
                return null;
            }

            if (name is not null && _contracts.TryGetValue(name, out var contract))
            {
                if (!StagedAssembly.SameIdentity(contract.Identity, assemblyName))
                {
                    throw new SharedContractIdentityException(
                        StagedAssembly.Describe(AssemblyIdentity.From(assemblyName)),
                        StagedAssembly.Describe(contract.Identity),
                        ContextName);
                }

                return contract.Assembly;
            }

            throw new PluginIsolationException(
                $"A shared contract requested '{StagedAssembly.Describe(AssemblyIdentity.From(assemblyName))}', which this installation "
                + "has not admitted as a shared contract. A shared contract may reach the host contract assembly, "
                + "the shared framework and other admitted contracts, and nothing else.");
        }
    }
}

/// <summary>The shared-contract mechanism's own diagnostics.</summary>
internal static partial class SharedContractLog
{
    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Shared contract {Identity} published by {Publisher} was admitted once for this installation (SHA-256 {ContentHash}).")]
    internal static partial void AdmittedCore(ILogger logger, string identity, string publisher, string contentHash);

    internal static void Admitted(ILogger? logger, string identity, string publisher, string contentHash)
    {
        if (logger is not null)
        {
            AdmittedCore(logger, identity, publisher, contentHash);
        }
    }
}
