using System.Globalization;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;

namespace Arronix.Client.Contracts;

/// <summary>
/// Proves that a contract manifest describes an installation before anything acts on it.
/// </summary>
/// <remarks>
/// <para>
/// The manifest is untrusted input, and deserializing is not the same as making sense. Downstream code
/// indexes packages by identifier, walks closures, keys assemblies by simple name, and fetches whatever a
/// length and a hash name; each step has a quietly wrong answer for a merely well-formed document. A
/// duplicate identifier replaces an entry, a duplicate simple name overwrites a verification result, a
/// closure missing its own package loads dependencies and never the dependant.
/// </para>
/// <para>
/// So the document is checked once, whole, and a bad one yields one refusal naming the first defect. A
/// description this client cannot trust to be self-consistent is not one it may act on part of.
/// </para>
/// </remarks>
internal static class ContractManifestValidator
{
    /// <summary>
    /// Describes what is wrong with a manifest, or <see langword="null"/> when nothing is.
    /// </summary>
    /// <param name="manifest">The document the host answered with.</param>
    /// <returns>One sentence naming the defect, or <see langword="null"/>.</returns>
    internal static string? Describe(ClientContractManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (string.IsNullOrWhiteSpace(manifest.ContractIdentity))
        {
            return "it states no universal contract identity.";
        }

        if (!IsSha256(manifest.InstallationHash))
        {
            return "its installation hash is not a SHA-256 value.";
        }

        if (manifest.Packages is null || manifest.Refused is null)
        {
            return "one of its lists is absent rather than empty.";
        }

        var identifiers = new HashSet<PluginId>();
        var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in manifest.Packages)
        {
            if (package is null)
            {
                return "one of its packages is null.";
            }

            if (package.Id == default)
            {
                return "a package states no identifier.";
            }

            if (!identifiers.Add(package.Id))
            {
                return $"package '{package.Id}' appears more than once, so one description of it would "
                    + "silently replace another.";
            }

            if (string.IsNullOrWhiteSpace(package.Version) || string.IsNullOrWhiteSpace(package.Name))
            {
                return $"package '{package.Id}' states no version or no name.";
            }

            if (!IsSha256(package.ClosureHash))
            {
                return $"package '{package.Id}' states a closure hash that is not a SHA-256 value.";
            }

            if (package.Assemblies is null || package.Closure is null)
            {
                return $"package '{package.Id}' has an absent list rather than an empty one.";
            }

            if (package.Assemblies.Count == 0)
            {
                return $"package '{package.Id}' offers no assembly, so there is nothing for a client to load.";
            }

            if (Describe(package, assemblyNames) is { } defect)
            {
                return defect;
            }
        }

        foreach (var package in manifest.Packages)
        {
            var closure = new HashSet<PluginId>();

            foreach (var member in package.Closure)
            {
                if (member == default)
                {
                    return $"package '{package.Id}' names an empty identifier in its closure.";
                }

                if (!closure.Add(member))
                {
                    return $"package '{package.Id}' names '{member}' in its closure more than once.";
                }

                if (!identifiers.Contains(member))
                {
                    return $"package '{package.Id}' names '{member}' in its closure, and this host published "
                        + "no such package. A client cannot load a closure with a hole in it.";
                }
            }

            // Last, not merely present. The closure is a load order, and a package that loads before one of
            // its own dependencies is a closure a client cannot follow.
            if (package.Closure.Count == 0 || package.Closure[^1] != package.Id)
            {
                return $"package '{package.Id}' is not the final member of its own closure, so a client "
                    + "following that closure would load it before something it binds to, or never.";
            }
        }

        var refused = new HashSet<PluginId>();

        foreach (var refusal in manifest.Refused)
        {
            if (refusal is null || refusal.MissingAssemblies is null || refusal.UnadmittedFiles is null
                || refusal.CausedBy is null)
            {
                return "one of its refusals is null or has an absent list.";
            }

            if (refusal.Package == default || string.IsNullOrWhiteSpace(refusal.Reason))
            {
                return "a refusal states no package or no reason.";
            }

            if (!refused.Add(refusal.Package))
            {
                return $"package '{refusal.Package}' is refused more than once.";
            }

            // Published and withheld are the two halves of one answer, and a package cannot be in both.
            if (identifiers.Contains(refusal.Package))
            {
                return $"package '{refusal.Package}' is both published to clients and withheld from them.";
            }

            if (Describe(refusal.Package, "assembly", refusal.MissingAssemblies, mustBeBare: false)
                is { } missingDefect)
            {
                return missingDefect;
            }

            if (Describe(refusal.Package, "file", refusal.UnadmittedFiles, mustBeBare: true)
                is { } fileDefect)
            {
                return fileDefect;
            }
        }

        // Blame is checked after every refusal is known, because a cascade names packages that may appear
        // later in the list.
        foreach (var refusal in manifest.Refused)
        {
            var causes = new HashSet<PluginId>();

            foreach (var cause in refusal.CausedBy)
            {
                if (cause == default)
                {
                    return $"refusal of '{refusal.Package}' names an empty cause.";
                }

                if (cause == refusal.Package)
                {
                    return $"refusal of '{refusal.Package}' blames itself, which explains nothing.";
                }

                if (!causes.Add(cause))
                {
                    return $"refusal of '{refusal.Package}' blames '{cause}' more than once.";
                }

                // A cause is a package that was itself withheld. A published package cannot be the reason
                // another one is not: it is in the list a client is being handed.
                if (!refused.Contains(cause))
                {
                    return $"refusal of '{refusal.Package}' blames '{cause}', which this host did not withhold.";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Proves one refusal's name list is non-blank, free of duplicates, and — for the file list — bare.
    /// </summary>
    /// <remarks>
    /// A refusal is rendered to an operator, so an unadmitted file name that is a path is a path this client
    /// would print as if the package had declared it. The declaration side rejects the same shape.
    /// </remarks>
    private static string? Describe(
        PluginId package,
        string kind,
        IReadOnlyList<string> names,
        bool mustBeBare)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return $"refusal of '{package}' names a blank {kind}.";
            }

            if (mustBeBare && !IsBareFileName(name))
            {
                return $"refusal of '{package}' names '{name}', which is not a bare {kind} name.";
            }

            if (!seen.Add(name))
            {
                return $"refusal of '{package}' names {kind} '{name}' more than once.";
            }
        }

        return null;
    }

    /// <summary>Proves one package's assemblies, and that no simple name is claimed twice anywhere.</summary>
    private static string? Describe(ClientContractPackage package, HashSet<string> assemblyNames)
    {
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in package.Assemblies)
        {
            if (assembly is null)
            {
                return $"package '{package.Id}' has a null assembly entry.";
            }

            if (string.IsNullOrWhiteSpace(assembly.AssemblyName) || string.IsNullOrWhiteSpace(assembly.Identity))
            {
                return $"package '{package.Id}' has an assembly with no simple name or no identity.";
            }

            // The simple name is the key this client checks the browser's own load context against, and the
            // identity is what it compares the bytes' metadata to. If they disagree, the occupancy check
            // asks about one assembly and the identity check answers about another.
            if (SimpleNameOf(assembly.Identity) is not { } declaredName)
            {
                return $"package '{package.Id}' states an identity for '{assembly.FileName}' that is not a "
                    + "readable assembly name.";
            }

            if (!string.Equals(declaredName, assembly.AssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return $"package '{package.Id}' offers '{assembly.AssemblyName}' with the identity "
                    + $"'{assembly.Identity}', whose simple name is '{declaredName}'.";
            }

            // Across the whole manifest, not per package. A simple name is what the runtime binds on and
            // what this client keys its verification results by, so two packages claiming one name is two
            // answers to a question that has one.
            if (!assemblyNames.Add(assembly.AssemblyName))
            {
                return $"'{assembly.AssemblyName}' is offered by more than one package, and a simple name "
                    + "binds to one assembly.";
            }

            if (!IsBareFileName(assembly.FileName))
            {
                return $"package '{package.Id}' names '{assembly.FileName}', which is not a bare file name.";
            }

            // A file name plus a content hash is an address. Two assemblies sharing one within a package
            // means two addresses that differ only in a hash the client is being asked to trust.
            if (!fileNames.Add(assembly.FileName))
            {
                return $"package '{package.Id}' offers '{assembly.FileName}' more than once.";
            }

            if (assembly.Length <= 0)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"package '{package.Id}' states a length of {assembly.Length} for '{assembly.FileName}'.");
            }

            if (!IsSha256(assembly.ContentHash))
            {
                return $"package '{package.Id}' states no readable content hash for '{assembly.FileName}'.";
            }

            if (assembly.ModuleVersionId == Guid.Empty)
            {
                return $"package '{package.Id}' states no module identifier for '{assembly.FileName}'.";
            }

            if (Describe(package, assembly) is { } declarationDefect)
            {
                return declarationDefect;
            }
        }

        return null;
    }

    /// <summary>Proves one assembly's declarations. Zero of them is valid.</summary>
    private static string? Describe(ClientContractPackage package, ClientContractAssembly assembly)
    {
        if (assembly.Declarations is null)
        {
            return $"package '{package.Id}' states no declaration list for '{assembly.FileName}'.";
        }

        var entryPoints = new HashSet<string>(StringComparer.Ordinal);
        var entities = new HashSet<string>(StringComparer.Ordinal);
        string? previous = null;

        foreach (var declaration in assembly.Declarations)
        {
            if (declaration is null)
            {
                return $"package '{package.Id}' has a null declaration for '{assembly.FileName}'.";
            }

            if (string.IsNullOrWhiteSpace(declaration.EntryPointType)
                || string.IsNullOrWhiteSpace(declaration.EntityTypeName))
            {
                return $"package '{package.Id}' declares a client contract for '{assembly.FileName}' with no "
                    + "entry point or no entity type.";
            }

            if (!IsDeclaredHash(declaration.GeneratedMetadataHash)
                || !IsDeclaredHash(declaration.ProjectionSchemaHash))
            {
                return $"package '{package.Id}' states a hash for '{declaration.EntryPointType}' that is not "
                    + "64 upper-case hexadecimal characters.";
            }

            // Non-decreasing here, unique below: together strictly sorted, and a repeat is still named as a
            // duplicate rather than as disorder.
            if (previous is not null && string.CompareOrdinal(previous, declaration.EntryPointType) > 0)
            {
                return $"package '{package.Id}' lists the declarations of '{assembly.FileName}' out of order.";
            }

            previous = declaration.EntryPointType;

            if (!entryPoints.Add(declaration.EntryPointType))
            {
                return $"package '{package.Id}' declares '{declaration.EntryPointType}' more than once for "
                    + $"'{assembly.FileName}'.";
            }

            if (!entities.Add(declaration.EntityTypeName))
            {
                return $"package '{package.Id}' declares two client contracts for "
                    + $"'{declaration.EntityTypeName}' in '{assembly.FileName}', so a consumer resolving one "
                    + "has no way to choose.";
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a name addresses a file inside the package's own folder and nowhere else.
    /// </summary>
    /// <remarks>
    /// Both separators are rejected on every platform, not only the ones the running platform treats as
    /// separators: the value is written by a host that may not be this machine, and a name that is bare
    /// there and a traversal here is not one a client should put in a URL.
    /// </remarks>
    private static bool IsBareFileName(string? name)
        => !string.IsNullOrWhiteSpace(name)
            && !name.Contains('/', StringComparison.Ordinal)
            && !name.Contains('\\', StringComparison.Ordinal)
            && !name.Contains(':', StringComparison.Ordinal)
            && name != "."
            && name != "..";

    /// <summary>Reads the simple name out of a rendered assembly identity, or nothing.</summary>
    private static string? SimpleNameOf(string identity)
    {
        try
        {
            return new AssemblyName(identity).Name;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (System.IO.FileLoadException)
        {
            return null;
        }
    }

    private static bool IsSha256(string? hash)
        => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    /// <summary>Whether text is a declared hash: exactly 64 upper-case hexadecimal characters.</summary>
    /// <remarks>
    /// Stricter than <see cref="IsSha256"/> on purpose: a declared hash is compared ordinally against the
    /// literal in the payload's own metadata, where the other case is a mismatch.
    /// </remarks>
    private static bool IsDeclaredHash(string? hash)
        => hash is { Length: 64 }
            && hash.All(static character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
