using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Wire;

namespace Arronix.Client.Contracts;

/// <summary>Where one assembly's bytes came from.</summary>
public enum ContractByteSource
{
    /// <summary>Nothing was fetched and nothing is held. First so that a default value claims nothing.</summary>
    None = 0,

    /// <summary>Fetched over the network from the host.</summary>
    Network = 1,

    /// <summary>Read from this browser's contract store, which is what a warm start looks like.</summary>
    Store = 2,

    /// <summary>
    /// Nothing was fetched because this page already holds the assembly, verified on an earlier pass.
    /// Distinct from <see cref="None"/>, which means nothing was fetched and nothing is held.
    /// </summary>
    Resident = 3
}

/// <summary>What became of one client-safe assembly in this browser.</summary>
/// <remarks>
/// Only <see cref="Loaded"/> and <see cref="AlreadyLoaded"/> mean the runtime holds the assembly. Every
/// other value — <see cref="Verified"/> included — means it does not. The distinction matters more than
/// usual here: the browser's default load context cannot unload, so what is resident and what merely could
/// have been are permanently different facts.
/// </remarks>
public enum ContractLoadOutcome
{
    /// <summary>
    /// Nothing was attempted for this assembly. First so that a default value claims nothing.
    /// </summary>
    NotAttempted = 0,

    /// <summary>
    /// Verified against every published fact and deliberately not loaded, because another member of the
    /// required set failed or the commit stopped earlier. The runtime never saw these bytes.
    /// </summary>
    Verified = 1,

    /// <summary>Verified before loading, then loaded, and proved again against what the runtime produced.</summary>
    Loaded = 2,

    /// <summary>
    /// Found already resident from an earlier pass of this page, with the same content hash, identity and
    /// module version identifier. Reused rather than loaded again.
    /// </summary>
    AlreadyLoaded = 3,

    /// <summary>The bytes received did not have the published length.</summary>
    LengthMismatch = 4,

    /// <summary>The bytes received did not hash to the published content hash.</summary>
    ContentHashMismatch = 5,

    /// <summary>The bytes declare a different CLR identity than the host published.</summary>
    IdentityMismatch = 6,

    /// <summary>The bytes declare a different build than the host published.</summary>
    ModuleVersionMismatch = 7,

    /// <summary>The bytes do not declare exactly the universal contract reference this client carries.</summary>
    ContractReferenceMismatch = 8,

    /// <summary>
    /// This page already holds an assembly of that simple name which is not the one published. A browser
    /// cannot unload it, so the page can never satisfy this installation.
    /// </summary>
    NameAlreadyResident = 9,

    /// <summary>The bytes could not be fetched, or could not be read as an assembly.</summary>
    /// <remarks>
    /// A transport failure. A host that answered about the address says so through
    /// <see cref="Superseded"/> or <see cref="NotOffered"/>.
    /// </remarks>
    Unavailable = 10,

    /// <summary>
    /// Verification passed and the runtime refused the bytes, or produced something other than what was
    /// published. Earlier members may already be resident, so the page is terminal.
    /// </summary>
    RuntimeRefused = 11,

    /// <summary>
    /// The bytes declare client contracts other than the ones the host published for them, or declare one
    /// this client cannot read.
    /// </summary>
    /// <remarks>
    /// A separate outcome from <see cref="IdentityMismatch"/> because it is a different disagreement. The
    /// identity says what the runtime will bind these bytes as; a declaration says what may be read out of
    /// them once it has, and a host describing a payload's contracts wrongly is exactly the case the
    /// published hashes exist to catch before anything is projected.
    /// </remarks>
    DeclarationMismatch = 12,

    /// <summary>
    /// 410 Gone: the host still publishes this file, under a different content hash. Recoverable by
    /// re-reading the manifest, and never a statement about what this page holds.
    /// </summary>
    Superseded = 13,

    /// <summary>404 Not Found: the host offers nothing at this address under any content hash.</summary>
    NotOffered = 14
}

/// <summary>One client-safe assembly, as published and as verified.</summary>
/// <param name="Published">Exactly what the host said this file is.</param>
/// <param name="Outcome">What became of it here.</param>
/// <param name="Source">Where its bytes came from on this pass.</param>
/// <param name="ObservedLength">The number of bytes this browser received.</param>
/// <param name="ObservedContentHash">The SHA-256 this browser computed over the bytes it received.</param>
/// <param name="ObservedIdentity">The CLR identity the bytes declare, read from their metadata.</param>
/// <param name="ObservedModuleVersionId">The module identifier the bytes declare, read from their metadata.</param>
/// <param name="ObservedContractReference">
/// The universal contract reference the bytes declare, read from their metadata.
/// </param>
/// <param name="ObservedDeclarations">
/// The client contracts the bytes declare, read from their metadata. Empty when they declare none, which is
/// what a shared assembly owning no item looks like.
/// </param>
/// <param name="Failure">Why it was refused, when it was.</param>
/// <remarks>
/// Every observed value is read from the bytes themselves before the runtime is involved, so a refusal can
/// state what the bytes actually were rather than what a loaded assembly reports about itself.
/// </remarks>
public sealed record LoadedContractAssembly(
    ClientContractAssembly Published,
    ContractLoadOutcome Outcome,
    ContractByteSource Source,
    int? ObservedLength,
    string? ObservedContentHash,
    string? ObservedIdentity,
    Guid? ObservedModuleVersionId,
    string? ObservedContractReference,
    IReadOnlyList<ClientContractDeclaration> ObservedDeclarations,
    string? Failure);

/// <summary>One installed package's client facet, as verified.</summary>
/// <param name="Id">The package identifier.</param>
/// <param name="Version">The installed version.</param>
/// <param name="Name">The name an operator sees.</param>
/// <param name="ClosureHash">The closure hash the host published for it.</param>
/// <param name="Closure">The package identifiers a client must load for it, dependency first.</param>
/// <param name="Assemblies">Its own client-safe assemblies.</param>
public sealed record LoadedContractPackage(
    PluginId Id,
    string Version,
    string Name,
    string ClosureHash,
    IReadOnlyList<PluginId> Closure,
    IReadOnlyList<LoadedContractAssembly> Assemblies);

/// <summary>What the installation a page last read says about an orphaned assembly's package.</summary>
public enum OrphanedContractOwner
{
    /// <summary>Named neither among that manifest's offers nor among its refusals.</summary>
    /// <remarks>Uninstalled, quarantined and never installed are one answer from a client's side.</remarks>
    Unpublished = 0,

    /// <summary>Still offered, with a client facet that no longer carries this assembly.</summary>
    Offered = 1,

    /// <summary>Withheld, with the reason in <see cref="OrphanedContract.Refusal"/>.</summary>
    Withheld = 2
}

/// <summary>One assembly this page holds which the installation it last read does not name.</summary>
/// <param name="Verified">What the host published for it on the pass that loaded it.</param>
/// <param name="PackageId">The package that published it, captured when it was last admitted.</param>
/// <param name="PackageName">That package's name, as the host last stated it.</param>
/// <param name="PackageVersion">That package's version, as the host last stated it.</param>
/// <param name="Owner">What the manifest just read says about that package.</param>
/// <param name="Refusal">The host's live refusal when withheld; otherwise <see langword="null"/>.</param>
/// <remarks>
/// A browser cannot unload, so the assembly stays resident; what changes is that
/// <see cref="MediaContractLoader.Find"/> and <see cref="MediaContractLoader.ContractsOf"/> refuse it. It
/// never makes <see cref="ContractLoadReport.CanProject"/> false: a removal elsewhere is not a fault in
/// what this page still holds.
/// </remarks>
public sealed record OrphanedContract(
    ClientContractAssembly Verified,
    PluginId PackageId,
    string PackageName,
    string PackageVersion,
    OrphanedContractOwner Owner,
    ClientContractRefusal? Refusal);

/// <summary>Whether this browser may project anything from what the host published.</summary>
public enum ContractCompatibility
{
    /// <summary>
    /// Nothing has been attempted. First so that a default value never permits a projection.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Every required assembly was verified and is resident. The only value under which a typed projection
    /// is permitted.
    /// </summary>
    Compatible = 1,

    /// <summary>The host publishes a contract identity this client cannot bind. Nothing was loaded.</summary>
    ContractIdentityMismatch = 2,

    /// <summary>The host could not be reached, or answered with something this client cannot read.</summary>
    Unreachable = 3,

    /// <summary>
    /// The host answered with a document that does not describe an installation. Nothing was fetched: a
    /// description this client cannot trust to be self-consistent is not one it may act on part of.
    /// </summary>
    ManifestInvalid = 4,

    /// <summary>
    /// At least one required assembly failed verification, so nothing was loaded. Recoverable: a corrected
    /// installation loads on the next pass.
    /// </summary>
    Refused = 5,

    /// <summary>
    /// This page can never satisfy the installation the host is running, because an assembly it cannot
    /// unload is resident or a verified load was refused part-way through. Only a reload clears it.
    /// </summary>
    Terminal = 6
}

/// <summary>Everything this browser knows about the contracts the host published.</summary>
/// <param name="Compatibility">Whether anything may be projected.</param>
/// <param name="PublishedContractIdentity">The universal contract identity the host published.</param>
/// <param name="ClientContractIdentity">The universal contract identity this client was compiled against.</param>
/// <param name="InstallationHash">The host's one hash over everything a client would load.</param>
/// <param name="Packages">The publishing packages and what became of each of their assemblies.</param>
/// <param name="Refused">The client facets the host itself withheld, and why.</param>
/// <param name="Orphaned">
/// The assemblies this page holds which that installation no longer names, by assembly name. Empty unless
/// the pass reached the required set of a manifest it had proved whole.
/// </param>
/// <param name="StoreAvailable">Whether this browser gave the client a persistent contract store.</param>
/// <param name="Failure">The sentence to show when nothing may be projected.</param>
/// <remarks>
/// <para>
/// Deliberately a complete record of both sides rather than a boolean. The failure this design exists to
/// prevent is a browser rendering something plausible out of a contract that is not the one the host
/// admitted, and the only way to see that has not happened is to be able to read what each side said.
/// </para>
/// <para>
/// <see cref="CanProject"/> is the one question a caller should ask before using anything that was loaded.
/// Diagnostics are visible whatever the outcome; "compatible" means safe to project, and nothing else.
/// </para>
/// </remarks>
public sealed record ContractLoadReport(
    ContractCompatibility Compatibility,
    string? PublishedContractIdentity,
    string ClientContractIdentity,
    string? InstallationHash,
    IReadOnlyList<LoadedContractPackage> Packages,
    IReadOnlyList<ClientContractRefusal> Refused,
    IReadOnlyList<OrphanedContract> Orphaned,
    bool StoreAvailable,
    string? Failure)
{
    /// <summary>
    /// Gets whether a typed projection may be built from what this page holds.
    /// </summary>
    /// <remarks>
    /// All or nothing over everything <i>currently required</i>: a partially verified installation is not a
    /// smaller installation. Narrower than everything this page has ever loaded — <see cref="Orphaned"/>
    /// carries that second fact and never makes this one false.
    /// </remarks>
    public bool CanProject => Compatibility == ContractCompatibility.Compatible;
}
