using System.Collections.ObjectModel;
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
        PackageAvailability availability = PackageAvailability.Available)
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
            contracts.Add(PackageFileName.Required(assembly, nameof(contractAssemblies)));
        }

        var declared = new List<PackageRequirement>(requirements?.Count ?? 0);

        foreach (var requirement in requirements ?? [])
        {
            declared.Add(requirement
                ?? throw new ArgumentException(
                    "A requirement list must not contain a null entry.",
                    nameof(requirements)));
        }

        ContractAssemblies = contracts.AsReadOnly();
        Requirements = declared.AsReadOnly();
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
