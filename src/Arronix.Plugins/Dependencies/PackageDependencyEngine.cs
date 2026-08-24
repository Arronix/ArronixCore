using System.Linq;
using Arronix.Abstractions.Plugins;

namespace Arronix.Plugins.Dependencies;

/// <summary>
/// Decides which installed packages may be activated, in which order, and why the rest may not.
/// </summary>
/// <remarks>
/// <para>
/// The engine never prefers. Two copies of one package, two statements of one requirement, and a cycle are
/// refusals rather than resolutions, because a resolver that picks the higher version or the first folder
/// produces a different installation depending on how the disk was walked.
/// </para>
/// <para>
/// Every supplied identifier ends up either in the activation order or in the ineligible list, every
/// ineligible identifier carries at least one diagnostic naming it, and a package unrelated to a broken one
/// keeps its place.
/// </para>
/// <para>
/// The engine reasons about identifiers and versions only. Nothing here loads an assembly, holds a
/// <see cref="System.Type"/> or reads a media-kind identifier.
/// </para>
/// </remarks>
internal static class PackageDependencyEngine
{
    /// <summary>
    /// Resolves one set of installed candidates.
    /// </summary>
    /// <param name="installed">The installed candidates, in any order.</param>
    /// <param name="activationOrder">The eligible packages, dependencies before dependants.</param>
    /// <param name="ineligiblePackages">The identifiers that may not be activated, in identifier order.</param>
    /// <param name="diagnostics">Every reason an identifier is ineligible.</param>
    /// <exception cref="ArgumentNullException"><paramref name="installed"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="installed"/> contains a null entry.</exception>
    /// <remarks>
    /// The result is invariant under every permutation of <paramref name="installed"/>, and under every
    /// permutation of each candidate's requirement list.
    /// </remarks>
    public static void Resolve(
        IEnumerable<InstalledPackage> installed,
        out IReadOnlyList<InstalledPackage> activationOrder,
        out IReadOnlyList<PluginId> ineligiblePackages,
        out IReadOnlyList<PackageDependencyDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(installed);

        new Resolver(installed).Resolve(out activationOrder, out ineligiblePackages, out diagnostics);
    }

    /// <summary>One resolution in progress; the passes share derived state.</summary>
    private sealed class Resolver
    {
        private readonly Dictionary<PluginId, List<InstalledPackage>> _copies = [];
        private readonly Dictionary<PluginId, InstalledPackage> _unique = [];
        private readonly Dictionary<PluginId, IReadOnlyList<PluginId>> _declared = [];
        private readonly Dictionary<PluginId, IReadOnlyList<PluginId>> _resolved = [];
        private readonly Dictionary<PluginId, HashSet<PluginId>> _reachable = [];
        private readonly Dictionary<PluginId, HashSet<PluginId>> _component = [];
        private readonly Dictionary<(PluginId Package, PluginId Dependency), PackageRequirement> _requirements = [];
        private readonly HashSet<(PluginId Package, PluginId Dependency)> _explained = [];
        private readonly List<PackageDependencyDiagnostic> _diagnostics = [];
        private readonly HashSet<PluginId> _invalid = [];
        private readonly PluginId[] _ids;

        public Resolver(IEnumerable<InstalledPackage> installed)
        {
            foreach (var candidate in installed)
            {
                if (candidate is null)
                {
                    throw new ArgumentException("The installed candidates must not contain a null entry.", nameof(installed));
                }

                if (!_copies.TryGetValue(candidate.Id, out var found))
                {
                    found = [];
                    _copies.Add(candidate.Id, found);
                }

                found.Add(candidate);
            }

            _ids = [.. _copies.Keys.Order(PackageIdentity.Order)];
        }

        public void Resolve(
            out IReadOnlyList<InstalledPackage> activationOrder,
            out IReadOnlyList<PluginId> ineligiblePackages,
            out IReadOnlyList<PackageDependencyDiagnostic> diagnostics)
        {
            ReportDuplicatePackages();
            ReportUnavailablePackages();
            ReportRequirementDefects();
            ComputeReachability();
            ReportCycles();

            var ineligible = PropagateIneligibility();
            ReportIneligibleDependencies(ineligible);

            activationOrder = BuildActivationOrder(ineligible);
            ineligiblePackages = [.. ineligible.Order(PackageIdentity.Order)];
            diagnostics =
            [
                .. _diagnostics
                    .OrderBy(static diagnostic => diagnostic.Package, PackageIdentity.Order)
                    .ThenBy(static diagnostic => (int)diagnostic.Kind)
                    .ThenBy(static diagnostic => diagnostic.Dependency?.Value ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
            ];
        }

        /// <summary>
        /// An identifier installed more than once is a refusal, not a selection.
        /// </summary>
        /// <remarks>
        /// Two copies of the same version are refused as firmly as two different versions. The engine has
        /// no way to know they are the same package rather than two builds that happen to agree about a
        /// number, and "they looked identical" is not a rule anyone can reason about later.
        /// </remarks>
        private void ReportDuplicatePackages()
        {
            foreach (var id in _ids)
            {
                var found = _copies[id];
                if (found.Count == 1)
                {
                    _unique.Add(id, found[0]);
                    continue;
                }

                _invalid.Add(id);

                // Ordered by version so the list reads in a useful order, and then by the exact text that
                // will be printed. The second key is what makes the sequence a property of the copies
                // rather than of the order they arrived in: two copies that tie on version and origin
                // render identically, so no tie between them is observable.
                var described = string.Join(
                    ", ",
                    found
                        .OrderBy(static candidate => candidate.Version)
                        .ThenBy(static candidate => candidate.Described, StringComparer.Ordinal)
                        .Select(static candidate => candidate.Described));

                _diagnostics.Add(new PackageDependencyDiagnostic(
                    PackageDependencyDiagnosticKind.DuplicatePackage,
                    id,
                    dependency: null,
                    $"Package '{id}' is installed {found.Count} times ({described}). Remove every copy but "
                    + "one: the graph never chooses between them by folder order, by version or by "
                    + "discovery order."));
            }
        }

        /// <summary>
        /// Refuses every package the caller said can never be activated.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Directly at fault, exactly like a package that is broken on its own terms, and for the same
        /// reason: an installed package that will not start cannot satisfy anything that requires it. The
        /// alternative — leaving it eligible and letting the caller skip it later — activates a dependant
        /// whose dependency provably never arrives.
        /// </para>
        /// <para>
        /// It stays <i>installed</i>, though. A dependant of it is told the package cannot be activated and
        /// why, not that nothing with that identifier was installed, which would be false and would send an
        /// operator looking for a package that is sitting right there.
        /// </para>
        /// <para>
        /// A duplicated identifier is skipped: it has no single candidate to read an answer from, and it is
        /// already refused.
        /// </para>
        /// </remarks>
        private void ReportUnavailablePackages()
        {
            foreach (var id in _ids)
            {
                if (!_unique.TryGetValue(id, out var candidate)
                    || candidate.Availability == PackageAvailability.Available)
                {
                    continue;
                }

                _invalid.Add(id);

                _diagnostics.Add(new PackageDependencyDiagnostic(
                    PackageDependencyDiagnosticKind.UnavailablePackage,
                    id,
                    dependency: null,
                    $"Package '{id}' is installed but cannot be activated: "
                    + $"{PackageAvailabilityReason.Describe(candidate.Availability)}."));
            }
        }

        /// <summary>
        /// Reads each unique candidate's requirements and reports the ones that cannot be met on their own
        /// terms: stated twice, absent, or present at the wrong version.
        /// </summary>
        /// <remarks>
        /// A duplicated identifier contributes no requirements at all. Its copies may declare different
        /// ones, and reading either list would be the choice this engine refuses to make; the identifier is
        /// already ineligible, and everything that depends on it becomes ineligible through it.
        /// </remarks>
        private void ReportRequirementDefects()
        {
            foreach (var id in _ids)
            {
                if (!_unique.TryGetValue(id, out var candidate))
                {
                    _declared[id] = [];
                    _resolved[id] = [];
                    continue;
                }

                var declared = new List<PluginId>();
                var resolved = new List<PluginId>();

                var stated = candidate.Requirements
                    .GroupBy(static requirement => requirement.PackageId)
                    .OrderBy(static group => group.Key, PackageIdentity.Order);

                foreach (var group in stated)
                {
                    var target = group.Key;
                    declared.Add(target);

                    if (!TryReadOneRequirement(id, target, group, out var requirement))
                    {
                        continue;
                    }

                    _requirements[(id, target)] = requirement;

                    if (!_copies.TryGetValue(target, out var found))
                    {
                        Refuse(
                            PackageDependencyDiagnosticKind.MissingDependency,
                            id,
                            target,
                            $"Package '{id}' requires '{target}' {requirement.Range}, but no package with "
                            + "that identifier is installed. Install it, or remove the requirement.");
                        continue;
                    }

                    if (found.Count > 1)
                    {
                        // The identifier is present, so this is not a missing dependency, and there is no
                        // one version to compare the range against. The duplicate is reported against the
                        // target itself and reaches this package through the ineligibility pass.
                        continue;
                    }

                    if (!requirement.Range.IsSatisfiedBy(found[0].Version))
                    {
                        Refuse(
                            PackageDependencyDiagnosticKind.IncompatibleDependency,
                            id,
                            target,
                            $"Package '{id}' requires '{target}' {requirement.Range}, but the installed "
                            + $"'{target}' is {found[0].Version}. Install a version the range admits, or "
                            + "widen the range.");
                        continue;
                    }

                    resolved.Add(target);
                }

                _declared[id] = [.. declared];
                _resolved[id] = [.. resolved];
            }
        }

        /// <summary>
        /// Reads the single requirement a package states for one dependency, refusing a repeated statement.
        /// </summary>
        /// <remarks>
        /// Intersecting two stated ranges would be the tempting reconciliation and it is the wrong one: the
        /// author wrote two things, at least one of which is not what they meant, and an intersection is a
        /// third thing neither of them said.
        /// </remarks>
        private bool TryReadOneRequirement(
            PluginId id,
            PluginId target,
            IEnumerable<PackageRequirement> group,
            out PackageRequirement requirement)
        {
            var found = group.ToArray();
            requirement = found[0];

            if (found.Length == 1)
            {
                return true;
            }

            var ranges = string.Join(
                ", ",
                found.Select(static stated => stated.Range.Text).Order(StringComparer.Ordinal));

            Refuse(
                PackageDependencyDiagnosticKind.DuplicateRequirement,
                id,
                target,
                $"Package '{id}' states a requirement on '{target}' {found.Length} times ({ranges}). State "
                + "each dependency once: the graph never chooses between two declared ranges and never "
                + "intersects them.");

            return false;
        }

        /// <summary>
        /// Records, for every identifier, everything it reaches over resolved edges in one step or more.
        /// </summary>
        /// <remarks>
        /// Reachability rather than a strongly-connected-component algorithm. An installation holds tens or
        /// hundreds of packages, so the extra factor buys nothing that matters and costs a reader the
        /// ability to check the cycle rule by reading it: an identifier is on a cycle exactly when it
        /// reaches itself.
        /// </remarks>
        private void ComputeReachability()
        {
            foreach (var id in _ids)
            {
                var seen = new HashSet<PluginId>();
                var pending = new Queue<PluginId>();

                foreach (var target in _resolved[id])
                {
                    if (seen.Add(target))
                    {
                        pending.Enqueue(target);
                    }
                }

                while (pending.Count > 0)
                {
                    foreach (var target in _resolved[pending.Dequeue()])
                    {
                        if (seen.Add(target))
                        {
                            pending.Enqueue(target);
                        }
                    }
                }

                _reachable.Add(id, seen);
            }
        }

        /// <summary>
        /// Reports every identifier that lies on a cycle, each with an actual cycle through itself.
        /// </summary>
        /// <remarks>
        /// One diagnostic per participant rather than one per cycle. A component can hold more cycles than
        /// it holds packages, so a single canonical loop would leave some participants unexplained, and an
        /// operator asking why one package will not start would have to read a diagnostic filed against a
        /// different one to find out.
        /// </remarks>
        private void ReportCycles()
        {
            foreach (var id in _ids)
            {
                if (!_reachable[id].Contains(id))
                {
                    continue;
                }

                _invalid.Add(id);

                var component = new HashSet<PluginId>(
                    _reachable[id].Where(candidate => _reachable[candidate].Contains(id)));
                _component.Add(id, component);

                var path = ShortestCycle(id, component);

                _diagnostics.Add(new PackageDependencyDiagnostic(
                    PackageDependencyDiagnosticKind.DependencyCycle,
                    id,
                    dependency: null,
                    $"Package '{id}' lies on a dependency cycle: {PackageIdentity.RenderPath(path)}. "
                    + "Dependencies must be acyclic; break the cycle by removing one of those requirements.",
                    path));
            }
        }

        /// <summary>
        /// Finds the shortest cycle from one identifier back to itself, taking the ordinally smallest such
        /// path when several are equally short.
        /// </summary>
        /// <remarks>
        /// A breadth-first search that keeps, for each identifier it reaches, the ordinally smallest of the
        /// shortest paths to it. That makes the reported path a property of the graph rather than of the
        /// search: it does not move when the caller enumerates the same packages in another order.
        /// </remarks>
        private IReadOnlyList<PluginId> ShortestCycle(PluginId start, IReadOnlySet<PluginId> component)
        {
            var paths = new Dictionary<PluginId, PluginId[]> { [start] = [start] };
            var frontier = new List<PluginId> { start };

            while (frontier.Count > 0)
            {
                PluginId[]? closed = null;
                foreach (var current in frontier)
                {
                    if (!_resolved[current].Contains(start))
                    {
                        continue;
                    }

                    var candidate = paths[current].Append(start).ToArray();
                    if (closed is null || ComparePaths(candidate, closed) < 0)
                    {
                        closed = candidate;
                    }
                }

                if (closed is not null)
                {
                    return closed;
                }

                var next = new Dictionary<PluginId, PluginId[]>();
                foreach (var current in frontier)
                {
                    foreach (var target in _resolved[current])
                    {
                        if (!component.Contains(target) || paths.ContainsKey(target))
                        {
                            continue;
                        }

                        var candidate = paths[current].Append(target).ToArray();
                        if (!next.TryGetValue(target, out var best) || ComparePaths(candidate, best) < 0)
                        {
                            next[target] = candidate;
                        }
                    }
                }

                foreach (var entry in next)
                {
                    paths.Add(entry.Key, entry.Value);
                }

                frontier = [.. next.Keys.Order(PackageIdentity.Order)];
            }

            // Unreachable: this method is only called for an identifier that reaches itself.
            throw new InvalidOperationException($"Package '{start}' was reported as cyclic but reaches no cycle.");
        }

        /// <summary>
        /// Spreads ineligibility from the packages that are directly at fault to everything that depends on
        /// them, however far away.
        /// </summary>
        /// <remarks>
        /// Over declared edges rather than resolved ones, because the edge to a package that is installed
        /// twice never became a resolved edge and is exactly the edge that has to carry the refusal.
        /// </remarks>
        private HashSet<PluginId> PropagateIneligibility()
        {
            var ineligible = new HashSet<PluginId>(_invalid);
            bool spread;

            do
            {
                spread = false;
                foreach (var id in _ids)
                {
                    if (ineligible.Contains(id) || !_declared[id].Any(ineligible.Contains))
                    {
                        continue;
                    }

                    ineligible.Add(id);
                    spread = true;
                }
            }
            while (spread);

            return ineligible;
        }

        /// <summary>
        /// Tells every package that is only ineligible because something it depends on is.
        /// </summary>
        /// <remarks>
        /// An edge already reported as missing, incompatible or stated twice is not reported again, and an
        /// edge that lies inside a cycle this package was already told about is not reported again either:
        /// its cycle path already contains that edge.
        /// </remarks>
        private void ReportIneligibleDependencies(IReadOnlySet<PluginId> ineligible)
        {
            foreach (var id in _ids)
            {
                foreach (var target in _declared[id])
                {
                    if (!ineligible.Contains(target)
                        || !_copies.ContainsKey(target)
                        || _explained.Contains((id, target))
                        || (_component.TryGetValue(id, out var component) && component.Contains(target)))
                    {
                        continue;
                    }

                    _diagnostics.Add(new PackageDependencyDiagnostic(
                        PackageDependencyDiagnosticKind.IneligibleDependency,
                        id,
                        target,
                        ExplainIneligibleDependency(id, target)));
                }
            }
        }

        /// <summary>
        /// Says why one edge cannot be met, in terms of the package that is actually at fault.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three cases, because three different things are the useful answer. A dependency the caller
        /// declared unable to start is quoted directly: the operator's own decision is the whole
        /// explanation, and pointing at diagnostics reported against it would point at that same sentence.
        /// A dependency broken on its own terms points at its diagnostics, which are filed under its name.
        /// </para>
        /// <para>
        /// A dependency that is itself only a dependant carries the closure. Without it, an operator reading
        /// a chain of five packages gets five identical "not eligible" messages and no fault; with it, the
        /// first message they read names the package to fix.
        /// </para>
        /// </remarks>
        private string ExplainIneligibleDependency(PluginId id, PluginId target)
        {
            var range = _requirements[(id, target)].Range;
            var opening = $"Package '{id}' requires '{target}' {range}, but ";

            if (Unavailability(target) is { } state)
            {
                return opening
                    + $"'{target}' cannot be activated: {PackageAvailabilityReason.Describe(state)}. "
                    + "Resolve that, or remove the requirement.";
            }

            var explanation = opening
                + $"'{target}' is not eligible. A package that depends on an ineligible package is itself "
                + $"ineligible: resolve the diagnostics reported against '{target}'.";

            if (_invalid.Contains(target))
            {
                return explanation;
            }

            var faults = RootFaults(target);
            return faults.Count == 0
                ? explanation
                : explanation + $" The fault is in {string.Join(", ", faults)}.";
        }

        /// <summary>
        /// Finds the packages a package's own dependency closure is actually broken by.
        /// </summary>
        /// <remarks>
        /// Everything directly at fault that this package reaches over the edges it declared, rather than
        /// over the ones that resolved: an edge to a package installed twice never resolved, and it is
        /// exactly the edge the closure has to be followed along. Ordinal, so the list is a property of the
        /// graph rather than of the walk that found it.
        /// </remarks>
        private IReadOnlyList<string> RootFaults(PluginId from)
        {
            var seen = new HashSet<PluginId>();
            var pending = new Queue<PluginId>();
            var faults = new List<PluginId>();

            pending.Enqueue(from);
            seen.Add(from);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();

                if (current != from && _invalid.Contains(current))
                {
                    faults.Add(current);
                    continue;
                }

                foreach (var target in _declared[current])
                {
                    if (_copies.ContainsKey(target) && seen.Add(target))
                    {
                        pending.Enqueue(target);
                    }
                }
            }

            return
            [
                .. faults
                    .Order(PackageIdentity.Order)
                    .Select(fault => Unavailability(fault) is { } state
                        ? $"'{fault}' ({PackageAvailabilityReason.Describe(state)})"
                        : $"'{fault}'")
            ];
        }

        /// <summary>
        /// Gets the state of a package which cannot be activated, or <see langword="null"/> when it can be
        /// or when there is no single candidate to ask.
        /// </summary>
        /// <remarks>
        /// A duplicated identifier has no one candidate to read a state from, which is why the lookup is
        /// through <c>_unique</c>: it is already refused on its own terms, and choosing one of its copies to
        /// speak for it is the decision this graph does not make.
        /// </remarks>
        private PackageAvailability? Unavailability(PluginId id)
            => _unique.TryGetValue(id, out var candidate)
                && candidate.Availability != PackageAvailability.Available
                    ? candidate.Availability
                    : null;

        /// <summary>
        /// Orders the eligible packages so that every package follows everything it requires.
        /// </summary>
        /// <remarks>
        /// The ready set is drained in package-identifier order, which makes this the ordinally smallest of
        /// the valid orders. Two packages that require nothing of each other therefore have one order rather
        /// than an order that depends on which of them the caller happened to list first.
        /// </remarks>
        private IReadOnlyList<InstalledPackage> BuildActivationOrder(IReadOnlySet<PluginId> ineligible)
        {
            var eligible = _ids.Where(id => !ineligible.Contains(id)).ToArray();
            var remaining = new Dictionary<PluginId, int>(eligible.Length);
            var dependants = new Dictionary<PluginId, List<PluginId>>(eligible.Length);

            foreach (var id in eligible)
            {
                remaining[id] = 0;
                dependants[id] = [];
            }

            foreach (var id in eligible)
            {
                foreach (var target in _resolved[id])
                {
                    // An eligible package's resolved targets are eligible: had one of them not been, the
                    // ineligibility pass would have reached this package through it.
                    remaining[id]++;
                    dependants[target].Add(id);
                }
            }

            var ready = new PriorityQueue<PluginId, PluginId>(PackageIdentity.Order);
            foreach (var id in eligible)
            {
                if (remaining[id] == 0)
                {
                    ready.Enqueue(id, id);
                }
            }

            var order = new List<InstalledPackage>(eligible.Length);
            while (ready.TryDequeue(out var next, out _))
            {
                order.Add(_unique[next]);

                foreach (var dependant in dependants[next])
                {
                    if (--remaining[dependant] == 0)
                    {
                        ready.Enqueue(dependant, dependant);
                    }
                }
            }

            if (order.Count != eligible.Length)
            {
                // Unreachable: every identifier on a cycle is ineligible, so the eligible graph is acyclic.
                throw new InvalidOperationException("The eligible packages could not be ordered.");
            }

            return order;
        }

        private void Refuse(PackageDependencyDiagnosticKind kind, PluginId id, PluginId target, string message)
        {
            _invalid.Add(id);
            _explained.Add((id, target));
            _diagnostics.Add(new PackageDependencyDiagnostic(kind, id, target, message));
        }

        private static int ComparePaths(IReadOnlyList<PluginId> left, IReadOnlyList<PluginId> right)
        {
            var shared = Math.Min(left.Count, right.Count);
            for (var index = 0; index < shared; index++)
            {
                var comparison = PackageIdentity.Order.Compare(left[index], right[index]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return left.Count.CompareTo(right.Count);
        }
    }
}
