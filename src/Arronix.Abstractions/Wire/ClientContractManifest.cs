using Arronix.Abstractions.Plugins;

namespace Arronix.Abstractions.Wire;

/// <summary>
/// One client contract an admitted assembly declares, read from its own metadata.
/// </summary>
/// <param name="EntryPointType">
/// The full name of the generated attribute type carrying the declaration. It is the type the declaration
/// <i>is</i>, so a consumer that resolved it and a host that read it are naming one thing.
/// </param>
/// <param name="EntityTypeName">
/// The full name of the CLR type this entry point reads and constructs, as the declaration's own type
/// argument states it.
/// </param>
/// <param name="GeneratedMetadataHash">
/// The SHA-256, upper-case hexadecimal, over the member graph the generated reader accepts.
/// </param>
/// <param name="ProjectionSchemaHash">
/// The SHA-256, upper-case hexadecimal, over the field schema the generated projection produces.
/// </param>
/// <remarks>
/// <para>
/// Every value here is decoded from the exact admitted bytes by a structured metadata reader, before
/// anything is loaded and without calling into the package. That is possible because each is a constructor
/// argument of the declaration attribute, and constructor arguments live in the custom attribute blob; a
/// fact exposed only by an overridden property would be executable code, and publishing it would mean
/// running the package to describe it.
/// </para>
/// <para>
/// The two hashes are checked, never adopted. The content hash already proves both parties hold one file;
/// these prove they read the same meaning out of it, and a consumer recomputes each from the contract it
/// loaded rather than believing what was published here.
/// </para>
/// </remarks>
public sealed record ClientContractDeclaration(
    string EntryPointType,
    string EntityTypeName,
    string GeneratedMetadataHash,
    string ProjectionSchemaHash);

/// <summary>
/// One client-safe assembly, as the running host admitted it.
/// </summary>
/// <param name="AssemblyName">The simple name the runtime binds on.</param>
/// <param name="FileName">The bare file name inside the publishing package's own folder.</param>
/// <param name="Identity">
/// The complete CLR identity — name, version, culture and public key token — rendered exactly as
/// <see cref="System.Reflection.AssemblyName.FullName"/> renders it.
/// </param>
/// <param name="ContentHash">The SHA-256 of the admitted bytes, upper-case hexadecimal.</param>
/// <param name="ModuleVersionId">The module identifier of that exact build.</param>
/// <param name="Length">The number of admitted bytes.</param>
/// <param name="Declarations">
/// The client contracts this assembly declares, ordered by entry point name. Empty for a shared assembly
/// that owns no item — a format's representation vocabulary declares none — and plural for one that owns
/// several.
/// </param>
/// <remarks>
/// <para>
/// Four independent statements about the same file, and a client is expected to check all four before the
/// runtime is allowed near the bytes. The length and the content hash say which bytes; the identity says
/// what the runtime will bind them as; the module version identifier says which build the compiler
/// produced. Bytes that hash correctly but declare a different module identifier are not a corrupted
/// download — they are a different build being served under a name this installation already decided the
/// meaning of, and the difference is only visible before loading.
/// </para>
/// <para>
/// A list rather than an optional single value, because the count is a fact about the assembly rather than
/// a case to be handled: none, one and several are the same shape read the same way. An optional singular
/// field would have had to answer "which one" for an assembly owning two items, and the honest answer is
/// that the question is wrong.
/// </para>
/// <para>
/// No signing information beyond the public key token appears here, and that is a limit rather than an
/// omission: this installation records what a file <i>is</i>, not who vouched for it. Package provenance
/// remains unbuilt.
/// </para>
/// </remarks>
public sealed record ClientContractAssembly(
    string AssemblyName,
    string FileName,
    string Identity,
    string ContentHash,
    Guid ModuleVersionId,
    int Length,
    IReadOnlyList<ClientContractDeclaration> Declarations);

/// <summary>
/// One installed package, as far as a browser client is concerned.
/// </summary>
/// <param name="Id">The package identifier.</param>
/// <param name="Version">The installed package version.</param>
/// <param name="Name">The name shown to an operator.</param>
/// <param name="Assemblies">
/// The client-safe assemblies this package itself publishes, ordered by simple name.
/// </param>
/// <param name="Closure">
/// Every package identifier in this package's transitive client dependency closure, including itself,
/// ordered so that a dependency precedes its dependants. Identifiers rather than text: a package
/// identifier has a grammar, and a consumer that read one out of this document should not have to reparse
/// it or invent what an unparseable one means.
/// </param>
/// <param name="ClosureHash">
/// The SHA-256 over the canonical rendering of that closure: each package's identifier and version, and
/// each of its assemblies' identity and content hash, in closure order.
/// </param>
/// <remarks>
/// The closure is what a client must actually load, and it is stated here rather than being rediscovered
/// from assembly reference tables in a browser. A package's client facet is a subset of what it publishes,
/// so the reference table of a client-safe assembly is not a reliable description of what a client is
/// entitled to: it can name an assembly the package publishes but does not offer.
/// </remarks>
public sealed record ClientContractPackage(
    PluginId Id,
    string Version,
    string Name,
    IReadOnlyList<ClientContractAssembly> Assemblies,
    IReadOnlyList<PluginId> Closure,
    string ClosureHash);

/// <summary>
/// An Active package whose declared client facet this host will not serve, and why.
/// </summary>
/// <param name="Package">The package whose facet is withheld.</param>
/// <param name="Reason">The sentence an operator reads first.</param>
/// <param name="MissingAssemblies">
/// Simple names of admitted contract assemblies this facet binds and its client closure does not offer, in
/// a deterministic order.
/// </param>
/// <param name="UnadmittedFiles">
/// Bare file names this facet declares which the installation admitted no shared contract for, in a
/// deterministic order. A separate list from <paramref name="MissingAssemblies"/> because these are
/// declaration text rather than assembly identities, and mixing the two makes neither readable.
/// </param>
/// <param name="CausedBy">
/// Every required package whose own withdrawal contributed to this one, in identifier order; empty when the
/// refusal did not cascade. Plural because a package may lose several required facets at once, and naming
/// one of them would send an operator to fix a fraction of the problem.
/// </param>
/// <remarks>
/// A package reaches this list after admission, so it cannot be quarantined for it. Withholding its facet
/// and saying so is the honest outcome: a browser handed part of a closure fails at whichever type it
/// touches first. It travels on the manifest rather than behind a separate query because a client asking
/// what it may load is entitled to know what it may not.
/// </remarks>
public sealed record ClientContractRefusal(
    PluginId Package,
    string Reason,
    IReadOnlyList<string> MissingAssemblies,
    IReadOnlyList<string> UnadmittedFiles,
    IReadOnlyList<PluginId> CausedBy);

/// <summary>
/// What a browser client may load from this host, and the contract identity it must already have.
/// </summary>
/// <param name="ContractIdentity">
/// The complete CLR identity of the universal contract assembly this host is running. A client whose own
/// compiled contract assembly does not carry this exact identity cannot bind anything published here.
/// </param>
/// <param name="InstallationHash">
/// The SHA-256 over every published package's closure hash, in package order. One value that changes
/// whenever anything a client would load changes, so a client can decide whether to re-read the rest.
/// </param>
/// <param name="Packages">The publishing packages, ordered by identifier.</param>
/// <param name="Refused">The Active packages whose client facet is withheld, ordered by identifier.</param>
/// <remarks>
/// <para>
/// This is the whole of the client loading protocol, and everything in it is a fact about the installation
/// that is running now. There is no separate registry, no build-time list and no second description: a
/// package appears here because it is Active in this host and declared a client facet, and it disappears
/// the moment it stops being either.
/// </para>
/// <para>
/// <see cref="ContractIdentity"/> is compared, not negotiated. The host and the browser hold two different
/// builds of the same contract assembly — one for this machine, one compiled to WebAssembly — so their
/// bytes will never match and their CLR identity must. A client that finds a different identity has been
/// served by a host it cannot bind to, and the honest response is to say so rather than to load whichever
/// subset happens to resolve.
/// </para>
/// </remarks>
public sealed record ClientContractManifest(
    string ContractIdentity,
    string InstallationHash,
    IReadOnlyList<ClientContractPackage> Packages,
    IReadOnlyList<ClientContractRefusal> Refused);
