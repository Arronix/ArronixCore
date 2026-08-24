using System.IO;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Common.Naming;
using Arronix.Plugins.Configuration;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginLoader"/> class.
    /// </summary>
    /// <param name="options">Operator control over loading.</param>
    /// <param name="platform">The services an extension context is built from.</param>
    /// <param name="registry">Where outcomes are recorded.</param>
    /// <param name="tokens">Who owns which naming token.</param>
    /// <param name="clock">The clock, so state changes are stamped from one source of time.</param>
    /// <param name="logger">The host's own diagnostics. Extensions never see it.</param>
    /// <param name="mediaTypes">
    /// How a typed media kind is priced in capabilities. Optional so that a loader driving extensions that
    /// contribute no media kind needs no media machinery; a registry built without one refuses a typed kind
    /// rather than under-pricing it.
    /// </param>
    /// <param name="publication">The one Host-owned boundary shared by every plugin and Host registry.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public PluginLoader(
        IOptions<PluginRuntimeOptions> options,
        PluginPlatformServices platform,
        PluginRuntimeRegistry registry,
        TokenRegistry tokens,
        TimeProvider clock,
        ILogger<PluginLoader> logger,
        PluginPublicationGate publication,
        IMediaTypeCapabilityReader? mediaTypes = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(publication);

        if (!ReferenceEquals(registry.PublicationGate, publication)
            || !ReferenceEquals(tokens.PublicationGate, publication))
        {
            throw new InvalidOperationException(
                "The extension loader, runtime registry and token registry must share one publication boundary.");
        }

        _options = options;
        _platform = platform;
        _registry = registry;
        _tokens = tokens;
        _clock = clock;
        _logger = logger;
        _mediaTypes = mediaTypes;
        _publication = publication;
    }

    /// <summary>Gets the publication boundary this loader coordinates.</summary>
    internal PluginPublicationGate PublicationGate => _publication;

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
    /// <param name="cancellationToken">Stops before another candidate begins; committed candidates remain the caller's responsibility.</param>
    /// <returns>What became of every extension, recorded in the registry as well as returned.</returns>
    internal IReadOnlyList<PluginLoadResult> LoadAll(
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
            _registry.Record(failure);
            results.Add(failure);
        }

        var validated = new List<(PluginCandidate Candidate, ValidatedManifest Manifest)>(discovered.Count);

        // Steps 2 and 3: every declaration is proved well-formed before any of them is acted on.
        foreach (var candidate in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (PluginManifestValidator.TryValidate(candidate.Manifest, out var manifest, out var defects))
            {
                validated.Add((candidate, manifest!));
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

        // Step 4: identity is checked across the whole installation, not within one extension, so it can
        // only be checked once every declaration has been read.
        var duplicates = validated
            .GroupBy(entry => entry.Manifest.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        var disabled = _options.Value.Disabled.ToHashSet(StringComparer.Ordinal);

        foreach (var (candidate, manifest) in validated)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (duplicates.Contains(manifest.Id))
            {
                results.Add(Quarantine(
                    candidate.ManifestPath,
                    manifest.Id,
                    manifest,
                    CoreErrorCode.PluginIdConflict,
                    $"More than one installed extension claims the identifier '{manifest.Id}'. Identity must be unique across an installation.",
                    defects: []));
                continue;
            }

            if (disabled.Contains(manifest.Id.Value))
            {
                results.Add(Quarantine(
                    candidate.ManifestPath,
                    manifest.Id,
                    manifest,
                    CoreErrorCode.PluginDisabled,
                    $"Extension '{manifest.Id}' is installed but disabled by configuration.",
                    defects: []));
                continue;
            }

            results.Add(Load(candidate, manifest, admission));
        }

        return results;
    }

    /// <summary>
    /// Runs the pipeline over one already-proved declaration.
    /// </summary>
    /// <param name="candidate">Where the extension was found.</param>
    /// <param name="manifest">Its proved declaration.</param>
    /// <param name="admission">The host's mandatory attempt-scoped admission transaction.</param>
    /// <returns>What became of it, recorded in the registry as well as returned.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    internal PluginLoadResult Load(
        PluginCandidate candidate,
        ValidatedManifest manifest,
        IPluginAdmissionCheck admission)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(admission);

        var source = candidate.ManifestPath;
        var admitted = AdmittedInventory.NotAdmitted;

        // A host precondition rather than a step: checked before anything is loaded, so that a host missing
        // a subsystem is reported as a host misconfiguration rather than as a defect in the first extension
        // that happened to reach for it.
        var missing = _platform.MissingRequiredServices();
        if (missing.Count > 0)
        {
            return Quarantine(
                source,
                manifest.Id,
                manifest,
                CoreErrorCode.PluginLoadFailure,
                $"This host cannot activate extensions: it has registered no {string.Join(", ", missing)}.",
                defects: [.. missing]);
        }

        // Step 5: the declared contract range.
        if (!manifest.ContractRange.IsSatisfiedBy(HostContractVersion))
        {
            return Quarantine(
                source,
                manifest.Id,
                manifest,
                CoreErrorCode.PluginContractMismatch,
                $"Extension '{manifest.Id}' accepts contract versions '{manifest.ContractRange}', which this host's {HostContractVersion} does not satisfy.",
                defects: []);
        }

        var entryPath = candidate.ResolveEntryAssemblyPath();

        // Step 6: the reference graph, read as metadata, before a load context exists and before a single
        // type initializer has had the chance to run.
        if (!PluginReferenceInspector.TryInspect(entryPath, out var report, out var inspectionError))
        {
            return Quarantine(
                source,
                manifest.Id,
                manifest,
                CoreErrorCode.PluginLoadFailure,
                $"The entry assembly of extension '{manifest.Id}' could not be inspected: {inspectionError}",
                defects: []);
        }

        if (!report!.IsAdmissible)
        {
            return Quarantine(
                source,
                manifest.Id,
                manifest,
                CoreErrorCode.PluginIsolationViolation,
                $"Extension '{manifest.Id}' references forbidden Host, platform, or legacy implementation assemblies.",
                [.. report.Violations]);
        }

        PluginLoadContext? context = null;
        IPluginModule? module = null;
        PluginRegistrationLedger? ledger = null;
        PluginRuntimeLease? runtimeLease = null;
        var committed = false;
        PluginAdmissionResult? preparation = null;
        TokenRegistry.TokenClaimPlan? tokenPlan = null;

        try
        {
            // Steps 7 and 8.
            context = new PluginLoadContext(manifest.Id, entryPath);
            var assembly = context.LoadEntryAssembly();

            if (!TryLocateModule(assembly, manifest, out module, out var moduleError))
            {
                return Quarantine(source, manifest.Id, manifest, CoreErrorCode.PluginLoadFailure, moduleError!, defects: []);
            }

            // Step 9: everything the extension registers is admitted or refused as it registers it.
            ledger = new PluginRegistrationLedger(manifest.Id);
            var registry = new PluginRegistry(manifest.Id, manifest.GrantedCapabilities, ledger, _mediaTypes);

            var pluginContext = BuildContext(manifest, registry, context);
            ledger.ActivationContext = pluginContext;

            try
            {
                module!.Configure(pluginContext);
            }
            catch (PluginCapabilityException failure)
            {
                return Quarantine(source, manifest.Id, manifest, failure.ErrorCode, failure.Message, defects: []);
            }
#pragma warning disable CA1031 // A throwing extension quarantines itself; it never brings down the host.
            catch (Exception failure)
#pragma warning restore CA1031
            {
                return Quarantine(
                    source,
                    manifest.Id,
                    manifest,
                    CoreErrorCode.PluginLoadFailure,
                    $"Extension '{manifest.Id}' threw while registering: {failure.Message}",
                    defects: []);
            }
            finally
            {
                registry.Seal();
            }

            // Step 10: the forward half of the bidirectional check.
            if (!ledger.TryVerifyDeclaredCapabilities(manifest.DeclaredCapabilities, out var unsatisfied))
            {
                return Quarantine(
                    source,
                    manifest.Id,
                    manifest,
                    CoreErrorCode.PluginCapabilityUnsatisfied,
                    $"Extension '{manifest.Id}' declared capabilities it never used. Declare only what the extension contributes.",
                    [.. unsatisfied.Select(CapabilityNames.ToWireName)]);
            }

            // Steps 11 and 12: the host's own checks over what was registered. From here on, what the host
            // admitted — not what the declaration claimed — is the authority on this extension's kinds and
            // their tokens.
            var verdict = admission.Prepare(manifest, ledger);

            if (!verdict.IsAdmitted)
            {
                return Quarantine(
                    source,
                    manifest.Id,
                    manifest,
                    verdict.ErrorCode,
                    $"What extension '{manifest.Id}' registered did not pass validation.",
                    verdict.Defects);
            }

            // From this point the Host has handed the loader an attempt-owned resource. Retain the
            // receipt before validating the rest of the result so every malformed successful verdict is
            // still abandoned by the finally block.
            preparation = verdict;

            var admissionAttempt = verdict.Attempt;
            if (admissionAttempt is null)
            {
                return Quarantine(
                    source,
                    manifest.Id,
                    manifest,
                    CoreErrorCode.PluginLoadFailure,
                    $"The Host admission check for extension '{manifest.Id}' returned no commit attempt.",
                    defects: ["A successful Host preparation must carry its attempt-scoped commit and rollback receipt."]);
            }

            if (!verdict.Inventory.IsAuthoritative)
            {
                return Quarantine(
                    source,
                    manifest.Id,
                    manifest,
                    CoreErrorCode.PluginLoadFailure,
                    $"The Host admission check for extension '{manifest.Id}' returned a non-authoritative inventory.",
                    defects: ["A successful Host preparation must describe exactly what its attempt will publish, including an authoritative empty result."]);
            }
            admitted = verdict.Inventory;

            // Step 13: what the declaration says about derivable media facts must agree with what was
            // admitted. A declaration that says nothing about them agrees by construction, because they are
            // derived from the extension's own types rather than restated in its manifest.
            if (!TryCrossCheckDerivedDeclarations(manifest, admitted, out var tokenDefects))
            {
                return Quarantine(
                    source,
                    manifest.Id,
                    manifest,
                    CoreErrorCode.PluginPolicyDeclarationInvalid,
                    $"The media kinds and tokens extension '{manifest.Id}' declares do not match what its media shape defines.",
                    tokenDefects);
            }

            // Step 14: token ownership across the whole installation, claimed per kind by the kind that owns
            // the token rather than as the cross product of an extension's kinds and its whole vocabulary.
            if (!_tokens.TryPrepareClaims(
                    manifest.Id,
                    TokenClaims(admitted),
                    out tokenPlan,
                    out var claimDefects))
            {
                return Quarantine(
                    source,
                    manifest.Id,
                    manifest,
                    CoreErrorCode.PluginTokenConflict,
                    $"Extension '{manifest.Id}' declares tokens that are already spoken for.",
                    [.. claimDefects.Select(defect => defect.ToString())]);
            }

            // Step 15: identity of what was registered, within this extension and across the installation.
            if (!TryCheckIdentifiers(ledger, admitted, out var identityCode, out var identityDefects))
            {
                return Quarantine(
                    source,
                    manifest.Id,
                    manifest,
                    identityCode,
                    $"Extension '{manifest.Id}' registered conflicting identifiers.",
                    identityDefects);
            }

            // Step 16.
            runtimeLease = new PluginRuntimeLease(
                context,
                ledger,
                module,
                tokenPlan,
                admissionAttempt);
            var loaded = PluginLoadResult
                .Progressed(
                    source,
                    PluginState.Registered,
                    manifest,
                    ledger,
                    context,
                    _clock.GetUtcNow(),
                    admitted)
                .Activate(
                    ledger,
                    context,
                    _clock.GetUtcNow(),
                    admitted,
                    runtimeLease);

            CoreErrorCode? publicationError = null;
            IReadOnlyList<string> publicationDefects = [];

            using (_publication.EnterWrite())
            {
                if (!_registry.CanActivate(manifest.Id))
                {
                    publicationError = CoreErrorCode.PluginIdConflict;
                    publicationDefects = [$"Extension '{manifest.Id}' already has an active runtime attempt."];
                }
                else if (!tokenPlan!.TryCommit(out var lateTokenDefects))
                {
                    publicationError = CoreErrorCode.PluginTokenConflict;
                    publicationDefects = [.. lateTokenDefects.Select(defect => defect.ToString())];
                }
                else if (!admissionAttempt.TryCommit(out var commitCode, out var commitDefects))
                {
                    publicationError = commitCode;
                    publicationDefects = commitDefects;
                    tokenPlan.Rollback();
                }
                else
                {
                    if (!_registry.Record(loaded))
                    {
                        throw new InvalidOperationException(
                            $"Extension '{manifest.Id}' became active while holding the final publication gate.");
                    }

                    committed = true;
                }
            }

            if (publicationError is { } code)
            {
                return Quarantine(
                    source,
                    manifest.Id,
                    manifest,
                    code,
                    $"Extension '{manifest.Id}' could not publish its prepared contributions.",
                    publicationDefects);
            }

            PluginLoaderLog.Activated(
                _logger,
                manifest.Id.ToString(),
                manifest.Version.ToString(),
                ledger.Count);

            context = null;
            return loaded;
        }
        catch (PluginIsolationException failure)
        {
            return Quarantine(source, manifest.Id, manifest, failure.ErrorCode, failure.Message, defects: []);
        }
        catch (BadImageFormatException failure)
        {
            return Quarantine(
                source,
                manifest.Id,
                manifest,
                CoreErrorCode.PluginLoadFailure,
                $"The entry assembly of extension '{manifest.Id}' is not loadable: {failure.Message}",
                defects: []);
        }
        catch (FileNotFoundException failure)
        {
            return Quarantine(
                source,
                manifest.Id,
                manifest,
                CoreErrorCode.PluginLoadFailure,
                $"The entry assembly of extension '{manifest.Id}' was not found: {failure.Message}",
                defects: []);
        }
// Everything in this block which is not Host publication infrastructure is extension-controlled: its
// assembly metadata, constructors, registration objects and property getters. A novel extension exception
// is still a quarantine, and the finally block releases every attempt-owned value before the next candidate.
#pragma warning disable CA1031
        catch (Exception failure) when (!committed)
#pragma warning restore CA1031
        {
            return Quarantine(
                source,
                manifest.Id,
                manifest,
                CoreErrorCode.PluginLoadFailure,
                $"Extension '{manifest.Id}' failed during loading: {failure.Message}",
                defects: []);
        }
        finally
        {
            if (!committed)
            {
                tokenPlan?.Rollback();
                preparation?.Attempt?.Rollback();
            }

            if (!committed && context is not null)
            {
                runtimeLease ??= new PluginRuntimeLease(
                    context,
                    ledger,
                    module,
                    tokenPlan,
                    preparation?.Attempt);

                foreach (var failure in runtimeLease.DisposeSynchronously())
                {
                    PluginLoaderLog.CleanupFailed(_logger, manifest.Id.ToString(), failure);
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
        catch (TargetInvocationException failure)
        {
            error = $"The entry module of extension '{manifest.Id}' threw while being constructed: {failure.InnerException?.Message ?? failure.Message}";
            return false;
        }
        catch (MissingMethodException failure)
        {
            error = $"The entry module of extension '{manifest.Id}' could not be constructed: {failure.Message}";
            return false;
        }

        PluginId moduleId;
        try
        {
            moduleId = module.Id;
        }
// The identifier getter is extension code. A throwing getter quarantines and the caller still retains the
// constructed module so its asynchronous disposal runs before the load context is unloaded.
#pragma warning disable CA1031
        catch (Exception failure)
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

        foreach (var descriptor in DescribedProviders(ledger))
        {
            if (!seen.Add((descriptor.Family, descriptor.LocalId)))
            {
                found.Add($"More than one {descriptor.Family} is registered as '{descriptor.LocalId}'.");
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

    private static IEnumerable<ProviderDescriptor> DescribedProviders(PluginRegistrationLedger ledger)
    {
        foreach (var registration in ledger.Registered<ProviderTypeRegistration>())
        {
            yield return registration.Descriptor;
        }
    }

    private PluginLoadResult Quarantine(
        string source,
        PluginId? id,
        ValidatedManifest? manifest,
        CoreErrorCode errorCode,
        string message,
        IReadOnlyList<string> defects)
    {
        var result = PluginLoadResult.Quarantined(source, id, manifest, errorCode, message, defects, _clock.GetUtcNow());

        _registry.Record(result);
        PluginLoaderLog.Quarantined(_logger, id?.ToString() ?? source, errorCode, message);

        return result;
    }
}
