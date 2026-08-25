using System.IO;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Common.Naming;
using Arronix.Plugins.Configuration;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Manifest;
using Arronix.Plugins.Registration;
using Arronix.Plugins.Registry;
using Arronix.Plugins.Scoping;
using Arronix.Plugins.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;


namespace Arronix.Plugins.Loading;

/// <summary>
/// A check the host contributes to the pipeline, run after registration and before commitment.
/// </summary>
/// <remarks>
/// <para>
/// The seam exists because two of the pipeline's checks — proving a declared media shape well-formed, and
/// proving a declared interface surface coherent — need types the loader deliberately does not know about.
/// The loader owns isolation and privilege; the host owns meaning. Inverting the dependency here is what
/// keeps the loader from having to reference the registries it loads extensions <i>for</i>.
/// </para>
/// <para>
/// The check answers with what it prepared rather than only with a verdict. Three of the pipeline's steps
/// run after preparation and every one of them is a question about what the extension actually contributes;
/// a verdict alone left them reading the declaration file, which is how a typed media kind the host had
/// already bound could still be quarantined for contributing no shape.
/// </para>
/// </remarks>
internal interface IPluginAdmissionCheck
{
    /// <summary>
    /// Builds and validates what an extension registered without publishing it.
    /// </summary>
    /// <param name="manifest">The extension's proved declaration.</param>
    /// <param name="ledger">Everything it registered.</param>
    /// <returns>The prepared attempt with its authoritative inventory, or the complete defect list.</returns>
    PluginAdmissionResult Prepare(ValidatedManifest manifest, PluginRegistrationLedger ledger);

    /// <summary>
    /// Authorizes one package to become active, inside the publication lease that would publish it.
    /// </summary>
    /// <param name="package">The package about to be published.</param>
    /// <param name="refusal">Why Host will not authorize it, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the package may be published.</returns>
    /// <remarks>
    /// Every package passes through this, including one that contributes no executable code and therefore
    /// never reaches <see cref="Prepare"/>. Without it a contract-only package could root itself while Host
    /// was shutting down, because the only state gate on the way in belonged to executable admission.
    /// </remarks>
    bool MayPublish(PluginId package, out string? refusal);
}

/// <summary>
/// The load pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Sixteen steps in a straight line, each with one failure class, and one property that matters more than
/// any of them: an extension never partially activates. It either passes every step and has everything it
/// registered committed, or it is quarantined and contributes nothing. There is no arrangement of failures
/// that leaves an extension half-present, because the commit is the last step and nothing before it is
/// visible outside the loader.
/// </para>
/// <para>
/// There is zero assembly scanning, ever. The loader loads exactly the assembly the declaration names,
/// enumerates its exported types once, and requires exactly one entry module. The alternative — adding an
/// extension assembly to a container's type scan — is how a surveyed implementation ended up with
/// extensions that could see every internal type of the host, and with a startup fallback that retried with
/// extensions disabled when the host threw. A loader that needs that fallback has already lost.
/// </para>
/// <para>
/// Files are read through the framework's own APIs rather than through the platform's file-system contract,
/// deliberately. Extension loading must not become unavailable because some other subsystem has not been
/// built, and the loader reads only paths the operator configured.
/// </para>
/// </remarks>
public sealed class PluginLoader
{
    private readonly IOptions<PluginRuntimeOptions> _options;
    private readonly PluginPlatformServices _platform;
    private readonly PluginRuntimeRegistry _registry;
    private readonly TokenRegistry _tokens;
    private readonly TimeProvider _clock;
    private readonly ILogger<PluginLoader> _logger;
    private readonly IMediaTypeCapabilityReader? _mediaTypes;
    private readonly PluginPublicationGate _publication;
    private readonly IPackageGraphSource _graphSource;
    private readonly SharedContractStore _contracts;
    private readonly PackageDependencyRegistry _dependencies;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginLoader"/> class.
    /// </summary>
    /// <param name="options">Operator control over loading.</param>
    /// <param name="platform">The services an extension context is built from.</param>
    /// <param name="registry">Where outcomes are recorded.</param>
    /// <param name="tokens">Who owns which naming token.</param>
    /// <param name="clock">The clock, so state changes are stamped from one source of time.</param>
    /// <param name="logger">The host's own diagnostics. Extensions never see it.</param>
    /// <param name="publication">The one Host-owned boundary shared by every plugin and Host registry.</param>
    /// <param name="graphSource">
    /// The one authority on what the installation requires of itself. Required: a loader with no resolution
    /// authority does not fall back to an assumed shape of the installation, it does not compose.
    /// </param>
    /// <param name="contracts">
    /// The installation's shared contract assemblies. Required: an installation that shares nothing has an
    /// empty admitted set, not an absent authority.
    /// </param>
    /// <param name="dependencies">Which package attempts are rooted, and which are pinned by a dependant.</param>
    /// <param name="mediaTypes">
    /// How a typed media kind is priced in capabilities. Optional so that a loader driving extensions that
    /// contribute no media kind needs no media machinery.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal PluginLoader(
        IOptions<PluginRuntimeOptions> options,
        PluginPlatformServices platform,
        PluginRuntimeRegistry registry,
        TokenRegistry tokens,
        TimeProvider clock,
        ILogger<PluginLoader> logger,
        PluginPublicationGate publication,
        IPackageGraphSource graphSource,
        SharedContractStore contracts,
        PackageDependencyRegistry dependencies,
        IMediaTypeCapabilityReader? mediaTypes = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(graphSource);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(dependencies);

        if (!ReferenceEquals(registry.PublicationGate, publication)
            || !ReferenceEquals(tokens.PublicationGate, publication)
            || !ReferenceEquals(dependencies.PublicationGate, publication))
        {
            throw new InvalidOperationException(
                "The extension loader, runtime registry, token registry and package dependency registry must "
                + "share one publication boundary.");
        }

        _options = options;
        _platform = platform;
        _registry = registry;
        _tokens = tokens;
        _clock = clock;
        _logger = logger;
        _mediaTypes = mediaTypes;
        _publication = publication;
        _graphSource = graphSource;
        _contracts = contracts;
        _dependencies = dependencies;
    }

    /// <summary>Gets the publication boundary this loader coordinates.</summary>
    internal PluginPublicationGate PublicationGate => _publication;

    /// <summary>Gets the installation's shared contract authority.</summary>
    internal SharedContractStore SharedContracts => _contracts;

    /// <summary>Gets which package attempts are rooted and pinned.</summary>
    internal PackageDependencyRegistry Dependencies => _dependencies;

    /// <summary>
    /// Gets the version of the contract assembly this host is running.
    /// </summary>
    /// <remarks>
    /// Read from the informational version so that it is the version the project declared rather than the
    /// four-part assembly version the build machinery may have rewritten.
    /// </remarks>
    public static SemanticVersion HostContractVersion { get; } = ReadHostContractVersion();

    /// <summary>
    /// Finds every declaration file beneath the configured root.
    /// </summary>
    /// <param name="rejected">Candidates whose declaration could not even be read.</param>
    /// <returns>The candidates, in folder order.</returns>
    /// <remarks>
    /// One folder per extension, one declaration per folder, no recursion. Discovery that is easy to
    /// describe is discovery an operator can predict.
    /// </remarks>
    public IReadOnlyList<PluginCandidate> Discover(out IReadOnlyList<PluginLoadResult> rejected)
    {
        var options = _options.Value;
        var failures = new List<PluginLoadResult>();
        var candidates = new List<PluginCandidate>();

        if (!options.Enabled)
        {
            PluginLoaderLog.LoadingDisabled(_logger);
            rejected = failures;
            return candidates;
        }

        var root = Path.GetFullPath(options.RootFolder);

        if (!Directory.Exists(root))
        {
            PluginLoaderLog.NoExtensionFolder(_logger, root);
            rejected = failures;
            return candidates;
        }

        foreach (var folder in Directory.EnumerateDirectories(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            var manifestPath = Path.Combine(folder, PluginManifestReader.FileName);

            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                candidates.Add(new PluginCandidate(manifestPath, PluginManifestReader.ReadFile(manifestPath)));
            }
            catch (ArronixException failure)
            {
                failures.Add(PluginLoadResult.Quarantined(
                    manifestPath,
                    id: null,
                    manifest: null,
                    failure.ErrorCode,
                    failure.Message,
                    defects: [failure.Message],
                    _clock.GetUtcNow()));
            }
        }

        rejected = failures;
        return candidates;
    }

    /// <summary>
    /// Runs the whole pipeline over everything installed.
    /// </summary>
    /// <param name="admission">The host's mandatory attempt-scoped admission transaction.</param>
    /// <param name="cancellationToken">Stops before another package begins; committed ones remain the caller's responsibility.</param>
    /// <returns>What became of every package, recorded in the registry as well as returned.</returns>
    /// <remarks>
    /// Asynchronous because preparing a package reads bytes and the publication gate is thread-affine: a
    /// write lease cannot span an await. Everything a commit needs is built before the gate is entered.
    /// </remarks>
    internal async Task<IReadOnlyList<PluginLoadResult>> LoadAllAsync(
        IPluginAdmissionCheck admission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admission);
        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<PluginLoadResult>();
        var discovered = Discover(out var unreadable);

        foreach (var failure in unreadable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(Record(failure));
        }

        // Steps 2 and 3: every declaration is proved well-formed, and the operator's configuration becomes
        // the typed availability state resolution reads, before any of them is acted on.
        var disabled = _options.Value.Disabled.ToHashSet(StringComparer.Ordinal);
        var validated = new List<ValidatedManifest>(discovered.Count);

        foreach (var candidate in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var availability = disabled.Contains(candidate.Manifest.Id)
                ? PackageAvailability.DisabledByConfiguration
                : PackageAvailability.Available;

            if (PluginManifestValidator.TryValidate(candidate, availability, out var manifest, out var defects))
            {
                validated.Add(manifest!);
                continue;
            }

            results.Add(Quarantine(
                candidate.ManifestPath,
                id: null,
                manifest: null,
                defects[0].Code,
                $"The declaration of the extension at '{candidate.Folder}' is not valid.",
                [.. defects.Select(defect => defect.ToString())]));
        }

        // Step 4: what the installation requires of itself, resolved once from proved declarations. Identity,
        // availability and dependencies are decided together, before any assembly is opened.
        var installed = validated.Select(manifest => manifest.Package).ToArray();
        var graph = _graphSource.Resolve(installed);
        RequireExactPartition(graph, installed);

        var byPackage = new Dictionary<InstalledPackage, ValidatedManifest>(ReferenceEqualityComparer.Instance);

        foreach (var manifest in validated)
        {
            byPackage[manifest.Package] = manifest;
        }

        // One result per installed copy, in the canonical order the refusal lists them. A duplicated
        // identifier has more than one, and each folder an operator has to act on is recorded separately
        // rather than one of them being chosen to speak for the identifier.
        foreach (var refusal in graph.Refused)
        {
            foreach (var copy in refusal.Copies)
            {
                cancellationToken.ThrowIfCancellationRequested();

                results.Add(Quarantine(
                    copy.Source,
                    refusal.Package,
                    byPackage[copy],
                    refusal.ErrorCode,
                    refusal.Reason,
                    [.. refusal.Defects.Select(defect => defect.ToString())]));
            }
        }

        // Step 5: the installation's shared contract assemblies become one admitted identity each, staged
        // and metadata-validated in graph order and loaded as one transaction, before any package's code
        // exists.
        var contracts = _contracts.Admit(graph);

        foreach (var package in graph.AdmissionOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifest = byPackage[package];

            if (contracts.Refusals.TryGetValue(package.Id, out var refused))
            {
                results.Add(Quarantine(
                    package.Source,
                    package.Id,
                    manifest,
                    refused.Code,
                    refused.Reason,
                    refused.Defects));
                continue;
            }

            // A dependency that failed its own admission is only knowable once it has been attempted, so
            // this is the one closure verdict the resolver cannot reach. The dependant is not attempted and
            // its defect names the dependency, so the chain stays walkable one link at a time.
            if (!TryBindDependencies(package, graph, out var edges, out var unmet))
            {
                results.Add(Quarantine(
                    package.Source,
                    package.Id,
                    manifest,
                    CoreErrorCode.PluginDependencyUnavailable,
                    $"Extension '{package.Id}' was not attempted because its dependency closure is not satisfied.",
                    unmet));
                continue;
            }

            results.Add(await LoadAsync(manifest, edges, admission, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>
    /// Requires the resolved graph to be an exact partition of what it was asked about.
    /// </summary>
    /// <param name="graph">What the resolution authority answered.</param>
    /// <param name="installed">Exactly what it was asked about.</param>
    /// <exception cref="InvalidOperationException">The answer is not an exact partition of the question.</exception>
    /// <remarks>
    /// Compared by reference. A resolver that returned structurally equal clones would decide what a package
    /// is, and every diagnostic downstream would describe something that is not installed. None of these is
    /// an operator's mistake, so none of them quarantines a package: they fail composition before anything
    /// loads.
    /// </remarks>
    private static void RequireExactPartition(
        ResolvedPackageGraph graph,
        IReadOnlyList<InstalledPackage> installed)
    {
        if (graph is null)
        {
            throw new InvalidOperationException(
                "The package resolution authority answered with no graph. A host with no usable authority on "
                + "what the installation requires of itself cannot decide what to admit.");
        }

        var copies = new Dictionary<PluginId, List<InstalledPackage>>();

        foreach (var package in installed)
        {
            if (!copies.TryGetValue(package.Id, out var found))
            {
                found = [];
                copies[package.Id] = found;
            }

            found.Add(package);
        }

        var answered = new HashSet<PluginId>();
        var accounted = new HashSet<InstalledPackage>(ReferenceEqualityComparer.Instance);
        var defects = new List<string>();

        foreach (var package in graph.AdmissionOrder)
        {
            if (!answered.Add(package.Id))
            {
                defects.Add($"package[{package.Id}]: answered for more than once.");
                continue;
            }

            if (!copies.TryGetValue(package.Id, out var installedCopies))
            {
                defects.Add($"package[{package.Id}]: admitted, but it is not installed.");
                continue;
            }

            if (installedCopies.Count != 1 || !ReferenceEquals(package, installedCopies[0]))
            {
                defects.Add(
                    $"package[{package.Id}]: admitted as a different object from the one installed copy the "
                    + "authority was asked about.");
                continue;
            }

            accounted.Add(package);
        }

        foreach (var refusal in graph.Refused)
        {
            if (!answered.Add(refusal.Package))
            {
                defects.Add($"package[{refusal.Package}]: answered for more than once.");
                continue;
            }

            if (!copies.TryGetValue(refusal.Package, out var installedCopies))
            {
                defects.Add($"package[{refusal.Package}]: refused, but it is not installed.");
                continue;
            }

            // A refusal carries the copies it is about, and everything downstream reads them: a duplicated
            // identifier is reported once per installed copy. A refusal naming none of them, or naming a
            // clone, would leave an installed object unaccounted for and fail later at a lookup.
            var named = new HashSet<InstalledPackage>(refusal.Copies, ReferenceEqualityComparer.Instance);

            if (named.Count != installedCopies.Count || !installedCopies.All(named.Contains))
            {
                defects.Add(
                    $"package[{refusal.Package}]: refused naming {refusal.Copies.Count} copies, but "
                    + $"{installedCopies.Count} exact installed copies were supplied.");
                continue;
            }

            foreach (var copy in installedCopies)
            {
                accounted.Add(copy);
            }
        }

        foreach (var id in copies.Keys.Where(id => !answered.Contains(id)))
        {
            defects.Add($"package[{id}]: installed, but the graph neither admits nor refuses it.");
        }

        foreach (var package in installed.Where(package => !accounted.Contains(package)))
        {
            defects.Add($"package[{package.Id}]: the copy at '{package.Folder}' was never accounted for.");
        }

        if (defects.Count > 0)
        {
            throw new InvalidOperationException(
                "The package resolution authority did not answer for exactly the installed extensions: "
                + string.Join(" ", defects.OrderBy(defect => defect, StringComparer.Ordinal)));
        }
    }

    /// <summary>
    /// Binds one package's requirements to the exact receipts they were satisfied by.
    /// </summary>
    /// <param name="package">The package about to be attempted.</param>
    /// <param name="graph">The resolved graph this package's edges were computed from.</param>
    /// <param name="edges">The bound edges when every requirement is rooted.</param>
    /// <param name="defects">Why the closure is unsatisfied, or an empty list.</param>
    /// <returns><see langword="true"/> when every requirement is rooted.</returns>
    private bool TryBindDependencies(
        InstalledPackage package,
        ResolvedPackageGraph graph,
        out IReadOnlyList<PackageDependencyEdge> edges,
        out IReadOnlyList<string> defects)
    {
        var bound = new List<PackageDependencyEdge>(package.Requirements.Count);
        var found = new List<string>();

        foreach (var requirement in package.Requirements)
        {
            if (!_dependencies.TryGetRooted(requirement.PackageId, out var dependency) || dependency is null)
            {
                found.Add(
                    $"dependency[{requirement.PackageId}]: extension '{package.Id}' requires "
                    + $"'{requirement.PackageId}' {requirement.Range}, which is not admitted. Resolve that "
                    + "extension's own defects first.");
                continue;
            }

            // The rooted receipt has to be the attempt this graph resolved against, not merely something
            // sharing its identifier. An incumbent left over from an earlier pass, or a reload at another
            // version, would otherwise satisfy an edge computed for a different installation.
            if (!graph.TryGet(requirement.PackageId, out var resolved)
                || !ReferenceEquals(dependency.Package, resolved))
            {
                found.Add(
                    $"dependency[{requirement.PackageId}]: extension '{package.Id}' was resolved against a "
                    + $"different installation attempt of '{requirement.PackageId}' than the one now admitted "
                    + $"at version {dependency.Version}.");
                continue;
            }

            bound.Add(new PackageDependencyEdge(package.Id, requirement, dependency));
        }

        edges = bound;
        defects = found;
        return found.Count == 0;
    }

    /// <summary>
    /// Runs the pipeline over one proved package.
    /// </summary>
    /// <param name="manifest">Its proved declaration, carrying the canonical package snapshot.</param>
    /// <param name="edges">Its bound dependency edges, each naming an exact rooted dependency receipt.</param>
    /// <param name="admission">The host's mandatory attempt-scoped admission transaction.</param>
    /// <param name="cancellationToken">Stops before this package is admitted.</param>
    /// <returns>What became of it, recorded in the registry as well as returned.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal async Task<PluginLoadResult> LoadAsync(
        ValidatedManifest manifest,
        IReadOnlyList<PackageDependencyEdge> edges,
        IPluginAdmissionCheck admission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(admission);

        var package = manifest.Package;
        var source = package.Source;
        var admitted = AdmittedInventory.NotAdmitted;

        // A host precondition rather than a step, and only for a package that runs code: a contract-only
        // package activates nothing and needs no extension context, so refusing it for a subsystem it never
        // touches would report a host misconfiguration as a defect in a package that has none.
        var missing = package.HasEntryAssembly ? _platform.MissingRequiredServices() : [];
        if (missing.Count > 0)
        {
            return Quarantine(
                source,
                package.Id,
                manifest,
                CoreErrorCode.PluginLoadFailure,
                $"This host cannot activate extensions: it has registered no {string.Join(", ", missing)}.",
                defects: [.. missing]);
        }

        // Step 6: the declared contract range.
        if (!manifest.ContractRange.IsSatisfiedBy(HostContractVersion))
        {
            return Quarantine(
                source,
                package.Id,
                manifest,
                CoreErrorCode.PluginContractMismatch,
                $"Extension '{package.Id}' accepts contract versions '{manifest.ContractRange}', which this host's {HostContractVersion} does not satisfy.",
                defects: []);
        }

        // Step 7: nothing in this package's folder may privately duplicate an admitted shared contract, and
        // nothing in it may bind to one outside its own declared closure.
        if (!_contracts.TryCheckPackage(package, out var isolationCode, out var isolationDefects))
        {
            return Quarantine(
                source,
                package.Id,
                manifest,
                isolationCode,
                isolationCode == CoreErrorCode.PluginLoadFailure
                    ? $"Extension '{package.Id}' could not be checked against the shared contracts this installation admitted."
                    : $"Extension '{package.Id}' conflicts with the shared contracts this installation admitted.",
                isolationDefects);
        }

        // The package receipt exists from here, before either of its optional halves does.
        var receipt = new PackageAdmissionReceipt(package, edges);

        // Step 8: pin the exact dependency receipts before anything is read, resolved or run. Binding them
        // was a diagnostic; this is the defense.
        if (!_dependencies.TryPinDependencies(receipt, out var pinDefects))
        {
            return Quarantine(
                source,
                package.Id,
                manifest,
                CoreErrorCode.PluginDependencyUnavailable,
                $"Extension '{package.Id}' was not loaded because its dependencies changed while it was being prepared.",
                pinDefects);
        }

        PackageAdmissionLease? lease = null;
        PluginLoadContext? context = null;
        IPluginModule? module = null;
        PluginRegistrationLedger? ledger = null;
        PluginRuntimeLease? runtimeLease = null;
        var committed = false;
        PluginAdmissionResult? preparation = null;
        TokenRegistry.TokenClaimPlan? tokenPlan = null;

        try
        {
            // Step 9: this package's scoped view of the installation's contracts, which is also its hold on
            // the contract context. A contract-only package takes one too: its published contracts live in
            // that context and its dependants pin it through this hold.
            lease = new PackageAdmissionLease(receipt, _contracts.OpenScope(package));

            PluginLoadResult loaded;

            if (package.EntryAssemblyFileName is null)
            {
                // Step 10a: a contract-only package has nothing to activate. It is a complete installation
                // attempt with a receipt and a contract hold and no load context, ledger or Host admission.
                loaded = PluginLoadResult.ActivePackage(source, manifest, _clock.GetUtcNow(), lease);
            }
            else
            {
                var entryPath = Path.Combine(package.Folder, package.EntryAssemblyFileName);

                // Steps 10b and 11: the file is read once. Two reads of a path the package owns is a race in
                // which the assembly that was judged and the assembly that runs need not be the same one.
                if (!StagedAssembly.TryStage(entryPath, out var staged, out var stagingError))
                {
                    return Quarantine(
                        source,
                        package.Id,
                        manifest,
                        CoreErrorCode.PluginLoadFailure,
                        $"The entry assembly of extension '{package.Id}' could not be inspected: {stagingError}",
                        defects: []);
                }

                var report = PluginReferenceInspector.Report(staged!);

                if (!report.IsAdmissible)
                {
                    return Quarantine(
                        source,
                        package.Id,
                        manifest,
                        CoreErrorCode.PluginIsolationViolation,
                        $"Extension '{package.Id}' references forbidden Host, platform, or legacy implementation assemblies.",
                        [.. report.Violations]);
                }

                context = new PluginLoadContext(package.Id, entryPath, nativeLibraryResolver: null, contracts: lease.Contracts);
                var assembly = context.LoadEntryAssembly(staged!);

                if (!TryLocateModule(assembly, manifest, out module, out var moduleError))
                {
                    return Quarantine(source, package.Id, manifest, CoreErrorCode.PluginLoadFailure, moduleError!, defects: []);
                }

                // Step 12: everything the extension registers is admitted or refused as it registers it.
                ledger = new PluginRegistrationLedger(package.Id);
                var registry = new PluginRegistry(package.Id, manifest.GrantedCapabilities, ledger, _mediaTypes);

                var pluginContext = BuildContext(manifest, registry, context);
                ledger.ActivationContext = pluginContext;

                try
                {
                    module!.Configure(pluginContext);
                }
                catch (PluginCapabilityException failure)
                {
                    return Quarantine(source, package.Id, manifest, failure.ErrorCode, failure.Message, defects: []);
                }
// A throwing extension quarantines itself; it never brings down the host. A process-fatal condition is
// not an extension defect and keeps propagating.
#pragma warning disable CA1031
                catch (Exception failure) when (LoadFailurePolicy.IsContainablePackageFailure(failure))
#pragma warning restore CA1031
                {
                    return Quarantine(
                        source,
                        package.Id,
                        manifest,
                        CoreErrorCode.PluginLoadFailure,
                        $"Extension '{package.Id}' threw while registering: {failure.Message}",
                        defects: []);
                }
                finally
                {
                    registry.Seal();
                }

                // Step 13: the forward half of the bidirectional capability check.
                if (!ledger.TryVerifyDeclaredCapabilities(manifest.DeclaredCapabilities, out var unsatisfied))
                {
                    return Quarantine(
                        source,
                        package.Id,
                        manifest,
                        CoreErrorCode.PluginCapabilityUnsatisfied,
                        $"Extension '{package.Id}' declared capabilities it never used. Declare only what the extension contributes.",
                        [.. unsatisfied.Select(CapabilityNames.ToWireName)]);
                }

                // Steps 14 and 15: the host's own checks over what was registered. From here on, what the
                // host admitted is the authority on this extension's kinds and their tokens.
                var verdict = admission.Prepare(manifest, ledger);

                if (!verdict.IsAdmitted)
                {
                    return Quarantine(
                        source,
                        package.Id,
                        manifest,
                        verdict.ErrorCode,
                        $"What extension '{package.Id}' registered did not pass validation.",
                        verdict.Defects);
                }

                // The Host has handed the loader an attempt-owned resource. Retain the receipt before
                // validating the rest of the verdict so a malformed success is still abandoned.
                preparation = verdict;

                var admissionAttempt = verdict.Attempt;
                if (admissionAttempt is null)
                {
                    return Quarantine(
                        source,
                        package.Id,
                        manifest,
                        CoreErrorCode.PluginLoadFailure,
                        $"The Host admission check for extension '{package.Id}' returned no commit attempt.",
                        defects: ["A successful Host preparation must carry its attempt-scoped commit and rollback receipt."]);
                }

                if (!verdict.Inventory.IsAuthoritative)
                {
                    return Quarantine(
                        source,
                        package.Id,
                        manifest,
                        CoreErrorCode.PluginLoadFailure,
                        $"The Host admission check for extension '{package.Id}' returned a non-authoritative inventory.",
                        defects: ["A successful Host preparation must describe exactly what its attempt will publish, including an authoritative empty result."]);
                }

                admitted = verdict.Inventory;

                // Step 16: what the declaration says about derivable media facts must agree with what was
                // admitted.
                if (!TryCrossCheckDerivedDeclarations(manifest, admitted, out var tokenDefects))
                {
                    return Quarantine(
                        source,
                        package.Id,
                        manifest,
                        CoreErrorCode.PluginPolicyDeclarationInvalid,
                        $"The media kinds and tokens extension '{package.Id}' declares do not match what its media shape defines.",
                        tokenDefects);
                }

                // Step 17: token ownership across the installation, claimed per kind.
                if (!_tokens.TryPrepareClaims(package.Id, TokenClaims(admitted), out tokenPlan, out var claimDefects))
                {
                    return Quarantine(
                        source,
                        package.Id,
                        manifest,
                        CoreErrorCode.PluginTokenConflict,
                        $"Extension '{package.Id}' declares tokens that are already spoken for.",
                        [.. claimDefects.Select(defect => defect.ToString())]);
                }

                // Step 18: identity of what was registered, within this extension and across the installation.
                if (!TryCheckIdentifiers(ledger, admitted, out var identityCode, out var identityDefects))
                {
                    return Quarantine(
                        source,
                        package.Id,
                        manifest,
                        identityCode,
                        $"Extension '{package.Id}' registered conflicting identifiers.",
                        identityDefects);
                }

                // Step 19.
                receipt.AttachHostAdmission(admissionAttempt);
                runtimeLease = new PluginRuntimeLease(context, ledger, module, tokenPlan, admissionAttempt);
                lease.AttachRuntime(runtimeLease);

                loaded = PluginLoadResult
                    .Progressed(source, PluginState.Registered, manifest, ledger, context, _clock.GetUtcNow(), admitted)
                    .Activate(ledger, context, _clock.GetUtcNow(), admitted, lease);
            }

            CoreErrorCode? publicationError = null;
            IReadOnlyList<string> publicationDefects = [];

            using (_publication.EnterWrite())
            {
                if (!admission.MayPublish(package.Id, out var authorization))
                {
                    publicationError = CoreErrorCode.PluginLoadFailure;
                    publicationDefects = [authorization ?? "Host did not authorize this package to publish."];
                }
                else if (!_registry.CanActivate(package.Id))
                {
                    publicationError = CoreErrorCode.PluginIdConflict;
                    publicationDefects = [$"Extension '{package.Id}' already has an active runtime attempt."];
                }
                else if (tokenPlan is not null && !tokenPlan.TryCommit(out var lateTokenDefects))
                {
                    publicationError = CoreErrorCode.PluginTokenConflict;
                    publicationDefects = [.. lateTokenDefects.Select(defect => defect.ToString())];
                }
                else if (receipt.HostAdmission is { } attempt && !attempt.TryCommit(out var commitCode, out var commitDefects))
                {
                    publicationError = commitCode;
                    publicationDefects = commitDefects;
                    tokenPlan?.Rollback();
                }
                // Every edge is rechecked here against the exact receipt this package was prepared against.
                // A pre-gate check is a diagnostic; only a recheck inside the lease that publishes can be a
                // total order with the lease that withdraws.
                else if (!_dependencies.TryPublish(receipt, out var edgeDefects))
                {
                    publicationError = CoreErrorCode.PluginDependencyUnavailable;
                    publicationDefects = edgeDefects;
                    receipt.HostAdmission?.Rollback();
                    tokenPlan?.Rollback();
                }
                else
                {
                    if (!_registry.Record(loaded))
                    {
                        throw new InvalidOperationException(
                            $"Extension '{package.Id}' became active while holding the final publication gate.");
                    }

                    committed = true;
                }
            }

            if (publicationError is { } code)
            {
                return Quarantine(
                    source,
                    package.Id,
                    manifest,
                    code,
                    $"Extension '{package.Id}' could not publish its prepared contributions.",
                    publicationDefects);
            }

            PluginLoaderLog.Activated(
                _logger,
                package.Id.ToString(),
                package.Version.ToString(),
                ledger?.Count ?? 0);

            context = null;
            return loaded;
        }
        // Cancellation of the caller's token is not an extension defect and must not be recorded as one. The
        // finally block still runs first, so this attempt gives up its pins, its instances and its contract
        // hold before the cancellation reaches the caller.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PluginIsolationException failure)
        {
            return Quarantine(source, package.Id, manifest, failure.ErrorCode, failure.Message, defects: []);
        }
        catch (BadImageFormatException failure)
        {
            return Quarantine(
                source,
                package.Id,
                manifest,
                CoreErrorCode.PluginLoadFailure,
                $"The entry assembly of extension '{package.Id}' is not loadable: {failure.Message}",
                defects: []);
        }
        catch (FileNotFoundException failure)
        {
            return Quarantine(
                source,
                package.Id,
                manifest,
                CoreErrorCode.PluginLoadFailure,
                $"The entry assembly of extension '{package.Id}' was not found: {failure.Message}",
                defects: []);
        }
// Everything in this block which is not Host publication infrastructure is extension-controlled. A novel
// extension exception is still a quarantine - an allowlist here would let an unfamiliar extension bug stop
// the whole installation - and the finally block releases every attempt-owned value.
#pragma warning disable CA1031
        catch (Exception failure) when (!committed && LoadFailurePolicy.IsContainablePackageFailure(failure))
#pragma warning restore CA1031
        {
            return Quarantine(
                source,
                package.Id,
                manifest,
                CoreErrorCode.PluginLoadFailure,
                $"Extension '{package.Id}' failed during loading: {failure.Message}",
                defects: []);
        }
        finally
        {
            if (!committed)
            {
                // Phase one of the same two-phase withdrawal teardown uses: hide the attempt without giving
                // up anything it holds. An attempt that never published removes nothing.
                if (!_dependencies.BeginWithdrawal(receipt, out var blockedBy))
                {
                    PluginLoaderLog.CleanupFailed(
                        _logger,
                        package.Id.ToString(),
                        $"package edges are still pinned by [{string.Join(", ", blockedBy)}]");
                }

                tokenPlan?.Rollback();
                preparation?.Attempt?.Rollback();

                var released = true;

                if (lease is not null)
                {
                    if (context is not null && lease.Runtime is null)
                    {
                        lease.AttachRuntime(new PluginRuntimeLease(context, ledger, module, tokenPlan, preparation?.Attempt));
                    }

                    foreach (var failure in await lease.DisposeAsync().ConfigureAwait(false))
                    {
                        released = false;
                        PluginLoaderLog.CleanupFailed(_logger, package.Id.ToString(), failure);
                    }
                }

                if (released)
                {
                    // Phase two: pins and edges go last, after this attempt's code is gone and after it gave
                    // up its own contract hold, which it must do while its dependencies are still pinned.
                    _dependencies.CompleteWithdrawal(receipt);
                }
                else
                {
                    // Explicitly retained rather than left as a pending preparation: its code may still be
                    // resident, so its identifier stays occupied and its dependencies stay pinned.
                    _dependencies.RetainFailedAttempt(receipt);
                    PluginLoaderLog.CleanupFailed(
                        _logger,
                        package.Id.ToString(),
                        "its instances, load context or contract hold could not be released, so its "
                        + "identifier stays occupied and it goes on holding every extension it depends on");
                }
            }
        }
    }

    private static SemanticVersion ReadHostContractVersion()
    {
        var assembly = typeof(IPluginModule).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (informational is not null)
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            var trimmed = plus >= 0 ? informational[..plus] : informational;

            if (SemanticVersion.TryParse(trimmed, out var parsed))
            {
                return parsed;
            }
        }

        var version = assembly.GetName().Version;
        return version is null
            ? new SemanticVersion(0, 0, 0)
            : new SemanticVersion(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    private static bool TryLocateModule(
        Assembly assembly,
        ValidatedManifest manifest,
        out IPluginModule? module,
        out string? error)
    {
        module = null;
        error = null;

        // Exported types only, and only of the one assembly the declaration named. Nothing is scanned.
        var candidates = assembly
            .GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IPluginModule).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            .ToArray();

        if (candidates.Length != 1)
        {
            error = candidates.Length == 0
                ? $"The entry assembly of extension '{manifest.Id}' exposes no public parameterless entry module."
                : $"The entry assembly of extension '{manifest.Id}' exposes {candidates.Length} entry modules where exactly one is required. Ambiguity about what an extension is is a defect, not a feature.";
            return false;
        }

        try
        {
            module = (IPluginModule)Activator.CreateInstance(candidates[0])!;
        }
        // The constructor is package code and reflection wraps whatever it threw, so the filter walks the
        // chain: an unfamiliar package exception quarantines, and a wrapped process-fatal condition or a
        // cancellation propagates rather than being recorded as an ordinary construction defect.
        catch (TargetInvocationException failure)
            when (LoadFailurePolicy.IsContainablePackageFailure(failure))
        {
            error = $"The entry module of extension '{manifest.Id}' threw while being constructed: {failure.InnerException?.Message ?? failure.Message}";
            return false;
        }
        catch (MissingMethodException failure)
        {
            // Raised by reflection rather than by the package: there is no constructor to have thrown, so
            // there is no chain to inspect.
            error = $"The entry module of extension '{manifest.Id}' could not be constructed: {failure.Message}";
            return false;
        }

        PluginId moduleId;
        try
        {
            moduleId = module.Id;
        }
// The identifier getter is package code, so an unfamiliar exception quarantines while a process-fatal
// condition or a cancellation propagates. Either way the caller retains the constructed module, because
// `module` was written to its variable before the getter ran, so disposal happens before the context unloads.
#pragma warning disable CA1031
        catch (Exception failure) when (LoadFailurePolicy.IsContainablePackageFailure(failure))
#pragma warning restore CA1031
        {
            error = $"The entry module of extension '{manifest.Id}' threw while reporting its identity: {failure.Message}";
            return false;
        }

        if (moduleId != manifest.Id)
        {
            error = $"The entry module of extension '{manifest.Id}' identifies itself as '{moduleId}'. The declaration and the module must agree.";
            return false;
        }

        return true;
    }

    /// <remarks>
    /// Every gated dependency is wrapped before it is handed over, and the wrapping happens here rather
    /// than at the point of use so that there is exactly one place to read to know what an extension is
    /// actually holding.
    /// </remarks>
    private PluginContext BuildContext(
        ValidatedManifest manifest,
        PluginRegistry registry,
        PluginLoadContext loadContext)
    {
        var options = _options.Value;
        var access = options.AccessFor(manifest.Id.Value);

        var paths = PluginPaths.Beneath(manifest.Id, options.StateFolder);
        paths.Prepare();

        var granted = manifest.GrantedCapabilities;

        var http = granted.Has(Capability.Network) && _platform.Http is not null
            ? new ScopedHttpGateway(_platform.Http, manifest.Id, [.. access.AllowedHosts], [.. access.DeniedHosts])
            : null;

        var limiter = granted.Has(Capability.Network) && _platform.RateLimiter is not null
            ? new PartitionedRateLimiter(_platform.RateLimiter, manifest.Id)
            : null;

        // The extension's own folders are always inside its grant: a folder the platform created for it is
        // not a privilege an operator has to remember to configure.
        var roots = new List<string>(access.GrantedRoots.Count + 3) { paths.DataFolder, paths.CacheFolder, paths.TempFolder };
        roots.AddRange(access.GrantedRoots);

        var fileSystem = granted.Has(Capability.Storage) && _platform.FileSystem is not null
            ? new ScopedFileSystem(_platform.FileSystem, manifest.Id, roots)
            : null;

        return new PluginContext(
            manifest.Id,
            manifest.Version.ToString(),
            HostContractVersion.ToString(),
            granted,
            registry,
            paths,
            new PartitionedCacheProvider(_platform.Cache!, manifest.Id),
            _platform.Json,
            new PluginTelemetryEmitter(_platform.Telemetry!, manifest.Id),
            new FilteredEventPublisher(_platform.Events!, manifest.Id, loadContext),
            _platform.Runtime!,
            _platform.OperatingSystem!,
            _platform.Clock,
            http,
            limiter,
            granted.Has(Capability.Network) ? _platform.CertificatePolicy : null,
            fileSystem,
            granted.Has(Capability.Import) ? _platform.FileTransfer : null);
    }

    /// <remarks>
    /// <para>
    /// Media kinds and naming tokens are derived from an extension's own types, so a manifest that omits
    /// them is complete rather than incomplete and nothing is checked. A manifest that states them is held
    /// to them exactly, in both directions: a token in the declaration that the kind does not define would
    /// present token help for something no template can use, and a token the kind defines that the
    /// declaration omits would have no description to present.
    /// </para>
    /// <para>The authority is always the Host admission attempt's exact inventory, including when empty.</para>
    /// </remarks>
    private static bool TryCrossCheckDerivedDeclarations(
        ValidatedManifest manifest,
        AdmittedInventory admitted,
        out IReadOnlyList<string> defects)
    {
        var authority = "admitted media kind";
        var definedKinds = admitted.Kinds.Select(kind => kind.Value).ToHashSet(StringComparer.Ordinal);
        var definedTokenNames = admitted.TokenNames
            .GroupBy(NamingTokenName.Canonicalize, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var found = new List<string>();

        if (manifest.MediaKinds.Count > 0)
        {
            var declaredKinds = manifest.MediaKinds.Select(kind => kind.Value).ToHashSet(StringComparer.Ordinal);

            foreach (var kind in declaredKinds.Except(definedKinds, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                found.Add($"media kind '{kind}' is declared in the manifest but no {authority} supplies it.");
            }

            foreach (var kind in definedKinds.Except(declaredKinds, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                found.Add($"media kind '{kind}' is supplied by a {authority} but is not declared in the manifest.");
            }
        }

        if (manifest.Tokens.Count > 0)
        {
            var declaredTokenNames = manifest.Tokens
                .ToDictionary(
                    token => NamingTokenName.Canonicalize(token.Name),
                    token => token.Name,
                    StringComparer.Ordinal);

            foreach (var canonical in declaredTokenNames.Keys
                         .Except(definedTokenNames.Keys, StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                found.Add($"'{declaredTokenNames[canonical]}' is declared in the manifest but no {authority} defines it.");
            }

            foreach (var canonical in definedTokenNames.Keys
                         .Except(declaredTokenNames.Keys, StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                found.Add($"'{definedTokenNames[canonical]}' is defined by a {authority} but is not declared in the manifest.");
            }
        }

        defects = found;
        return found.Count == 0;
    }

    /// <summary>
    /// Works out which tokens each of an extension's media kinds owns.
    /// </summary>
    /// <remarks>
    /// One request per kind, carrying that kind's own tokens from the exact Host admission attempt.
    /// </remarks>
    private static IReadOnlyList<TokenClaimRequest> TokenClaims(AdmittedInventory admitted)
        => [.. admitted.MediaKinds.Select(kind => new TokenClaimRequest(kind.Kind, kind.Tokens))];

    /// <remarks>
    /// <para>
    /// Provider identifiers are qualified by the extension that supplies them, so two extensions can never
    /// collide. Within one extension they can, and a media kind is claimed across the whole installation, so
    /// both are checked here.
    /// </para>
    /// <para>
    /// The kinds compared are the ones actually supplied on both sides: this extension's exact admitted
    /// inventory against every active extension's exact admitted inventory.
    /// </para>
    /// </remarks>
    private bool TryCheckIdentifiers(
        PluginRegistrationLedger ledger,
        AdmittedInventory admitted,
        out CoreErrorCode errorCode,
        out IReadOnlyList<string> defects)
    {
        var found = new List<string>();
        errorCode = CoreErrorCode.PluginIdConflict;

        var seen = new HashSet<(ProviderFamily Family, string LocalId)>();

        foreach (var registration in ledger.Registered<ProviderTypeRegistration>())
        {
            if (!seen.Add((registration.Family, registration.Descriptor.LocalId)))
            {
                found.Add(
                    $"More than one {registration.Family} is registered as '{registration.Descriptor.LocalId}'.");
            }
        }

        if (found.Count > 0)
        {
            defects = found;
            return false;
        }

        var claimed = new Dictionary<string, PluginId>(StringComparer.Ordinal);

        foreach (var result in _registry.All.Where(result => result.IsActive && result.Manifest is not null))
        {
            foreach (var kind in result.Admitted.Kinds)
            {
                claimed[kind.Value] = result.Manifest!.Id;
            }
        }

        foreach (var kind in admitted.Kinds)
        {
            if (claimed.TryGetValue(kind.Value, out var owner))
            {
                found.Add($"Media kind '{kind}' is already supplied by extension '{owner}'.");
            }
        }

        if (found.Count > 0)
        {
            errorCode = CoreErrorCode.MediaKindConflict;
            defects = found;
            return false;
        }

        defects = [];
        return true;
    }

    /// <summary>Records one already-built result exactly once.</summary>
    private PluginLoadResult Record(PluginLoadResult result)
    {
        _registry.Record(result);
        PluginLoaderLog.Quarantined(
            _logger,
            result.Id?.ToString() ?? result.Source,
            result.ErrorCode ?? CoreErrorCode.PluginLoadFailure,
            result.Message ?? string.Empty);

        return result;
    }

    private PluginLoadResult Quarantine(
        string source,
        PluginId? id,
        ValidatedManifest? manifest,
        CoreErrorCode errorCode,
        string message,
        IReadOnlyList<string> defects)
    {
        return Record(PluginLoadResult.Quarantined(
            source,
            id,
            manifest,
            errorCode,
            message,
            defects,
            _clock.GetUtcNow()));
    }
}
