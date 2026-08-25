using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Dependencies;


namespace Arronix.Plugins.Registry;

/// <summary>
/// Which package installation attempts are rooted, and which of them are pinned by a dependant.
/// </summary>
/// <remarks>
/// <para>
/// The registry participates in the one extension publication boundary rather than owning a second lock,
/// so a dependant's edges become visible in the same write lease that publishes its kinds, tokens,
/// providers and runtime result, and disappear in the same lease that withdraws them.
/// </para>
/// <para>
/// A rooted receipt with a live inbound edge is not withdrawn. Unloading a package while a dependant holds
/// types from it does not fail loudly: the context is marked for a collection that cannot happen, and the
/// dependant is left holding types from a context the platform has declared gone.
/// </para>
/// <para>
/// Withdrawal is two-phase for the mirror reason. The first phase marks the package withdrawing and keeps
/// every pin it holds, because its own disposers run outside the gate and against its dependencies' types.
/// The second removes them, and only once the code is definitively gone.
/// </para>
/// <para>
/// A preparation pin closes the window before an edge exists. A dependant pins the exact receipts it is
/// being prepared against before it reads a byte; a pin counts as a live dependant, and a successful commit
/// converts the pins into edges in one lease.
/// </para>
/// </remarks>
internal sealed class PackageDependencyRegistry
{
    private readonly PluginPublicationGate _publication;
    private readonly Dictionary<PluginId, RootedPackage> _rooted = [];
    private readonly List<PackageDependencyEdge> _edges = [];
    private readonly Dictionary<PackageAdmissionReceipt, IReadOnlyList<PackageDependencyEdge>> _preparing = [];
    private readonly Dictionary<PluginId, RetainedAttempt> _retained = [];
    private long _sequence;

    /// <summary>
    /// Creates a dependency registry participating in the one extension publication boundary.
    /// </summary>
    /// <param name="publication">The shared extension-publication boundary.</param>
    /// <exception cref="ArgumentNullException"><paramref name="publication"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// There is deliberately no convenience constructor that manufactures a boundary. A registry holding a
    /// second gate would still compile, still pass its own unit tests, and still publish edges — outside
    /// the lease that publishes everything else. The one arrangement this type must make impossible is the
    /// one a default constructor would make effortless.
    /// </remarks>
    public PackageDependencyRegistry(PluginPublicationGate publication)
    {
        _publication = publication ?? throw new ArgumentNullException(nameof(publication));
    }

    /// <summary>Gets the publication boundary this registry participates in.</summary>
    public PluginPublicationGate PublicationGate => _publication;

    /// <summary>
    /// Gets every rooted package identifier, in the order the packages were published.
    /// </summary>
    public IReadOnlyList<PluginId> RootedPackages
    {
        get
        {
            using var publication = _publication.EnterRead();
            return
            [
                .. _rooted.Values
                    .OrderBy(rooted => rooted.Sequence)
                    .Select(rooted => rooted.Receipt.Id),
            ];
        }
    }

    /// <summary>
    /// Gets every published dependency edge, ordered by dependant and then dependency.
    /// </summary>
    public IReadOnlyList<PackageDependencyView> Snapshot()
    {
        using var publication = _publication.EnterRead();
        return
        [
            .. _edges
                .Select(edge => edge.ToView())
                .OrderBy(view => view.Dependant.Value, StringComparer.Ordinal)
                .ThenBy(view => view.Dependency.Value, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Gets every package attempt still holding registry state, rooted or preparing, ordered.
    /// </summary>
    /// <remarks>
    /// The authoritative answer to "is anything still held". A package whose release reported a failure is
    /// recorded stopped and is no longer active, so nothing in the runtime record distinguishes it from one
    /// that withdrew cleanly; its receipt is still here, and its dependencies are still pinned by it. A
    /// shutdown that called itself complete while this is non-empty would be claiming the platform had let
    /// go of code it is still holding.
    /// </remarks>
    public IReadOnlyList<PluginId> RetainedPackages
    {
        get
        {
            using var publication = _publication.EnterRead();
            return
            [
                .. _rooted.Values
                    .Select(rooted => rooted.Receipt.Id)
                    .Concat(_preparing.Keys.Select(receipt => receipt.Id))
                    .Concat(_retained.Keys)
                    .Distinct()
                    .OrderBy(id => id.Value, StringComparer.Ordinal),
            ];
        }
    }

    /// <summary>
    /// Gets the identifiers of every package currently holding preparation pins, ordered.
    /// </summary>
    public IReadOnlyList<PluginId> PreparingPackages
    {
        get
        {
            using var publication = _publication.EnterRead();
            return
            [
                .. _preparing.Keys
                    .Select(receipt => receipt.Id)
                    .Distinct()
                    .OrderBy(id => id.Value, StringComparer.Ordinal),
            ];
        }
    }

    /// <summary>
    /// Gets the identifiers of every package that still depends on the named one.
    /// </summary>
    /// <param name="dependency">The package to report dependants of.</param>
    /// <returns>The dependant identifiers, ordered.</returns>
    public IReadOnlyList<PluginId> DependantsOf(PluginId dependency)
    {
        using var publication = _publication.EnterRead();
        return
        [
            .. _edges
                .Where(edge => edge.Dependency == dependency)
                .Select(edge => edge.Dependant)
                .Distinct()
                .OrderBy(id => id.Value, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Publishes one prepared package receipt and every edge it owns, or publishes none of them.
    /// </summary>
    /// <param name="receipt">The prepared receipt.</param>
    /// <param name="defects">Why publication was refused, or an empty list.</param>
    /// <returns><see langword="true"/> when the receipt and its edges were published.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="receipt"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Every edge is rechecked here, under the write lease, against the exact dependency receipt this
    /// dependant was prepared against. An earlier check during preparation is a diagnostic, never the
    /// defense: only a recheck inside the lease that publishes can be a total order with withdrawal.
    /// </remarks>
    internal bool TryPublish(PackageAdmissionReceipt receipt, out IReadOnlyList<string> defects)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        using var publication = _publication.EnterWrite();

        if (!_preparing.ContainsKey(receipt))
        {
            defects =
            [
                $"Package '{receipt.Id}' was prepared without holding preparation pins on its dependencies, "
                + "so nothing prevented one of them being withdrawn while it loaded.",
            ];
            return false;
        }

        if (_retained.TryGetValue(receipt.Id, out var retained))
        {
            defects =
            [
                $"Package '{receipt.Id}' is retained by an installation attempt at version "
                + $"{retained.Receipt.Version} whose code could not be released. The identifier stays "
                + "occupied for the life of the process.",
            ];
            return false;
        }

        if (_rooted.TryGetValue(receipt.Id, out var incumbent))
        {
            defects =
            [
                ReferenceEquals(incumbent.Receipt, receipt)
                    ? $"Package '{receipt.Id}' is already rooted by this exact installation attempt."
                    : incumbent.IsWithdrawing
                        ? $"Package '{receipt.Id}' is still withdrawing an installation attempt at version "
                          + $"{incumbent.Receipt.Version}, whose code has not been released."
                        : $"Package '{receipt.Id}' is already rooted by another installation attempt at "
                          + $"version {incumbent.Receipt.Version}.",
            ];
            return false;
        }

        var found = new List<string>();

        foreach (var edge in receipt.Edges)
        {
            if (!_rooted.TryGetValue(edge.Dependency, out var current))
            {
                found.Add(
                    $"dependency[{edge.Dependency}]: package '{receipt.Id}' was admitted against it, but it "
                    + "is no longer rooted.");
                continue;
            }

            if (!ReferenceEquals(current.Receipt, edge.DependencyReceipt))
            {
                found.Add(
                    $"dependency[{edge.Dependency}]: package '{receipt.Id}' was admitted against a different "
                    + $"installation attempt of it than the one now rooted at version {current.Receipt.Version}.");
                continue;
            }

            if (current.IsWithdrawing)
            {
                found.Add(
                    $"dependency[{edge.Dependency}]: package '{receipt.Id}' was admitted against it, and it "
                    + "is now withdrawing.");
            }
        }

        if (found.Count > 0)
        {
            defects = found;
            return false;
        }

        // One lease, one conversion: the pins that kept these dependencies rooted while this package
        // loaded become the edges that keep them rooted while it serves. There is no instant at which the
        // package is prepared and its dependencies are unpinned.
        _rooted[receipt.Id] = new RootedPackage(receipt, ++_sequence);
        _edges.AddRange(receipt.Edges);
        _preparing.Remove(receipt);
        defects = [];
        return true;
    }

    /// <summary>
    /// Pins the exact dependency receipts one package is about to be prepared against.
    /// </summary>
    /// <param name="dependant">The prepared-but-unpublished dependant receipt.</param>
    /// <param name="defects">Why the pin was refused, or an empty list.</param>
    /// <returns><see langword="true"/> when every dependency was still rooted and is now pinned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dependant"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Taken before the package reads a byte or runs a line, so a refusal costs nothing and a success
    /// cannot be invalidated by a concurrent withdrawal. Every path out of preparation must reach either
    /// <see cref="TryPublish"/>, which converts the pins into edges, or <see cref="CompleteWithdrawal"/>,
    /// which releases them once nothing this attempt loaded can still need them.
    /// </remarks>
    internal bool TryPinDependencies(PackageAdmissionReceipt dependant, out IReadOnlyList<string> defects)
    {
        ArgumentNullException.ThrowIfNull(dependant);

        using var publication = _publication.EnterWrite();

        if (_preparing.ContainsKey(dependant))
        {
            defects = [$"Package '{dependant.Id}' already holds preparation pins for this attempt."];
            return false;
        }

        // A retained attempt's code may still be resident, so its identifier is occupied for the life of
        // the process. Refusing here rather than at publication is what stops a replacement running a
        // single line against types the incumbent may still hold.
        if (_retained.TryGetValue(dependant.Id, out var retained) && !ReferenceEquals(retained.Receipt, dependant))
        {
            defects =
            [
                $"Package '{dependant.Id}' is retained by an installation attempt at version "
                + $"{retained.Receipt.Version} whose instances, load context or contract hold could not be "
                + "released. Its identifier stays occupied for the life of the process.",
            ];
            return false;
        }

        // A second attempt preparing the same identifier at the same time would race for every resource
        // this one is about to take. Nothing in the platform produces one today; the check is here because
        // the cost of being wrong is code from two attempts running against one identifier's state.
        if (_preparing.Keys.Any(other => other.Id == dependant.Id))
        {
            defects =
            [
                $"Package '{dependant.Id}' is already being prepared by another installation attempt.",
            ];
            return false;
        }

        var found = new List<string>();

        foreach (var edge in dependant.Edges)
        {
            if (!_rooted.TryGetValue(edge.Dependency, out var current))
            {
                found.Add(
                    $"dependency[{edge.Dependency}]: extension '{dependant.Id}' requires it, and it is no "
                    + "longer admitted. This extension was not loaded.");
                continue;
            }

            if (!ReferenceEquals(current.Receipt, edge.DependencyReceipt))
            {
                found.Add(
                    $"dependency[{edge.Dependency}]: extension '{dependant.Id}' was resolved against a "
                    + $"different installation attempt of it than the one now admitted at version "
                    + $"{current.Receipt.Version}. This extension was not loaded.");
                continue;
            }

            if (current.IsWithdrawing)
            {
                found.Add(
                    $"dependency[{edge.Dependency}]: extension '{dependant.Id}' requires it, and it is "
                    + "being withdrawn. This extension was not loaded.");
            }
        }

        if (found.Count > 0)
        {
            defects = found;
            return false;
        }

        _preparing[dependant] = [.. dependant.Edges];
        defects = [];
        return true;
    }

    /// <summary>
    /// Marks exactly this receipt as withdrawing, keeping every pin and edge it holds.
    /// </summary>
    /// <param name="receipt">The receipt being withdrawn.</param>
    /// <param name="blockedBy">The dependants that refused the withdrawal, or an empty list.</param>
    /// <returns><see langword="false"/> when a live dependant still pins this package.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="receipt"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The first of two phases. Nothing new may bind to a withdrawing package, and the identifier stays
    /// occupied so a replacement cannot take it while the incumbent's code is still resident. What this
    /// deliberately does not do is release anything this package holds: its disposers have not run yet, and
    /// they run against its dependencies' types. A receipt that is not the rooted one reports success and
    /// changes nothing, so rolling back an attempt that never published removes nothing.
    /// </remarks>
    internal bool BeginWithdrawal(PackageAdmissionReceipt receipt, out IReadOnlyList<PluginId> blockedBy)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        using var publication = _publication.EnterWrite();

        if (!_rooted.TryGetValue(receipt.Id, out var rooted) || !ReferenceEquals(rooted.Receipt, receipt))
        {
            blockedBy = [];
            return true;
        }

        var dependants = LiveDependants(receipt);

        if (dependants.Count > 0)
        {
            blockedBy = dependants;
            return false;
        }

        _rooted[receipt.Id] = rooted with { IsWithdrawing = true };
        blockedBy = [];
        return true;
    }

    /// <summary>
    /// Releases everything exactly this receipt holds, once its code is definitively gone.
    /// </summary>
    /// <param name="receipt">The receipt being finalized.</param>
    /// <exception cref="ArgumentNullException"><paramref name="receipt"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The second of two phases, and the only place a package stops pinning its dependencies. Call it after
    /// disposal and unload have completed without a reported failure; a package whose code could not be
    /// released keeps its pins, because its dependencies' types may still be reachable from it. Removal is
    /// by exact reference throughout: the rooted entry only when it is this attempt, the edges only the ones
    /// this receipt owns, and the pins only this receipt's.
    /// </remarks>
    internal void CompleteWithdrawal(PackageAdmissionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        using var publication = _publication.EnterWrite();

        _preparing.Remove(receipt);

        if (!_rooted.TryGetValue(receipt.Id, out var rooted) || !ReferenceEquals(rooted.Receipt, receipt))
        {
            return;
        }

        _rooted.Remove(receipt.Id);
        _edges.RemoveAll(edge => receipt.Edges.Any(owned => ReferenceEquals(owned, edge)));
    }

    /// <summary>
    /// Records an attempt whose code could not be released, so its identifier stays occupied.
    /// </summary>
    /// <param name="receipt">The attempt that could not be released.</param>
    /// <exception cref="ArgumentNullException"><paramref name="receipt"/> is <see langword="null"/>.</exception>
    /// <param name="lifetime">
    /// The exact package lifetime that owns the code which could not be released. It is rooted here, not
    /// merely named: the receipt describes the attempt, and it is the lifetime that holds the load context,
    /// the instances and the contract hold. Rooting the description alone would leave every one of them
    /// collectible while the platform reported them retained, which is the same false claim in a different
    /// record — so there is no overload without one.
    /// </param>
    /// <remarks>
    /// An attempt that failed to publish and then failed to release is not merely a pending preparation. Its
    /// instances, load context or contract hold may still be resident, so a replacement must not take the
    /// identifier and the dependencies it pinned must stay pinned. This is terminal for the process:
    /// retrying would decide the same way from the same evidence.
    /// </remarks>
    internal void RetainFailedAttempt(PackageAdmissionReceipt receipt, PackageAdmissionLease lifetime)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(lifetime);

        using var publication = _publication.EnterWrite();
        _retained.TryAdd(receipt.Id, new RetainedAttempt(receipt, lifetime));
    }

    /// <summary>
    /// Determines whether any dependant still pins this exact receipt.
    /// </summary>
    /// <param name="receipt">The dependency receipt.</param>
    /// <param name="dependants">The pinning dependants, or an empty list.</param>
    /// <returns><see langword="true"/> when at least one dependant still pins it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="receipt"/> is <see langword="null"/>.</exception>
    internal bool HasLiveDependants(PackageAdmissionReceipt receipt, out IReadOnlyList<PluginId> dependants)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        using var publication = _publication.EnterRead();
        dependants = LiveDependants(receipt);
        return dependants.Count > 0;
    }

    /// <summary>Finds the receipt currently rooting one package identifier.</summary>
    /// <param name="package">The identifier.</param>
    /// <param name="receipt">The rooted receipt, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a receipt roots the identifier.</returns>
    internal bool TryGetRooted(PluginId package, out PackageAdmissionReceipt? receipt)
    {
        using var publication = _publication.EnterRead();

        if (_rooted.TryGetValue(package, out var rooted))
        {
            receipt = rooted.Receipt;
            return true;
        }

        receipt = null;
        return false;
    }

    /// <summary>
    /// Gets the position one package was published at, or <see langword="null"/> when it is not rooted.
    /// </summary>
    /// <param name="receipt">The receipt.</param>
    /// <returns>The publication position.</returns>
    /// <remarks>
    /// Withdrawal reverses the order packages were actually published in rather than recomputing an order
    /// from declarations at shutdown. A declaration edited while the host ran cannot change teardown order.
    /// </remarks>
    internal long? PublicationOrderOf(PackageAdmissionReceipt? receipt)
    {
        if (receipt is null)
        {
            return null;
        }

        using var publication = _publication.EnterRead();

        return _rooted.TryGetValue(receipt.Id, out var rooted) && ReferenceEquals(rooted.Receipt, receipt)
            ? rooted.Sequence
            : null;
    }

    /// <remarks>
    /// A package being prepared against this receipt is as live a dependant as one already serving. It has
    /// resolved contracts and may be executing registration code against this package's types right now,
    /// which is exactly the state in which releasing this package would be unsound.
    /// </remarks>
    private IReadOnlyList<PluginId> LiveDependants(PackageAdmissionReceipt receipt)
        =>
        [
            .. _edges
                .Concat(_preparing.Values.SelectMany(pinned => pinned))
                .Where(edge => ReferenceEquals(edge.DependencyReceipt, receipt))
                .Select(edge => edge.Dependant)
                .Distinct()
                .OrderBy(id => id.Value, StringComparer.Ordinal),
        ];

    private sealed record RootedPackage(
        PackageAdmissionReceipt Receipt,
        long Sequence,
        bool IsWithdrawing = false);

    /// <summary>
    /// One attempt whose code could not be released: what it was, and the lifetime that still owns it.
    /// </summary>
    /// <param name="Receipt">The attempt, which is all any diagnostic is shown.</param>
    /// <param name="Lifetime">
    /// The package lifetime holding its load context, its instances and its contract hold. Rooted for the
    /// life of the process, which is the whole point: the platform has said these objects may still be
    /// resident, and something has to make that true.
    /// </param>
    private sealed record RetainedAttempt(PackageAdmissionReceipt Receipt, PackageAdmissionLease Lifetime);
}
