using Arronix.Abstractions.Wire;

namespace Arronix.Client.Contracts;

/// <summary>Where one assembly's bytes came from.</summary>
public enum ContractByteSource
{
    /// <summary>Fetched over the network from the host.</summary>
    Network = 0,

    /// <summary>Read from this browser's contract store, which is what a warm start looks like.</summary>
    Store = 1
}

/// <summary>What became of one client-safe assembly in this browser.</summary>
public enum ContractLoadOutcome
{
    /// <summary>Loaded, and every published fact about it was proved against what loaded.</summary>
    Loaded = 0,

    /// <summary>Loaded on an earlier pass of this page and reused, because a browser cannot unload one.</summary>
    AlreadyLoaded = 1,

    /// <summary>The bytes received did not hash to the content hash the host published. Nothing was loaded.</summary>
    ContentHashMismatch = 2,

    /// <summary>The bytes loaded, but as a different CLR identity or a different build than was published.</summary>
    IdentityMismatch = 3,

    /// <summary>The bytes could not be fetched, or the runtime refused them.</summary>
    Failed = 4
}

/// <summary>One client-safe assembly, as published and as actually loaded.</summary>
/// <param name="Published">Exactly what the host said this file is.</param>
/// <param name="Outcome">What became of it here.</param>
/// <param name="Source">Where its bytes came from on this pass.</param>
/// <param name="ObservedContentHash">The SHA-256 this browser computed over the bytes it received.</param>
/// <param name="ObservedIdentity">The CLR identity the runtime bound the bytes as.</param>
/// <param name="ObservedModuleVersionId">The module identifier of the build that actually loaded.</param>
/// <param name="BindsToClientContract">
/// Whether the loaded assembly's reference to the universal contract resolved to this client's own
/// compiled contract assembly, by object identity. This is the whole point of the exercise: a media
/// contract is only shared if both sides are looking at the same <see cref="System.Reflection.Assembly"/>.
/// </param>
/// <param name="Failure">Why it did not load, when it did not.</param>
public sealed record LoadedContractAssembly(
    ClientContractAssembly Published,
    ContractLoadOutcome Outcome,
    ContractByteSource Source,
    string? ObservedContentHash,
    string? ObservedIdentity,
    Guid? ObservedModuleVersionId,
    bool BindsToClientContract,
    string? Failure);

/// <summary>One installed package's client facet, as loaded.</summary>
/// <param name="Id">The package identifier.</param>
/// <param name="Version">The installed version.</param>
/// <param name="Name">The name an operator sees.</param>
/// <param name="ClosureHash">The closure hash the host published for it.</param>
/// <param name="Closure">The package identifiers a client must load for it, dependency first.</param>
/// <param name="Assemblies">Its own client-safe assemblies.</param>
public sealed record LoadedContractPackage(
    string Id,
    string Version,
    string Name,
    string ClosureHash,
    IReadOnlyList<string> Closure,
    IReadOnlyList<LoadedContractAssembly> Assemblies);

/// <summary>Why a whole installation was refused before anything was loaded.</summary>
public enum ContractCompatibility
{
    /// <summary>The host and this client carry the same universal contract identity.</summary>
    Compatible = 0,

    /// <summary>The host publishes a contract identity this client cannot bind. Nothing was loaded.</summary>
    ContractIdentityMismatch = 1,

    /// <summary>The host could not be reached, or answered with something this client cannot read.</summary>
    Unreachable = 2
}

/// <summary>Everything this browser knows about the contracts the host published.</summary>
/// <param name="Compatibility">Whether anything was loadable at all.</param>
/// <param name="PublishedContractIdentity">The universal contract identity the host published.</param>
/// <param name="ClientContractIdentity">The universal contract identity this client was compiled against.</param>
/// <param name="InstallationHash">The host's one hash over everything a client would load.</param>
/// <param name="Packages">The publishing packages and what became of each of their assemblies.</param>
/// <param name="StoreAvailable">Whether this browser gave the client a persistent contract store.</param>
/// <param name="Failure">The sentence to show when nothing could be loaded.</param>
/// <remarks>
/// Deliberately a complete record of both sides rather than a boolean. The failure this design exists to
/// prevent is a browser rendering something plausible out of a contract that is not the one the host
/// admitted, and the only way to see that has not happened is to be able to read what each side said.
/// </remarks>
public sealed record ContractLoadReport(
    ContractCompatibility Compatibility,
    string? PublishedContractIdentity,
    string ClientContractIdentity,
    string? InstallationHash,
    IReadOnlyList<LoadedContractPackage> Packages,
    bool StoreAvailable,
    string? Failure);
