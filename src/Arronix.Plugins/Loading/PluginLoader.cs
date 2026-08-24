using System.IO;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
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
/// The check answers with what it admitted rather than only with a verdict. Three of the pipeline's steps
/// run after admission and every one of them is a question about what the extension actually contributes;
/// a verdict alone left them reading the declaration file, which is how a typed media kind the host had
/// already bound and published could still be quarantined for contributing no shape.
/// </para>
/// </remarks>
public interface IPluginAdmissionCheck
{
    /// <summary>
    /// Decides whether what an extension registered may be committed, and says what was admitted.
    /// </summary>
    /// <param name="manifest">The extension's proved declaration.</param>
    /// <param name="ledger">Everything it registered.</param>
    /// <returns>The verdict, with the admitted inventory or the complete defect list.</returns>
    PluginAdmissionResult Admit(ValidatedManifest manifest, PluginRegistrationLedger ledger);
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
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public PluginLoader(
        IOptions<PluginRuntimeOptions> options,
        PluginPlatformServices platform,
        PluginRuntimeRegistry registry,
        TokenRegistry tokens,
        TimeProvider clock,
        ILogger<PluginLoader> logger,
        IMediaTypeCapabilityReader? mediaTypes = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _platform = platform;
        _registry = registry;
        _tokens = tokens;
        _clock = clock;
        _logger = logger;
        _mediaTypes = mediaTypes;
    }

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
    /// <param name="admission">The host's own admission check, when it has one.</param>
    /// <returns>What became of every extension, recorded in the registry as well as returned.</returns>
    public IReadOnlyList<PluginLoadResult> LoadAll(IPluginAdmissionCheck? admission = null)
    {
        var results = new List<PluginLoadResult>();
        var discovered = Discover(out var unreadable);

        foreach (var failure in unreadable)
        {
            _registry.Record(failure);
            results.Add(failure);
        }

        var validated = new List<(PluginCandidate Candidate, ValidatedManifest Manifest)>(discovered.Count);

        // Steps 2 and 3: every declaration is proved well-formed before any of them is acted on.
        foreach (var candidate in discovered)
        {
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
    /// <param name="admission">The host's own admission check, when it has one.</param>
    /// <returns>What became of it, recorded in the registry as well as returned.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public PluginLoadResult Load(
        PluginCandidate candidate,
        ValidatedManifest manifest,
        IPluginAdmissionCheck? admission = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(manifest);

        var source = candidate.ManifestPath;
        var admitted = AdmittedInventory.Empty;

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
                $"Extension '{manifest.Id}' references assemblies an extension may not reference. An extension references the contract assembly and nothing else.",
                [.. report.Violations]);
        }

        PluginLoadContext? context = null;
        var claimedTokens = false;
        var committed = false;

        try
        {
            // Steps 7 and 8.
            context = new PluginLoadContext(manifest.Id, entryPath);
            var assembly = context.LoadEntryAssembly();

            if (!TryLocateModule(assembly, manifest, out var module, out var moduleError))
            {
                return Quarantine(source, manifest.Id, manifest, CoreErrorCode.PluginLoadFailure, moduleError!, defects: []);
            }

            // Step 9: everything the extension registers is admitted or refused as it registers it.
            var ledger = new PluginRegistrationLedger(manifest.Id);
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
            if (admission is not null)
            {
                var verdict = admission.Admit(manifest, ledger);

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

                admitted = verdict.Inventory;
            }

            // Step 13: what the declaration says about derivable media facts must agree with what was
            // admitted. A declaration that says nothing about them agrees by construction, because they are
            // derived from the extension's own types rather than restated in its manifest.
            if (!TryCrossCheckDerivedDeclarations(manifest, ledger, admitted, out var tokenDefects))
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
            if (!_tokens.TryClaimAll(manifest.Id, TokenClaims(manifest, ledger, admitted), out var claimDefects))
            {
                return Quarantine(
                    source,
                    manifest.Id,
                    manifest,
                    CoreErrorCode.PluginTokenConflict,
                    $"Extension '{manifest.Id}' declares tokens that are already spoken for.",
                    [.. claimDefects.Select(defect => defect.ToString())]);
            }

            claimedTokens = true;

            // Step 15: identity of what was registered, within this extension and across the installation.
            if (!TryCheckIdentifiers(manifest, ledger, admitted, out var identityCode, out var identityDefects))
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
            var loaded = PluginLoadResult
                .Progressed(source, PluginState.Registered, manifest, ledger, context, _clock.GetUtcNow(), admitted)
                .Advance(PluginState.Active, ledger, context, _clock.GetUtcNow(), admitted);

            _registry.Record(loaded);
            PluginLoaderLog.Activated(
                _logger,
                manifest.Id.ToString(),
                manifest.Version.ToString(),
                ledger.Count);

            committed = true;
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
        finally
        {
            // Anything that did not reach the commit gives back everything it took. Tokens are given back
            // here rather than at each failing step so that no later step can be added which forgets: an
            // extension holding token ownership it was quarantined for is the half-registered state the
            // pipeline exists to make impossible.
            if (claimedTokens && !committed)
            {
                _tokens.Release(manifest.Id);
            }

            // A quarantined extension does not leave a load context alive holding its assemblies.
            context?.Unload();
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

        if (module.Id != manifest.Id)
        {
            error = $"The entry module of extension '{manifest.Id}' identifies itself as '{module.Id}'. The declaration and the module must agree.";
            module = null;
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
    /// <para>
    /// The authority is the admitted inventory whenever the host admitted anything, because that is the
    /// projection the platform will actually serve. The registered legacy shape seam is consulted only when
    /// no admission ran — a loader driven without a host — and remains a transitional path.
    /// </para>
    /// </remarks>
    private static bool TryCrossCheckDerivedDeclarations(
        ValidatedManifest manifest,
        PluginRegistrationLedger ledger,
        AdmittedInventory admitted,
        out IReadOnlyList<string> defects)
    {
        var authority = admitted.HasMediaKinds ? "admitted media kind" : "registered media shape";

        var definedKinds = admitted.HasMediaKinds
            ? admitted.Kinds.Select(kind => kind.Value).ToHashSet(StringComparer.Ordinal)
            : ledger.Registered<IMediaShapeProvider>()
                .Select(shape => shape.Shape.Kind.Value)
                .ToHashSet(StringComparer.Ordinal);

        var definedTokens = admitted.HasMediaKinds
            ? admitted.TokenNames.ToHashSet(StringComparer.Ordinal)
            : ledger.Registered<IMediaShapeProvider>()
                .SelectMany(shape => shape.Shape.Tokens)
                .Select(token => token.Name)
                .ToHashSet(StringComparer.Ordinal);

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
            var declaredTokens = manifest.Tokens.Select(token => token.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var name in declaredTokens.Except(definedTokens, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                found.Add($"'{name}' is declared in the manifest but no {authority} defines it.");
            }

            foreach (var name in definedTokens.Except(declaredTokens, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                found.Add($"'{name}' is defined by a {authority} but is not declared in the manifest.");
            }
        }

        defects = found;
        return found.Count == 0;
    }

    /// <summary>
    /// Works out which tokens each of an extension's media kinds owns.
    /// </summary>
    /// <remarks>
    /// One request per kind, carrying that kind's own tokens. The admitted projection answers when the host
    /// admitted anything; the registered legacy shape answers when it did not; and an extension with neither
    /// falls back to its declaration, which can only produce claims for a manifest that declared tokens
    /// nothing defines — a state the previous step has already refused.
    /// </remarks>
    private static IReadOnlyList<TokenClaimRequest> TokenClaims(
        ValidatedManifest manifest,
        PluginRegistrationLedger ledger,
        AdmittedInventory admitted)
    {
        if (admitted.HasMediaKinds)
        {
            return [.. admitted.MediaKinds.Select(kind => new TokenClaimRequest(kind.Kind, kind.Tokens))];
        }

        var shapes = ledger.Registered<IMediaShapeProvider>();

        return shapes.Count > 0
            ? [.. shapes.Select(shape => new TokenClaimRequest(shape.Shape.Kind, shape.Shape.Tokens))]
            : [.. manifest.MediaKinds.Select(kind => new TokenClaimRequest(kind, manifest.Tokens))];
    }

    /// <remarks>
    /// <para>
    /// Provider identifiers are qualified by the extension that supplies them, so two extensions can never
    /// collide. Within one extension they can, and a media kind is claimed across the whole installation, so
    /// both are checked here.
    /// </para>
    /// <para>
    /// The kinds compared are the ones actually supplied on both sides — this extension's admitted
    /// inventory against every active extension's — and fall back to a declaration only for an extension no
    /// host admission has run for. Comparing declarations would let an extension claim a kind it never
    /// supplies, and would miss one it supplies without declaring.
    /// </para>
    /// </remarks>
    private bool TryCheckIdentifiers(
        ValidatedManifest manifest,
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
            foreach (var kind in SuppliedKinds(result.Admitted, result.Manifest!.MediaKinds))
            {
                claimed[kind.Value] = result.Manifest!.Id;
            }
        }

        foreach (var kind in SuppliedKinds(admitted, manifest.MediaKinds))
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

    /// <summary>
    /// The media kinds an extension actually supplies.
    /// </summary>
    /// <param name="admitted">What the host admitted for it.</param>
    /// <param name="declared">What its declaration claimed.</param>
    /// <returns>The admitted kinds, or the declared ones when no admission has run for it.</returns>
    private static IReadOnlyList<MediaKindId> SuppliedKinds(
        AdmittedInventory admitted,
        IReadOnlyList<MediaKindId> declared)
        => admitted.HasMediaKinds ? admitted.Kinds : declared;

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
