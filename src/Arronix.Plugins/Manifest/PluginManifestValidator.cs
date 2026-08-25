using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Common.Naming;
using Arronix.Plugins.Dependencies;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Versioning;


namespace Arronix.Plugins.Manifest;

/// <summary>
/// One thing wrong with a declaration.
/// </summary>
/// <param name="Path">The member at fault, in dotted form.</param>
/// <param name="Message">What is wrong with it, in terms an operator can act on.</param>
/// <param name="Code">The failure class the defect belongs to.</param>
public readonly record struct ManifestDefect(string Path, string Message, CoreErrorCode Code)
{
    /// <summary>
    /// Gets the defect in the form it is written into a failure message.
    /// </summary>
    /// <returns>The defect text.</returns>
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>
/// A declaration that has been proved well-formed.
/// </summary>
/// <remarks>
/// Parse, do not validate. Every string the manifest carried that has a stronger type has been converted
/// once, here, at the boundary. Downstream the loader cannot write a lookup that fails, because there are
/// no lookups left: the identifier is an identifier, the version is a version, the range is a parsed range
/// and the capabilities are a set.
/// </remarks>
public sealed class ValidatedManifest
{
    internal ValidatedManifest(
        string name,
        string? description,
        InstalledPackage package,
        VersionRange contractRange,
        CapabilitySet declaredCapabilities,
        IReadOnlyList<MediaKindId> mediaKinds,
        IReadOnlyList<NamingToken> tokens,
        ValidatedPolicies policies)
    {
        Name = name;
        Description = description;
        Package = package;
        ContractRange = contractRange;
        DeclaredCapabilities = declaredCapabilities;
        GrantedCapabilities = declaredCapabilities.WithImplied();
        MediaKinds = mediaKinds.ToList().AsReadOnly();
        Tokens = tokens.ToList().AsReadOnly();
        Policies = policies;
    }

    /// <summary>
    /// Gets the canonical installed-package snapshot this declaration proved.
    /// </summary>
    /// <remarks>
    /// The package identity, version, source, folder, entry assembly, published contracts and requirements
    /// are stored here and nowhere else. Everything from resolution through teardown consumes this exact
    /// object, so no second description of the package can drift from it.
    /// </remarks>
    internal InstalledPackage Package { get; }

    /// <summary>Gets the name shown to an operator.</summary>
    public string Name { get; }

    /// <summary>Gets the optional sentence describing what the extension is for.</summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the extension identifier, from the canonical package snapshot.
    /// </summary>
    public PluginId Id => Package.Id;

    /// <summary>
    /// Gets the extension's own version, from the canonical package snapshot.
    /// </summary>
    public SemanticVersion Version => Package.Version;

    /// <summary>
    /// Gets the contract range the extension accepts.
    /// </summary>
    public VersionRange ContractRange { get; }

    /// <summary>
    /// Gets the capabilities exactly as declared, before implication.
    /// </summary>
    /// <remarks>
    /// The forward check runs against these rather than against the granted set, so that an implied
    /// privilege is never reported as an undeclared one.
    /// </remarks>
    public CapabilitySet DeclaredCapabilities { get; }

    /// <summary>
    /// Gets the capabilities actually granted, after implication.
    /// </summary>
    public CapabilitySet GrantedCapabilities { get; }

    /// <summary>
    /// Gets the media kinds the extension claims.
    /// </summary>
    public ReadOnlyCollection<MediaKindId> MediaKinds { get; }

    /// <summary>
    /// Gets the declared naming tokens.
    /// </summary>
    public ReadOnlyCollection<NamingToken> Tokens { get; }

    /// <summary>
    /// Gets the bare file name of the assembly holding the entry module, or <see langword="null"/> when
    /// the package carries no executable behavior.
    /// </summary>
    public string? EntryAssembly => Package.EntryAssemblyFileName;

    /// <summary>
    /// Gets the bare file names of the assemblies this package publishes for its dependants, never
    /// containing the entry assembly.
    /// </summary>
    public IReadOnlyList<string> ContractAssemblies => Package.ContractAssemblies;

    /// <summary>
    /// Gets the bare file names this package permits a browser client to download, always a subset of
    /// <see cref="ContractAssemblies"/>.
    /// </summary>
    public IReadOnlyList<string> ClientContracts => Package.ClientContractAssemblies;

    /// <summary>
    /// Gets the packages this one requires, each an exact identifier and one proved range.
    /// </summary>
    /// <remarks>
    /// Internal because a resolved requirement is loader infrastructure rather than authoring vocabulary:
    /// an extension writes the declaration, and what the platform makes of it across a whole installation
    /// is not a fact the extension is entitled to a typed view of.
    /// </remarks>
    internal IReadOnlyList<PackageRequirement> Dependencies => Package.Requirements;

    /// <summary>
    /// Gets the proved policy identifiers, never <see langword="null"/>.
    /// </summary>
    public ValidatedPolicies Policies { get; }
}

/// <summary>
/// Checks everything about a declaration that can be checked without loading any code.
/// </summary>
/// <remarks>
/// <para>
/// Everything here runs before an assembly load context exists, which is the point: the cheapest failure to
/// diagnose is the one that happens before a single type initializer has run.
/// </para>
/// <para>
/// Omitting a list member and writing <c>null</c> for it are different statements. Omission takes the
/// property default; an explicit null is malformed input and is reported as a defect against its own member
/// path, so neither a null reference nor a silently erased declaration reaches the loader.
/// </para>
/// </remarks>
public static class PluginManifestValidator
{
    /// <summary>The only manifest format version this loader understands.</summary>
    /// <remarks>
    /// One value, always. The repository is pre-alpha and clean-sheet, so a format version this loader once
    /// understood and no longer does is a format nothing has ever shipped; carrying a reader for it would be
    /// carrying a compatibility layer for an installed base that does not exist.
    /// </remarks>
    public const int SupportedSchemaVersion = 1;

    /// <summary>
    /// Proves a declaration well-formed and builds the installation's canonical snapshot of the package.
    /// </summary>
    /// <param name="candidate">Where the declaration was found and what it says.</param>
    /// <param name="availability">
    /// Whether an operator has switched the package off. A typed state rather than a setting: the loader
    /// translates configuration into it once, and resolution reads the state.
    /// </param>
    /// <param name="validated">The proved declaration on success; otherwise <see langword="null"/>.</param>
    /// <param name="defects">Everything wrong with it, or an empty list on success.</param>
    /// <returns><see langword="true"/> when the declaration is well-formed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidate"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="availability"/> is not a defined state.</exception>
    /// <remarks>
    /// Every defect is reported, not the first. An operator fixing a manifest one error per restart is an
    /// operator the loader has failed.
    /// </remarks>
    internal static bool TryValidate(
        PluginCandidate candidate,
        PackageAvailability availability,
        out ValidatedManifest? validated,
        out IReadOnlyList<ManifestDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        PackageAvailabilityReason.Required(availability, nameof(availability));

        var manifest = candidate.Manifest;
        var found = new List<ManifestDefect>();
        validated = null;

        if (manifest.SchemaVersion != SupportedSchemaVersion)
        {
            found.Add(new ManifestDefect(
                "schemaVersion",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Manifest format version {manifest.SchemaVersion} is not understood; this host reads version {SupportedSchemaVersion}."),
                CoreErrorCode.PluginManifestInvalid));
        }

        var id = ValidateId(manifest, found);
        ValidateName(manifest, found);
        var version = ValidateVersion(manifest, found);
        var range = ValidateContractRange(manifest, found);
        var entryAssembly = ValidateEntryAssembly(manifest, found);
        var contractAssemblies = ValidateContractAssemblies(manifest, found);
        var clientContracts = ValidateClientContracts(manifest, contractAssemblies, found);
        var dependencies = ValidateDependencies(manifest, id, found);
        var capabilities = ValidateCapabilities(manifest, found);
        ValidatePackageShape(manifest, found);
        var mediaKinds = ValidateMediaKinds(manifest, capabilities, found);
        var tokens = ValidateTokens(manifest, found);
        ValidateIdentifiers(manifest, found);
        var policies = ValidatePolicies(manifest, found);

        if (found.Count > 0)
        {
            defects = found;
            return false;
        }

        defects = [];
        validated = new ValidatedManifest(
            manifest.Name,
            manifest.Description,
            new InstalledPackage(
                id!.Value,
                version!.Value,
                candidate.ManifestPath,
                candidate.Folder,
                entryAssembly,
                contractAssemblies,
                dependencies,
                availability,
                clientContracts),
            range!,
            capabilities,
            mediaKinds,
            tokens,
            policies);

        return true;
    }

    /// <summary>
    /// Reads a declared list, refusing an explicitly supplied <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The member type.</typeparam>
    /// <param name="declared">The member as read.</param>
    /// <param name="path">The member path a defect is reported against.</param>
    /// <param name="found">Where a defect is recorded.</param>
    /// <returns>The list, or an empty one so the remaining rules still run.</returns>
    /// <remarks>
    /// Omitting a list member and writing <c>null</c> for it are different statements. Omission takes the
    /// property default; an explicit null is malformed input, and reading it as "empty" would silently erase
    /// whatever the author meant. Returning empty afterwards is what lets validation report every other
    /// defect in the same pass instead of throwing out of the loader.
    /// </remarks>
    private static IReadOnlyList<T> RequireList<T>(
        IReadOnlyList<T>? declared,
        string path,
        List<ManifestDefect> found)
    {
        if (declared is not null)
        {
            return declared;
        }

        found.Add(new ManifestDefect(
            path,
            "The member is null. Omit it, or supply a list.",
            CoreErrorCode.PluginManifestInvalid));
        return [];
    }

    private static PluginId? ValidateId(PluginManifest manifest, List<ManifestDefect> found)
    {
        if (PluginId.TryParse(manifest.Id, out var id))
        {
            return id;
        }

        found.Add(new ManifestDefect(
            "id",
            $"'{manifest.Id}' is not a well-formed extension identifier. Use lower-case alphanumeric segments separated by dots, starting with a letter.",
            CoreErrorCode.PluginManifestInvalid));
        return null;
    }

    private static void ValidateName(PluginManifest manifest, List<ManifestDefect> found)
    {
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            found.Add(new ManifestDefect("name", "The name must not be blank.", CoreErrorCode.PluginManifestInvalid));
        }
    }

    private static SemanticVersion? ValidateVersion(PluginManifest manifest, List<ManifestDefect> found)
    {
        if (SemanticVersion.TryParse(manifest.Version, out var version))
        {
            return version;
        }

        found.Add(new ManifestDefect(
            "version",
            $"'{manifest.Version}' is not a well-formed version.",
            CoreErrorCode.PluginManifestInvalid));
        return null;
    }

    private static VersionRange? ValidateContractRange(PluginManifest manifest, List<ManifestDefect> found)
    {
        if (VersionRangeParser.TryParse(manifest.Contracts?.Arronix, out var range, out var error))
        {
            return range;
        }

        found.Add(new ManifestDefect("contracts.arronix", error!, CoreErrorCode.PluginManifestInvalid));
        return null;
    }

    /// <remarks>
    /// <para>
    /// An omitted entry assembly is a package with no executable behavior, which is a legitimate shape and
    /// is checked as one by <see cref="ValidatePackageShape"/>. A member that is present but blank is a
    /// mistake rather than an omission, and is reported as one.
    /// </para>
    /// <para>
    /// The entry assembly is resolved inside the extension's own folder, so a value carrying a directory
    /// separator or a parent-directory segment is rejected outright. An identifier that can escape its own
    /// folder is not an identifier, and neither is a file name.
    /// </para>
    /// </remarks>
    private static string? ValidateEntryAssembly(PluginManifest manifest, List<ManifestDefect> found)
        => manifest.EntryAssembly is null
            ? null
            : ValidateAssemblyFileName(manifest.EntryAssembly, "entryAssembly", "entry assembly", found);

    /// <summary>
    /// Proves the assemblies a package publishes for its dependants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entry assembly may not appear here. Sharing an assembly is a statement that its types are one
    /// identity across every package that binds to them, and the module, parser and provider
    /// implementations shipped in an entry assembly have a different isolation, update and unload lifetime
    /// from the contracts a dependant compiles against. A package that wants both publishes the contracts
    /// from an assembly of their own.
    /// </para>
    /// <para>
    /// Two spellings that differ only in case are one duplicate rather than two entries, because a manifest
    /// is written once and read wherever the host runs: a list that names one file on Windows and two on
    /// Linux is not a portable declaration.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> ValidateContractAssemblies(
        PluginManifest manifest,
        List<ManifestDefect> found)
    {
        var declared = RequireList(manifest.ContractAssemblies, "contractAssemblies", found);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(declared.Count);

        for (var index = 0; index < declared.Count; index++)
        {
            var path = $"contractAssemblies[{index}]";
            var value = ValidateAssemblyFileName(declared[index], path, "contract assembly", found);

            if (value is null)
            {
                continue;
            }

            if (!seen.Add(value))
            {
                found.Add(new ManifestDefect(
                    path,
                    $"'{value}' is published more than once.",
                    CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            if (manifest.EntryAssembly is { } entry && string.Equals(value, entry, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(new ManifestDefect(
                    path,
                    $"'{value}' is this package's entry assembly and cannot also be a shared contract assembly. Publish the contracts a dependant binds to from an assembly of their own.",
                    CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            result.Add(value);
        }

        return result;
    }

    /// <summary>
    /// Proves the subset of published contracts this package permits a browser to download.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Checked against the contract list this validator just proved rather than against the raw
    /// declaration, so a client contract naming an assembly whose own spelling was refused is reported once,
    /// against the member that names it, instead of twice.
    /// </para>
    /// <para>
    /// The subset rule is the whole facet. Host admits a shared contract assembly once per installation and
    /// hands every dependant the same assembly object; a browser that received a file outside that set would
    /// be holding a type identity no part of the installation is bound to, which is the one thing sharing a
    /// contract exists to prevent.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> ValidateClientContracts(
        PluginManifest manifest,
        IReadOnlyList<string> contractAssemblies,
        List<ManifestDefect> found)
    {
        var declared = RequireList(manifest.ClientContracts, "clientContracts", found);
        var published = new HashSet<string>(contractAssemblies, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(declared.Count);

        for (var index = 0; index < declared.Count; index++)
        {
            var path = $"clientContracts[{index}]";
            var value = ValidateAssemblyFileName(declared[index], path, "client contract assembly", found);

            if (value is null)
            {
                continue;
            }

            if (!seen.Add(value))
            {
                found.Add(new ManifestDefect(
                    path,
                    $"'{value}' is offered to clients more than once.",
                    CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            if (!published.Contains(value))
            {
                found.Add(new ManifestDefect(
                    path,
                    $"'{value}' is offered to clients but is not one of this package's shared contract assemblies. A client receives the same admitted identity a dependant binds to, or nothing.",
                    CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            result.Add(value);
        }

        return result;
    }

    /// <summary>
    /// Proves the packages this one requires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A dependency is an exact identifier and one range, and both are read here so that nothing downstream
    /// holds a package identifier that was never parsed or a range that was never proved. The one range
    /// grammar is <see cref="VersionRangeParser"/>'s; there is no second reader.
    /// </para>
    /// <para>
    /// One identifier is stated at most once. Two statements about one package are two things the author
    /// wrote, at least one of which is not what they meant: intersecting them produces a third range neither
    /// statement said, and taking either one is last-writer-wins. The graph refuses the same input on its
    /// own terms, because it is total over the candidates it is handed; this is where a real manifest meets
    /// the rule, with the member to fix named.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<PackageRequirement> ValidateDependencies(
        PluginManifest manifest,
        PluginId? self,
        List<ManifestDefect> found)
    {
        var declared = RequireList(manifest.Dependencies, "dependencies", found);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PackageRequirement>(declared.Count);

        for (var index = 0; index < declared.Count; index++)
        {
            var declaration = declared[index];

            if (declaration is null)
            {
                found.Add(new ManifestDefect(
                    $"dependencies[{index}]",
                    "A dependency must name a package and a range.",
                    CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            var package = ValidateDependencyPackage(declaration, index, self, seen, found);
            var range = ValidateDependencyRange(declaration, index, found);

            if (package is { } id && range is not null)
            {
                result.Add(new PackageRequirement(id, range));
            }
        }

        return result;
    }

    private static PluginId? ValidateDependencyPackage(
        PackageDependencyDeclaration declaration,
        int index,
        PluginId? self,
        HashSet<string> seen,
        List<ManifestDefect> found)
    {
        var path = $"dependencies[{index}].package";

        if (!PluginId.TryParse(declaration.Package, out var package))
        {
            found.Add(new ManifestDefect(
                path,
                $"'{declaration.Package}' is not a well-formed package identifier. Use lower-case alphanumeric segments separated by dots, starting with a letter.",
                CoreErrorCode.PluginManifestInvalid));
            return null;
        }

        if (self is { } id && package == id)
        {
            found.Add(new ManifestDefect(
                path,
                $"'{package}' is this package. A package's own assemblies are already available to it.",
                CoreErrorCode.PluginManifestInvalid));
            return null;
        }

        if (!seen.Add(package.Value))
        {
            found.Add(new ManifestDefect(
                path,
                $"'{package}' is required more than once. State each dependency once: two declared ranges are never intersected and never chosen between.",
                CoreErrorCode.PluginManifestInvalid));
            return null;
        }

        return package;
    }

    private static VersionRange? ValidateDependencyRange(
        PackageDependencyDeclaration declaration,
        int index,
        List<ManifestDefect> found)
    {
        if (VersionRangeParser.TryParse(declaration.Range, out var range, out var error))
        {
            return range;
        }

        found.Add(new ManifestDefect(
            $"dependencies[{index}].range",
            error!,
            CoreErrorCode.PluginManifestInvalid));
        return null;
    }

    /// <summary>
    /// Checks the two package shapes against each other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A package carries zero or more shared contract assemblies and zero or one entry assembly, and it must
    /// carry at least one of the two: a package that publishes nothing and runs nothing contributes nothing.
    /// </para>
    /// <para>
    /// A privilege is a statement about what code will be allowed to do. A package with no entry assembly
    /// runs no code of its own, so it can hold none — and the forward capability check would quarantine it
    /// for the same reason one step later, without a member to point at.
    /// </para>
    /// </remarks>
    private static void ValidatePackageShape(PluginManifest manifest, List<ManifestDefect> found)
    {
        var declaredCapabilities = manifest.Capabilities?.Count ?? 0;
        var publishedAssemblies = manifest.ContractAssemblies?.Count ?? 0;

        if (manifest.EntryAssembly is not null)
        {
            if (declaredCapabilities == 0)
            {
                found.Add(new ManifestDefect(
                    "capabilities",
                    "An extension must declare at least one capability. An extension that declares nothing can contribute nothing.",
                    CoreErrorCode.PluginManifestInvalid));
            }

            return;
        }

        if (publishedAssemblies == 0)
        {
            found.Add(new ManifestDefect(
                "entryAssembly",
                "A package must carry an entry assembly, shared contract assemblies, or both. A package that carries neither contributes nothing.",
                CoreErrorCode.PluginManifestInvalid));
        }

        if (declaredCapabilities > 0)
        {
            found.Add(new ManifestDefect(
                "capabilities",
                "A package with no entry assembly runs no code and can hold no privilege. Remove the capability, or name the assembly holding its entry module.",
                CoreErrorCode.PluginManifestInvalid));
        }
    }

    /// <remarks>
    /// Both separators are rejected on every platform, not only the ones the running platform treats as
    /// separators. A manifest is written once and read wherever the host runs, so a value that is a bare
    /// file name here and a path traversal on another operating system is not a portable declaration.
    /// </remarks>
    private static string? ValidateAssemblyFileName(
        string? value,
        string path,
        string subject,
        List<ManifestDefect> found)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            found.Add(new ManifestDefect(
                path,
                $"The {subject} must not be blank.",
                CoreErrorCode.PluginManifestInvalid));
            return null;
        }

        if (!PackageFileName.IsBare(value))
        {
            found.Add(new ManifestDefect(
                path,
                $"'{value}' must be a bare file name inside the extension's own folder.",
                CoreErrorCode.PluginManifestInvalid));
            return null;
        }

        if (!value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            found.Add(new ManifestDefect(
                path,
                $"'{value}' must name a managed assembly file.",
                CoreErrorCode.PluginManifestInvalid));
            return null;
        }

        return value;
    }

    private static CapabilitySet ValidateCapabilities(PluginManifest manifest, List<ManifestDefect> found)
    {
        var declared = RequireList(manifest.Capabilities, "capabilities", found);

        // Whether the list may be empty is a question about the package's shape rather than about the
        // capability vocabulary, and is answered once by ValidatePackageShape.
        if (declared.Count == 0)
        {
            return CapabilitySet.None;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var set = CapabilitySet.None;

        for (var index = 0; index < declared.Count; index++)
        {
            var name = declared[index];

            if (!seen.Add(name ?? string.Empty))
            {
                found.Add(new ManifestDefect(
                    $"capabilities[{index}]",
                    $"'{name}' is declared more than once.",
                    CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            if (!CapabilityNames.TryParse(name, out var capability))
            {
                found.Add(new ManifestDefect(
                    $"capabilities[{index}]",
                    $"'{name}' is not a capability this host grants.",
                    CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            set = set.Union(CapabilitySet.Of(capability));
        }

        return set;
    }

    private static IReadOnlyList<MediaKindId> ValidateMediaKinds(
        PluginManifest manifest,
        CapabilitySet capabilities,
        List<ManifestDefect> found)
    {
        var kinds = RequireList(manifest.MediaKinds, "mediaKinds", found);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<MediaKindId>(kinds.Count);

        for (var index = 0; index < kinds.Count; index++)
        {
            var kind = kinds[index];

            if (string.IsNullOrWhiteSpace(kind))
            {
                found.Add(new ManifestDefect($"mediaKinds[{index}]", "A media kind must not be blank.", CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            if (!seen.Add(kind))
            {
                found.Add(new ManifestDefect($"mediaKinds[{index}]", $"'{kind}' is declared more than once.", CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            result.Add(MediaKindId.FromString(kind));
        }

        // Claiming a media kind and holding the privilege to contribute one are the same statement written
        // twice. Letting them disagree would leave a kind nothing is permitted to supply a shape for.
        //
        // The converse is deliberately not checked. Which kinds an extension supplies is derived from the
        // types it registers, so a manifest holding the privilege and naming no kind is stating the
        // privilege once rather than omitting a fact: whether it supplies one, and which, is settled after
        // load against the kind the host actually admitted. Requiring the list here would make the manifest
        // a second media schema, which is the thing it must not become. The privilege itself stays an
        // explicit manifest-owned request, because least privilege cannot be derived from code that has not
        // been allowed to run yet.
        if (result.Count > 0 && !capabilities.Has(Capability.MediaKind))
        {
            found.Add(new ManifestDefect(
                "mediaKinds",
                $"Claiming a media kind requires the '{CapabilityNames.MediaKind}' capability.",
                CoreErrorCode.PluginManifestInvalid));
        }

        return result;
    }

    /// <remarks>
    /// A token is checked for shape only. What it <i>means</i> cannot be checked here: that comparison
    /// happens later, against the shape the extension registers, and against the tokens the host reserves.
    /// </remarks>
    private static IReadOnlyList<NamingToken> ValidateTokens(PluginManifest manifest, List<ManifestDefect> found)
    {
        var tokens = RequireList(manifest.Tokens, "tokens", found);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<NamingToken>(tokens.Count);

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var name = token?.Name;

            if (string.IsNullOrWhiteSpace(name))
            {
                found.Add(new ManifestDefect($"tokens[{index}].name", "A token name must not be blank.", CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            if (name.Length < 3 || name[0] != '{' || name[^1] != '}' || name.AsSpan(1, name.Length - 2).ContainsAny('{', '}'))
            {
                found.Add(new ManifestDefect(
                    $"tokens[{index}].name",
                    $"'{name}' is not a well-formed token. A token is a single brace-delimited name, for example '{{Title}}'.",
                    CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            var canonical = NamingTokenName.Canonicalize(name);

            if (canonical.Length == 0)
            {
                found.Add(new ManifestDefect(
                    $"tokens[{index}].name",
                    $"'{name}' contains no letter or digit and therefore has no naming-grammar identity.",
                    CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            if (!seen.Add(canonical))
            {
                found.Add(new ManifestDefect(
                    $"tokens[{index}].name",
                    $"'{name}' is equivalent to a token already declared under the naming grammar.",
                    CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            result.Add(token!);
        }

        return result;
    }

    private static void ValidateIdentifiers(PluginManifest manifest, List<ManifestDefect> found)
    {
        var identifiers = RequireList(manifest.Identifiers, "identifiers", found);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < identifiers.Count; index++)
        {
            var identifier = identifiers[index];

            if (string.IsNullOrWhiteSpace(identifier))
            {
                found.Add(new ManifestDefect($"identifiers[{index}]", "An identifier scheme must not be blank.", CoreErrorCode.PluginManifestInvalid));
                continue;
            }

            if (!seen.Add(identifier))
            {
                found.Add(new ManifestDefect($"identifiers[{index}]", $"'{identifier}' is declared more than once.", CoreErrorCode.PluginManifestInvalid));
            }
        }
    }

    /// <remarks>
    /// Every category goes through the same explicit-null rule the other list members do, and the proved
    /// values are copied into an immutable snapshot: the deserialized graph is not retained.
    /// </remarks>
    private static ValidatedPolicies ValidatePolicies(PluginManifest manifest, List<ManifestDefect> found)
    {
        var declared = manifest.Policies;

        if (declared is null)
        {
            return ValidatedPolicies.Empty;
        }

        var proved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var name in PolicyGraph.CategoryNames)
        {
            proved[name] = [];
        }

        foreach (var (category, declaredIds) in Declared(declared))
        {
            var ids = RequireList(declaredIds, $"policies.{category}", found);
            proved[category] = ids;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < ids.Count; index++)
            {
                var policyId = ids[index];

                if (string.IsNullOrWhiteSpace(policyId))
                {
                    found.Add(new ManifestDefect(
                        $"policies.{category}[{index}]",
                        "A policy identifier must not be blank.",
                        CoreErrorCode.PluginPolicyDeclarationInvalid));
                    continue;
                }

                if (!seen.Add(policyId))
                {
                    found.Add(new ManifestDefect(
                        $"policies.{category}[{index}]",
                        $"'{policyId}' is declared more than once in this category.",
                        CoreErrorCode.PluginPolicyDeclarationInvalid));
                }
            }
        }

        return new ValidatedPolicies(
            proved["parsing"],
            proved["matching"],
            proved["quality"],
            proved["import"],
            proved["naming"]);
    }

    /// <summary>Reads every category off the declaration without dereferencing a null one.</summary>
    private static IEnumerable<(string Category, IReadOnlyList<string>? Ids)> Declared(PolicyGraph policies)
    {
        yield return ("parsing", policies.Parsing);
        yield return ("matching", policies.Matching);
        yield return ("quality", policies.Quality);
        yield return ("import", policies.Import);
        yield return ("naming", policies.Naming);
    }
}
