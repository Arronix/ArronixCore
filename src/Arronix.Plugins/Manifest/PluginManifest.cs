using Arronix.Abstractions.DTOs;

namespace Arronix.Plugins.Manifest;

/// <summary>
/// The declaration file an extension ships beside its assembly.
/// </summary>
/// <remarks>
/// <para>
/// Held host-side rather than promoted to the contract assembly. Nothing constructs a manifest in code —
/// an extension writes JSON and the loader reads it — and everything an extension may legitimately read
/// back about itself is already on its context. Promoting a type with no caller on the far side of the
/// boundary would freeze a shape the loader is still learning.
/// </para>
/// <para>
/// The members are the whole of what the loader will act on. Anything else in the file is a spelling
/// mistake or a stale key, and both are load failures rather than values silently ignored: an extension
/// that believes it declared a capability it did not is exactly the failure the least-privilege model
/// exists to make impossible.
/// </para>
/// </remarks>
public sealed record PluginManifest
{
    /// <summary>
    /// Gets the version of the manifest format itself. The only accepted value today is one.
    /// </summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// Gets the extension identifier, which must match the entry module's own.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the name shown to an operator.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the extension's own version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the contract versions the extension declares it works against.
    /// </summary>
    public required ContractRequirements Contracts { get; init; }

    /// <summary>
    /// Gets the file name of the assembly holding the entry module, or <see langword="null"/> when the
    /// package carries no executable behavior. A bare file name: the loader resolves it inside the
    /// extension's own folder and never outside it.
    /// </summary>
    /// <remarks>
    /// Zero or one. A package that publishes shared contract assemblies and nothing else has no module to
    /// name, and requiring it to invent a no-op one would also require it to claim a privilege nothing
    /// uses — which the forward capability check refuses.
    /// </remarks>
    public string? EntryAssembly { get; init; }

    /// <summary>
    /// Gets the file names of the assemblies this package publishes for its dependants to compile and
    /// bind against.
    /// </summary>
    /// <remarks>
    /// Zero or more, each a bare file name inside the package's own folder, and never the entry assembly.
    /// A shared contract assembly and an executable entry assembly have different isolation, update and
    /// unload lifetimes: sharing the types a dependant closes its generics over must not also share the
    /// module, parser or provider implementations shipped beside them.
    /// </remarks>
    public IReadOnlyList<string> ContractAssemblies { get; init; } = [];

    /// <summary>
    /// Gets the packages this package requires, each an exact identifier and one compatible version range.
    /// </summary>
    /// <remarks>
    /// Direct edges only. The transitive closure is derived by the loader and is output rather than input:
    /// a package restating its dependencies' dependencies would be a second authority over them, and one
    /// that goes stale the first time either package moves.
    /// </remarks>
    public IReadOnlyList<PackageDependencyDeclaration> Dependencies { get; init; } = [];

    /// <summary>
    /// Gets the capabilities the extension declares, by wire name.
    /// </summary>
    /// <remarks>
    /// An extension carrying an entry module must declare at least one; a package that runs no code of its
    /// own must declare none. Both are checked by the validator, which can name the member at fault, rather
    /// than by the reader, which can only say the file was unreadable.
    /// </remarks>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>
    /// Gets an optional sentence describing what the extension is for.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the transitional media-kind inventory supplied by a legacy shape-based extension.
    /// </summary>
    public IReadOnlyList<string> MediaKinds { get; init; } = [];

    /// <summary>
    /// Gets the transitional, non-authoritative identifier inventory supplied by a legacy extension.
    /// </summary>
    public IReadOnlyList<string> Identifiers { get; init; } = [];

    /// <summary>
    /// Gets the transitional naming-token inventory supplied by a legacy extension.
    /// </summary>
    /// <remarks>
    /// Declared as the contract assembly's token descriptor rather than as bare strings, because the host
    /// has to validate templates and present help for each token, and a bare string carries neither a
    /// description nor an example. The reader still accepts a bare string and widens it, so a manifest
    /// written against the older documented example continues to parse.
    /// </remarks>
    public IReadOnlyList<NamingToken> Tokens { get; init; } = [];

    /// <summary>
    /// Gets the transitional legacy policy identifiers, by category.
    /// </summary>
    public PolicyGraph? Policies { get; init; }
}
