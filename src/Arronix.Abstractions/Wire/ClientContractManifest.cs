namespace Arronix.Abstractions.Wire;

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
/// <param name="ModuleVersionId">The module identifier the compiler stamped into that exact build.</param>
/// <param name="Length">The number of admitted bytes.</param>
/// <remarks>
/// <para>
/// Three independent statements about the same file, and a client is expected to check all three. The
/// content hash says which bytes; the identity says what the runtime will bind them as; the module version
/// identifier says which build the compiler produced. Bytes that hash correctly but load as a different
/// module identifier are not a corrupted download — they are a different build being served under a name
/// this installation already decided the meaning of.
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
    int Length);

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
/// ordered so that a dependency precedes its dependants.
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
    string Id,
    string Version,
    string Name,
    IReadOnlyList<ClientContractAssembly> Assemblies,
    IReadOnlyList<string> Closure,
    string ClosureHash);

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
    IReadOnlyList<ClientContractPackage> Packages);
