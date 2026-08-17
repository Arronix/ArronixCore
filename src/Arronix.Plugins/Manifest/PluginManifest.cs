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
    /// Gets the version of the manifest format itself. The only accepted value today is zero.
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
    /// Gets the file name of the assembly holding the entry module. A bare file name: the loader resolves
    /// it inside the extension's own folder and never outside it.
    /// </summary>
    public required string EntryAssembly { get; init; }

    /// <summary>
    /// Gets the capabilities the extension declares, by wire name.
    /// </summary>
    public required IReadOnlyList<string> Capabilities { get; init; }

    /// <summary>
    /// Gets an optional sentence describing what the extension is for.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the media kinds the extension claims.
    /// </summary>
    public IReadOnlyList<string> MediaKinds { get; init; } = [];

    /// <summary>
    /// Gets the external identifier schemes the extension understands.
    /// </summary>
    public IReadOnlyList<string> Identifiers { get; init; } = [];

    /// <summary>
    /// Gets the naming tokens the extension declares.
    /// </summary>
    /// <remarks>
    /// Declared as the contract assembly's token descriptor rather than as bare strings, because the host
    /// has to validate templates and present help for each token, and a bare string carries neither a
    /// description nor an example. The reader still accepts a bare string and widens it, so a manifest
    /// written against the older documented example continues to parse.
    /// </remarks>
    public IReadOnlyList<NamingToken> Tokens { get; init; } = [];

    /// <summary>
    /// Gets the declared policy identifiers, by category.
    /// </summary>
    public PolicyGraph? Policies { get; init; }
}
