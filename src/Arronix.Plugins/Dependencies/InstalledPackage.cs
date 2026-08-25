using System.Collections.ObjectModel;
using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Versioning;

namespace Arronix.Plugins.Dependencies;

/// <summary>
/// One installed package, proved from its declaration before any of its code could run.
/// </summary>
/// <remarks>
/// <para>
/// This is the only installed-package model. Manifest validation, graph resolution, contract admission,
/// executable loading, runtime publication and teardown act on this exact value, so no second description
/// of a package can drift from it.
/// </para>
/// <para>
/// Every collection is copied into a <see cref="ReadOnlyCollection{T}"/>, so a caller cannot cast a
/// property back to the array behind it and edit what the installation was proved to be. The mutable
/// declaration is not retained and no later step rereads the manifest file.
/// </para>
/// <para>
/// The type has reference identity only. Two installation attempts of one identifier are exactly what
/// exact-receipt withdrawal has to tell apart, so a structurally equal clone must not compare equal to the
/// package the loader actually admitted.
/// </para>
/// </remarks>
internal sealed class InstalledPackage
{
    /// <summary>Initializes a new instance of the <see cref="InstalledPackage"/> class.</summary>
    /// <param name="id">The package identifier.</param>
    /// <param name="version">The version its declaration states.</param>
    /// <param name="source">The declaration file it was read from.</param>
    /// <param name="folder">The folder the package owns. Nothing outside it is read.</param>
    /// <param name="entryAssemblyFileName">
    /// The bare file name of its entry assembly, or <see langword="null"/> when the package runs no code.
    /// </param>
    /// <param name="contractAssemblies">The bare file names it publishes as shared contracts.</param>
    /// <param name="requirements">Its direct requirements, exactly as declared.</param>
    /// <param name="availability">Whether it can be activated at all.</param>
    /// <param name="clientContractAssemblies">
    /// The bare file names it permits a browser client to download, always a subset of
    /// <paramref name="contractAssemblies"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// An argument is blank or default, a declared file name is not bare, or a collection contains a null entry.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="availability"/> is not a defined state.</exception>
    public InstalledPackage(
        PluginId id,
        SemanticVersion version,
        string source,
        string folder,
        string? entryAssemblyFileName = null,
        IReadOnlyList<string>? contractAssemblies = null,
        IReadOnlyList<PackageRequirement>? requirements = null,
        PackageAvailability availability = PackageAvailability.Available,
        IReadOnlyList<string>? clientContractAssemblies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        Id = PackageIdentity.Required(id, nameof(id));
        Version = version;
        Source = source;
        Folder = folder;
        Availability = PackageAvailabilityReason.Required(availability, nameof(availability));

        EntryAssemblyFileName = entryAssemblyFileName is null
            ? null
            : PackageFileName.Required(entryAssemblyFileName, nameof(entryAssemblyFileName));

        var contracts = new List<string>(contractAssemblies?.Count ?? 0);

        foreach (var assembly in contractAssemblies ?? [])
        {
            var name = PackageFileName.Required(assembly, nameof(contractAssemblies));

            // Sharing an assembly says its types are one identity everywhere. The entry assembly carries the
            // module, the parser and the provider implementations, whose isolation, update and unload
            // lifetime is exactly what a package boundary exists to keep separate.
            if (EntryAssemblyFileName is { } entry
                && string.Equals(name, entry, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"'{name}' is this package's entry assembly and cannot also be a shared contract assembly.",
                    nameof(contractAssemblies));
            }

            contracts.Add(name);
        }

        var declared = new List<PackageRequirement>(requirements?.Count ?? 0);

        foreach (var requirement in requirements ?? [])
        {
            declared.Add(requirement
                ?? throw new ArgumentException(
                    "A requirement list must not contain a null entry.",
                    nameof(requirements)));
        }

        var offered = new List<string>(clientContractAssemblies?.Count ?? 0);

        foreach (var assembly in clientContractAssemblies ?? [])
        {
            var name = PackageFileName.Required(assembly, nameof(clientContractAssemblies));

            // A client contract is a shared contract seen from outside the process. Offering a file the
            // package does not also publish would hand a browser a second identity for a type this
            // installation admitted exactly once, which is the failure sharing a contract exists to prevent.
            if (!contracts.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"'{name}' is offered to clients but is not one of this package's shared contract assemblies.",
                    nameof(clientContractAssemblies));
            }

            offered.Add(name);
        }

        ContractAssemblies = contracts.AsReadOnly();
        Requirements = declared.AsReadOnly();
        ClientContractAssemblies = offered.AsReadOnly();
    }

    /// <summary>Gets the package identifier.</summary>
    public PluginId Id { get; }

    /// <summary>Gets the installed version.</summary>
    public SemanticVersion Version { get; }

    /// <summary>Gets the declaration file the package was read from.</summary>
    public string Source { get; }

    /// <summary>Gets the folder the package owns.</summary>
    public string Folder { get; }

    /// <summary>
    /// Gets the bare file name of the entry assembly, or <see langword="null"/> when there is none.
    /// </summary>
    /// <remarks>
    /// A package carries zero or one executable entry assembly. A package with none is an ordinary package
    /// with nothing to activate, not a defective one.
    /// </remarks>
    public string? EntryAssemblyFileName { get; }

    /// <summary>Gets the bare file names this package publishes as shared contracts.</summary>
    /// <remarks>
    /// Declared, never inferred. Deciding that an assembly "looks like a contract" is assembly scanning by
    /// another name and would let a package globalize executable code by accident.
    /// </remarks>
    public ReadOnlyCollection<string> ContractAssemblies { get; }

    /// <summary>Gets the bare file names this package permits a browser client to download.</summary>
    /// <remarks>
    /// Always a subset of <see cref="ContractAssemblies"/>, and empty for a package that offers a client
    /// nothing. Declared rather than inferred: whatever is named here leaves the host's process, so the
    /// decision belongs to the package author and has to be visible in a review.
    /// </remarks>
    public ReadOnlyCollection<string> ClientContractAssemblies { get; }

    /// <summary>Gets the direct requirements exactly as declared, including any repetition.</summary>
    /// <remarks>
    /// Kept verbatim rather than collapsed to a set: a package that states one dependency twice has said
    /// something the resolver refuses rather than quietly reconciles.
    /// </remarks>
    public ReadOnlyCollection<PackageRequirement> Requirements { get; }

    /// <summary>Gets whether the package can be activated at all, before its dependencies are considered.</summary>
    public PackageAvailability Availability { get; }

    /// <summary>Gets a value indicating whether the package contributes executable code.</summary>
    public bool HasEntryAssembly => EntryAssemblyFileName is not null;

    /// <summary>Gets this copy in the form a duplicate diagnostic lists it.</summary>
    /// <remarks>
    /// The rendering and the ordering of a duplicated identifier's copies are the same value. A sort key
    /// coarser than the text it orders leaves ties the text can still tell apart, and the winner of such a
    /// tie would be whichever copy was discovered first. The folder cannot be blank, so two copies cannot
    /// order equal and render differently.
    /// </remarks>
    public string Described => $"{Version} at {Folder}";

    /// <inheritdoc />
    public override string ToString() => $"{Id} {Version} ({Folder})";
}
