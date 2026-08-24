using System.Globalization;
using System.IO;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Plugins;
using Arronix.Common.Naming;
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
        PluginManifest declaration,
        PluginId id,
        SemanticVersion version,
        VersionRange contractRange,
        CapabilitySet declaredCapabilities,
        IReadOnlyList<MediaKindId> mediaKinds,
        IReadOnlyList<NamingToken> tokens,
        string entryAssembly,
        PolicyGraph policies)
    {
        Declaration = declaration;
        Id = id;
        Version = version;
        ContractRange = contractRange;
        DeclaredCapabilities = declaredCapabilities;
        GrantedCapabilities = declaredCapabilities.WithImplied();
        MediaKinds = mediaKinds;
        Tokens = tokens;
        EntryAssembly = entryAssembly;
        Policies = policies;
    }

    /// <summary>
    /// Gets the declaration exactly as it was written.
    /// </summary>
    public PluginManifest Declaration { get; }

    /// <summary>
    /// Gets the extension identifier.
    /// </summary>
    public PluginId Id { get; }

    /// <summary>
    /// Gets the extension's own version.
    /// </summary>
    public SemanticVersion Version { get; }

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
    public IReadOnlyList<MediaKindId> MediaKinds { get; }

    /// <summary>
    /// Gets the declared naming tokens.
    /// </summary>
    public IReadOnlyList<NamingToken> Tokens { get; }

    /// <summary>
    /// Gets the bare file name of the assembly holding the entry module.
    /// </summary>
    public string EntryAssembly { get; }

    /// <summary>
    /// Gets the declared policy identifiers, never <see langword="null"/>.
    /// </summary>
    public PolicyGraph Policies { get; }
}

/// <summary>
/// Checks everything about a declaration that can be checked without loading any code.
/// </summary>
/// <remarks>
/// Everything here runs before an assembly load context exists, which is the point: the cheapest failure to
/// diagnose is the one that happens before a single type initializer has run.
/// </remarks>
public static class PluginManifestValidator
{
    /// <summary>The only manifest format version this loader understands.</summary>
    public const int SupportedSchemaVersion = 0;

    /// <summary>
    /// Proves a declaration well-formed.
    /// </summary>
    /// <param name="manifest">The declaration to check.</param>
    /// <param name="validated">The proved declaration on success; otherwise <see langword="null"/>.</param>
    /// <param name="defects">Everything wrong with it, or an empty list on success.</param>
    /// <returns><see langword="true"/> when the declaration is well-formed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Every defect is reported, not the first. An operator fixing a manifest one error per restart is an
    /// operator the loader has failed.
    /// </remarks>
    public static bool TryValidate(
        PluginManifest manifest,
        out ValidatedManifest? validated,
        out IReadOnlyList<ManifestDefect> defects)
    {
        ArgumentNullException.ThrowIfNull(manifest);

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
        var capabilities = ValidateCapabilities(manifest, found);
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
            manifest,
            id!.Value,
            version!.Value,
            range!,
            capabilities,
            mediaKinds,
            tokens,
            entryAssembly!,
            policies);

        return true;
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
    /// The entry assembly is resolved inside the extension's own folder, so a value carrying a directory
    /// separator or a parent-directory segment is rejected outright. An identifier that can escape its own
    /// folder is not an identifier, and neither is a file name.
    /// </remarks>
    private static string? ValidateEntryAssembly(PluginManifest manifest, List<ManifestDefect> found)
    {
        var value = manifest.EntryAssembly;

        if (string.IsNullOrWhiteSpace(value))
        {
            found.Add(new ManifestDefect("entryAssembly", "The entry assembly must not be blank.", CoreErrorCode.PluginManifestInvalid));
            return null;
        }

        // Both separators are rejected on every platform, not only the ones the running platform treats as
        // separators. A manifest is written once and read wherever the host runs, so a value that is a bare
        // file name here and a path traversal on another operating system is not a portable declaration.
        var escapes = value.Contains("..", StringComparison.Ordinal)
            || value.IndexOfAny(['/', '\\', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar]) >= 0;

        if (escapes)
        {
            found.Add(new ManifestDefect(
                "entryAssembly",
                $"'{value}' must be a bare file name inside the extension's own folder.",
                CoreErrorCode.PluginManifestInvalid));
            return null;
        }

        if (!value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            found.Add(new ManifestDefect(
                "entryAssembly",
                $"'{value}' must name a managed assembly file.",
                CoreErrorCode.PluginManifestInvalid));
            return null;
        }

        return value;
    }

    private static CapabilitySet ValidateCapabilities(PluginManifest manifest, List<ManifestDefect> found)
    {
        var declared = manifest.Capabilities;

        if (declared is null || declared.Count == 0)
        {
            found.Add(new ManifestDefect(
                "capabilities",
                "An extension must declare at least one capability. An extension that declares nothing can contribute nothing.",
                CoreErrorCode.PluginManifestInvalid));
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
        var kinds = manifest.MediaKinds;
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
        var tokens = manifest.Tokens;
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
        var identifiers = manifest.Identifiers;
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

    private static PolicyGraph ValidatePolicies(PluginManifest manifest, List<ManifestDefect> found)
    {
        var policies = manifest.Policies ?? new PolicyGraph();

        foreach (var (category, ids) in policies.Categories().Select(entry => (entry.Key, entry.Value)))
        {
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

        return policies;
    }
}
