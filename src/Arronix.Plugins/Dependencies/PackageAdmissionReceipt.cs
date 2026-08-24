using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Loading;
using Arronix.Plugins.Versioning;

namespace Arronix.Plugins.Dependencies;

/// <summary>
/// One published dependency edge, owned by the exact dependant receipt that created it.
/// </summary>
/// <remarks>
/// The edge records the dependency's exact receipt rather than its identifier, because an identifier is
/// precisely what two different installation attempts have in common. An edge is a dependant-side liability:
/// it is created, published and withdrawn by the dependant, and the dependency only ever reads it.
/// </remarks>
internal sealed class PackageDependencyEdge
{
    internal PackageDependencyEdge(
        PluginId dependant,
        PackageRequirement requirement,
        PackageAdmissionReceipt dependency)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(dependency);

        Dependant = dependant;
        Requirement = requirement;
        DependencyReceipt = dependency;
    }

    /// <summary>Gets the package that declared this dependency.</summary>
    internal PluginId Dependant { get; }

    /// <summary>Gets the package it depends on.</summary>
    internal PluginId Dependency => Requirement.PackageId;

    /// <summary>Gets the exact identifier and range this dependant declared.</summary>
    internal PackageRequirement Requirement { get; }

    /// <summary>Gets the exact dependency receipt this dependant was admitted against.</summary>
    internal PackageAdmissionReceipt DependencyReceipt { get; }

    /// <summary>Projects the edge as operator-facing diagnostics.</summary>
    internal PackageDependencyView ToView()
        => new(Dependant, Dependency, Requirement.Range.Text, DependencyReceipt.Version);
}

/// <summary>
/// One installation attempt of one package: its canonical declaration, its edges, its contract hold and its
/// optional executable admission.
/// </summary>
/// <remarks>
/// A package is not the same thing as executable Host admission. A package may share contract assemblies and
/// contribute no executable code, and a package whose executable admission is refused still had an
/// installation attempt that must be released exactly. Everything on the receipt is prepared before the
/// publication gate is entered; commit publishes an already-built value.
/// </remarks>
internal sealed class PackageAdmissionReceipt
{
    private IPluginAdmissionAttempt? _hostAdmission;

    internal PackageAdmissionReceipt(
        InstalledPackage package,
        IReadOnlyList<PackageDependencyEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(edges);

        if (edges.Count != package.Requirements.Count)
        {
            throw new ArgumentException(
                $"Package '{package.Id}' declares {package.Requirements.Count} requirement(s) and was given "
                + $"{edges.Count} edge(s). A receipt carries exactly the edges its declaration states.",
                nameof(edges));
        }

        for (var index = 0; index < edges.Count; index++)
        {
            var edge = edges[index];

            if (edge is null)
            {
                throw new ArgumentException("A package edge collection must not contain null entries.", nameof(edges));
            }

            if (edge.Dependant != package.Id)
            {
                throw new ArgumentException(
                    $"Edge '{edge.Dependant} -> {edge.Dependency}' does not belong to package '{package.Id}'. "
                    + "An edge is owned by the exact dependant receipt that publishes it.",
                    nameof(edges));
            }

            // The exact requirement object, in declaration order. An edge built from a structurally equal
            // requirement would let a receipt describe a dependency this package did not declare.
            if (!ReferenceEquals(edge.Requirement, package.Requirements[index]))
            {
                throw new ArgumentException(
                    $"Edge {index} of package '{package.Id}' names '{edge.Requirement}', but its declaration "
                    + $"states '{package.Requirements[index]}' at that position.",
                    nameof(edges));
            }
        }

        Package = package;
        Edges = [.. edges];
    }

    /// <summary>Gets the canonical installed package this attempt belongs to.</summary>
    internal InstalledPackage Package { get; }

    /// <summary>Gets the package identifier.</summary>
    internal PluginId Id => Package.Id;

    /// <summary>Gets the package version.</summary>
    internal SemanticVersion Version => Package.Version;

    /// <summary>Gets the edges this dependant owns, in declaration order.</summary>
    internal IReadOnlyList<PackageDependencyEdge> Edges { get; }

    /// <summary>Gets the exact executable admission receipt, or <see langword="null"/> when there is none.</summary>
    internal IPluginAdmissionAttempt? HostAdmission => _hostAdmission;

    /// <summary>Gets a value indicating whether this package contributes executable code.</summary>
    internal bool HasExecutableAdmission => _hostAdmission is not null;

    /// <summary>
    /// Couples the executable admission attempt to this package attempt, exactly once.
    /// </summary>
    /// <param name="attempt">The prepared Host attempt.</param>
    /// <exception cref="ArgumentNullException"><paramref name="attempt"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">An attempt is already coupled.</exception>
    internal void AttachHostAdmission(IPluginAdmissionAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (Interlocked.CompareExchange(ref _hostAdmission, attempt, null) is not null)
        {
            throw new InvalidOperationException(
                $"Package '{Id}' already has an executable admission attempt coupled to this receipt.");
        }
    }

    /// <summary>Projects this package's edges as operator-facing diagnostics.</summary>
    internal IReadOnlyList<PackageDependencyView> ToViews()
        => [.. Edges.Select(edge => edge.ToView())];
}

/// <summary>
/// One package's lifetime: its receipt, its hold on the installation contract context, and the optional
/// executable runtime it wraps.
/// </summary>
/// <remarks>
/// <para>
/// Package lifetime and executable lifetime are separate. A contract-only package has a receipt and a
/// contract hold and no runtime at all; an executable package has all three. Modelling the package lease as
/// the outer object is what lets a contract-only package be active without inventing a load context or a
/// registration ledger for it.
/// </para>
/// <para>
/// Release order is fixed: the executable runtime first, then this package's contract hold, and only then —
/// by the caller, and only when both reported success — the edges and pins. A package's own contract
/// assembly may reference contracts its dependencies published, so the hold is given up while those
/// dependencies are still pinned.
/// </para>
/// </remarks>
internal sealed class PackageAdmissionLease
{
    private PluginRuntimeLease? _runtime;
    private int _disposed;

    internal PackageAdmissionLease(PackageAdmissionReceipt receipt, PackageContractScope contracts)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(contracts);

        Receipt = receipt;
        Contracts = contracts;
    }

    /// <summary>Gets the exact package receipt this lease owns.</summary>
    internal PackageAdmissionReceipt Receipt { get; }

    /// <summary>Gets this package's hold on the installation contract context and its scoped resolver.</summary>
    internal PackageContractScope Contracts { get; }

    /// <summary>Gets the executable runtime this package wraps, or <see langword="null"/> when it has none.</summary>
    internal PluginRuntimeLease? Runtime => _runtime;

    /// <summary>Couples the executable runtime lease to this package lease, exactly once.</summary>
    /// <param name="runtime">The prepared runtime lease.</param>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A runtime lease is already coupled.</exception>
    internal void AttachRuntime(PluginRuntimeLease runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        if (Interlocked.CompareExchange(ref _runtime, runtime, null) is not null)
        {
            throw new InvalidOperationException(
                $"Package '{Receipt.Id}' already has an executable runtime coupled to this lease.");
        }
    }

    /// <summary>Withdraws naming-token claims published by the executable attempt, if there was one.</summary>
    internal void UnpublishTokenClaims() => _runtime?.UnpublishTokenClaims();

    /// <summary>
    /// Releases the executable runtime and then this package's contract hold.
    /// </summary>
    /// <returns>Everything that could not be released, or an empty list.</returns>
    /// <remarks>
    /// The contract hold is retained when the executable half reported a failure. An unload request that
    /// threw leaves the package in an indeterminate state, and a shared contract with possibly live
    /// dependant types must not become releasable on the assumption that teardown got far enough.
    /// </remarks>
    internal async ValueTask<IReadOnlyList<string>> DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return [];
        }

        var failures = new List<string>();

        if (_runtime is { } runtime)
        {
            failures.AddRange(await runtime.DisposeAsync().ConfigureAwait(false));
        }

        if (failures.Count == 0)
        {
            Contracts.Release();
        }
        else if (Contracts.IsHeld)
        {
            failures.Add(
                $"shared contracts: the hold held by '{Receipt.Id}' is retained because its executable half "
                + "did not release cleanly.");
        }

        return failures.AsReadOnly();
    }
}
