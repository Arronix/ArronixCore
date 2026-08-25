using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;


namespace Arronix.Host.Storage;

/// <summary>
/// The store this milestone ships: everything in memory, every invariant enforced.
/// </summary>
/// <remarks>
/// <para>
/// The invariants are enforced from day one deliberately. A store that accepted anything now and grew rules
/// when persistence arrived would let the first four media kinds be written against rules that did not yet
/// exist, and the rules would then be negotiated against the code rather than against the declarations. Here
/// the shape's own booleans are the constraints, checked on the write that would break them.
/// </para>
/// <para>
/// One code path, four media kinds. Nothing in this file names a media concept: the cardinality rules come
/// from the file binding, the span rules from the declared coordinate components, and the chosen-variant
/// rule from the variant level's position in the hierarchy.
/// </para>
/// <para>
/// Writes are serialized through one asynchronous gate rather than a monitor, because checking a span rule
/// needs the unit's coordinates and those come from the owning extension's projection — an await that a
/// monitor could not span. One gate for a store that holds a single deployment's library state is not the
/// bottleneck; the relational store that replaces this will use the database's own concurrency control.
/// </para>
/// </remarks>
/// <param name="kinds">The registry the declared invariants are read from.</param>
/// <param name="items">How a kind's item source is reached, under the contributing extension's ticket.</param>
internal sealed class InMemoryMediaStore(MediaKindRegistry kinds, MediaItemBroker items)
    : IMediaStore, IDisposable
{
    private readonly MediaKindRegistry _kinds = kinds ?? throw new ArgumentNullException(nameof(kinds));
    private readonly MediaItemBroker _items = items ?? throw new ArgumentNullException(nameof(items));
    private readonly ConcurrentDictionary<MediaItemRef, LibraryFacet> _library = new();
    private readonly ConcurrentDictionary<MediaFileId, MediaFileRecord> _files = new();
    private readonly Dictionary<MediaItemRef, List<UnitFileLink>> _linksByUnit = [];
    private readonly Dictionary<MediaFileId, List<UnitFileLink>> _linksByFile = [];
    private readonly Dictionary<MediaItemRef, List<GroupMembership>> _membersByGroup = [];
    private readonly Dictionary<MediaItemRef, List<GroupMembership>> _groupsByMember = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _nextFileId;

    /// <inheritdoc />
    public ValueTask<LibraryFacet?> FindLibraryAsync(
        MediaItemRef reference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_library.GetValueOrDefault(reference));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<MediaItemRef, LibraryFacet>> FindLibraryManyAsync(
        IReadOnlyList<MediaItemRef> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        cancellationToken.ThrowIfCancellationRequested();

        var found = new Dictionary<MediaItemRef, LibraryFacet>(references.Count);

        foreach (var reference in references)
        {
            if (_library.TryGetValue(reference, out var facet))
            {
                found[reference] = facet;
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<MediaItemRef, LibraryFacet>>(found);
    }

    /// <inheritdoc />
    public async ValueTask UpsertLibraryAsync(LibraryFacet facet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facet);

        var shape = ShapeOf(facet.Ref);
        GuardMonitorDimensions(shape, facet);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            GuardSelectedVariant(shape, facet);
            _library[facet.Ref] = facet;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask<MediaFileId> UpsertFileAsync(
        MediaFileRecord file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();

        var id = file.Id.Value == 0
            ? MediaFileId.FromInt64(Interlocked.Increment(ref _nextFileId))
            : file.Id;

        _files[id] = file with { Id = id };
        return ValueTask.FromResult(id);
    }

    /// <inheritdoc />
    public ValueTask<MediaFileRecord?> FindFileAsync(
        MediaFileId file,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_files.GetValueOrDefault(file));
    }

    /// <inheritdoc />
    public async ValueTask LinkAsync(UnitFileLink link, CancellationToken cancellationToken = default)
    {
        var registered = _kinds.Require(link.Unit.Kind);
        var shape = ShapeOf(link.Unit);
        var binding = shape.Declaration.FileBinding;

        if (link.Unit.Level != binding.UnitLevelId)
        {
            throw Refuse(
                CoreErrorCode.ImportValidationFailed,
                $"A file satisfies units at level '{binding.UnitLevelId}'; '{link.Unit}' is at '{link.Unit.Level}'.");
        }

        if (link.Ordinal is not null && !binding.OrdinalIsMeaningful)
        {
            throw Refuse(
                CoreErrorCode.ImportValidationFailed,
                $"Media kind '{shape.Kind}' gives a file's position within a unit no meaning, so a link carries no ordinal.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var forUnit = GetOrAdd(_linksByUnit, link.Unit);
            var forFile = GetOrAdd(_linksByFile, link.File);

            if (forUnit.Exists(existing => existing.File == link.File))
            {
                return;
            }

            // The two booleans ARE the uniqueness constraints, and this is where they are enforced.
            if (binding.AtMostOneFilePerUnit && forUnit.Count > 0)
            {
                throw Refuse(
                    CoreErrorCode.ImportValidationFailed,
                    $"Unit '{link.Unit}' already has a file and media kind '{shape.Kind}' allows at most one.");
            }

            if (binding.AtMostOneUnitPerFile && forFile.Count > 0)
            {
                throw Refuse(
                    CoreErrorCode.ImportValidationFailed,
                    $"File {link.File} already satisfies a unit and media kind '{shape.Kind}' allows at most one.");
            }

            await GuardSpanConstraintsAsync(registered, shape, link, forFile, cancellationToken)
                .ConfigureAwait(false);

            forUnit.Add(link);
            forFile.Add(link);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask UnlinkAsync(UnitFileLink link, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_linksByUnit.TryGetValue(link.Unit, out var forUnit))
            {
                forUnit.RemoveAll(existing => existing.File == link.File);
            }

            if (_linksByFile.TryGetValue(link.File, out var forFile))
            {
                forFile.RemoveAll(existing => existing.Unit == link.Unit);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UnitFileLink>> LinksForUnitAsync(
        MediaItemRef unit,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return _linksByUnit.TryGetValue(unit, out var found)
                ? [.. found.OrderBy(link => link.Ordinal ?? 0).ThenBy(link => link.File.Value)]
                : [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<UnitFileLink>> LinksForFileAsync(
        MediaFileId file,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return _linksByFile.TryGetValue(file, out var found) ? [.. found] : [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask SetGroupMembershipAsync(
        GroupMembership membership,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var members = GetOrAdd(_membersByGroup, membership.Group);
            var groups = GetOrAdd(_groupsByMember, membership.Member);

            members.RemoveAll(existing => existing.Member == membership.Member);
            groups.RemoveAll(existing => existing.Group == membership.Group);

            // A designated primary membership is single-valued per member, so setting one clears the rest.
            if (membership.IsPrimary)
            {
                for (var index = 0; index < groups.Count; index++)
                {
                    groups[index] = groups[index] with { IsPrimary = false };
                }

                DemotePrimaryElsewhere(membership);
            }

            members.Add(membership);
            groups.Add(membership);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask RemoveGroupMembershipAsync(
        MediaItemRef group,
        MediaItemRef member,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_membersByGroup.TryGetValue(group, out var members))
            {
                members.RemoveAll(existing => existing.Member == member);
            }

            if (_groupsByMember.TryGetValue(member, out var groups))
            {
                groups.RemoveAll(existing => existing.Group == group);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<GroupMembership>> MembersOfAsync(
        MediaItemRef group,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return _membersByGroup.TryGetValue(group, out var found)
                ? [.. found.OrderBy(membership => membership.SortIndex)]
                : [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<GroupMembership>> GroupsOfAsync(
        MediaItemRef member,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return _groupsByMember.TryGetValue(member, out var found) ? [.. found] : [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private static ArronixException Refuse(CoreErrorCode code, string message) => new(code, message);

    private static List<TValue> GetOrAdd<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        return list;
    }

    private void DemotePrimaryElsewhere(GroupMembership membership)
    {
        foreach (var members in _membersByGroup.Values)
        {
            for (var index = 0; index < members.Count; index++)
            {
                if (members[index].Member == membership.Member
                    && members[index].Group != membership.Group
                    && members[index].IsPrimary)
                {
                    members[index] = members[index] with { IsPrimary = false };
                }
            }
        }
    }

    private ValidatedShape ShapeOf(MediaItemRef reference)
    {
        var registered = _kinds.Require(reference.Kind);

        if (!registered.Shape.HasLevel(reference.Level))
        {
            throw Refuse(
                CoreErrorCode.MediaItemNotFound,
                $"Media kind '{reference.Kind}' has no level '{reference.Level}'.");
        }

        return registered.Shape;
    }

    private static void GuardMonitorDimensions(ValidatedShape shape, LibraryFacet facet)
    {
        if (facet.Monitor.Count == 0)
        {
            return;
        }

        var level = shape.LevelOf(facet.Ref.Level);

        foreach (var (dimensionId, value) in facet.Monitor)
        {
            var dimension = level.MonitorDimensions
                .FirstOrDefault(candidate => string.Equals(candidate.DimensionId, dimensionId, StringComparison.Ordinal));

            if (dimension is null)
            {
                throw Refuse(
                    CoreErrorCode.InvalidConfiguration,
                    $"Level '{facet.Ref.Level}' of media kind '{shape.Kind}' declares no monitoring axis '{dimensionId}'.");
            }

            if (dimension.Kind == MonitorDimensionKind.Enumerated
                && !dimension.Choices.Any(choice => string.Equals(choice.Value, value, StringComparison.Ordinal)))
            {
                throw Refuse(
                    CoreErrorCode.InvalidConfiguration,
                    $"'{value}' is not one of the choices monitoring axis '{dimensionId}' declares.");
            }
        }
    }

    private void GuardSelectedVariant(ValidatedShape shape, LibraryFacet facet)
    {
        if (facet.SelectedVariant is not { } selected)
        {
            return;
        }

        if (shape.VariantLevel is not { } variantLevel)
        {
            throw Refuse(
                CoreErrorCode.InvalidConfiguration,
                $"Media kind '{shape.Kind}' has no variant level, so no manifestation can be chosen.");
        }

        if (selected.Level != variantLevel.Id)
        {
            throw Refuse(
                CoreErrorCode.InvalidConfiguration,
                $"A chosen manifestation is at level '{variantLevel.Id}'; '{selected}' is at '{selected.Level}'.");
        }

        if (variantLevel.Parent != facet.Ref.Level)
        {
            throw Refuse(
                CoreErrorCode.InvalidConfiguration,
                $"A chosen manifestation is recorded on level '{variantLevel.Parent}'; this facet is for '{facet.Ref.Level}'.");
        }

        // Because the choice lives on the parent's facet there is exactly one slot per parent, so the
        // at-most-one invariant two of the surveyed applications hand-roll in a repository holds by
        // construction. What remains to check is that one manifestation is not claimed by two parents.
        foreach (var other in _library.Values)
        {
            if (other.Ref != facet.Ref && other.SelectedVariant == selected)
            {
                throw Refuse(
                    CoreErrorCode.InvalidConfiguration,
                    $"'{selected}' is already the chosen manifestation of '{other.Ref}'.");
            }
        }
    }

    private async ValueTask GuardSpanConstraintsAsync(
        RegisteredMediaKind registered,
        ValidatedShape shape,
        UnitFileLink link,
        List<UnitFileLink> existingForFile,
        CancellationToken cancellationToken)
    {
        var constraints = shape.Declaration.FileBinding.SpanConstraints
            .Where(constraint => constraint.Rule == SpanRule.MustNotSpan)
            .ToList();

        if (constraints.Count == 0 || existingForFile.Count == 0)
        {
            return;
        }

        // One ticket for the whole span check: both reads are the extension's own item source, and a
        // teardown landing between them would leave the constraint decision half-made.
        // Refused rather than skipped when the kind disappears: this is the check that decides whether a
        // link may exist at all, and a withdrawal that silently satisfied it would admit the link the rule
        // forbids.
        await _items.RequireItemsAsync(
            registered.Kind,
            async (source, token) =>
            {
                await GuardAsync(source, shape, link, existingForFile, constraints, token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask GuardAsync(
        IMediaItemSource source,
        ValidatedShape shape,
        UnitFileLink link,
        List<UnitFileLink> existingForFile,
        List<SpanConstraint> constraints,
        CancellationToken cancellationToken)
    {
        var incoming = await source.GetAsync(link.Unit, cancellationToken).ConfigureAwait(false);

        if (incoming is null)
        {
            return;
        }

        foreach (var existing in existingForFile)
        {
            var other = await source.GetAsync(existing.Unit, cancellationToken).ConfigureAwait(false);

            if (other is null)
            {
                continue;
            }

            foreach (var constraint in constraints)
            {
                var space = shape.SpaceOf(constraint.SpaceId);

                if (space is null || space.Kind != CoordinateKind.Ordinal)
                {
                    continue;
                }

                var componentIndex = IndexOfComponent(space, constraint.ComponentId);

                if (componentIndex < 0)
                {
                    continue;
                }

                var left = ComponentOf(incoming.Coordinates, constraint.SpaceId, componentIndex);
                var right = ComponentOf(other.Coordinates, constraint.SpaceId, componentIndex);

                if (left is not null && right is not null && left != right)
                {
                    throw Refuse(
                        CoreErrorCode.ImportValidationFailed,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Media kind '{shape.Kind}' forbids one file spanning component '{constraint.ComponentId}' of space '{constraint.SpaceId}'; the units read {left} and {right}."));
                }
            }
        }
    }

    private static int IndexOfComponent(CoordinateSpace space, string componentId)
    {
        for (var index = 0; index < space.Components.Count; index++)
        {
            if (string.Equals(space.Components[index].ComponentId, componentId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static long? ComponentOf(CoordinateSet coordinates, string spaceId, int componentIndex)
    {
        if (!coordinates.TryGet(spaceId, out var reading)
            || reading.Value.Kind != CoordinateKind.Ordinal
            || componentIndex >= reading.Value.Ordinals.Length)
        {
            return null;
        }

        return reading.Value.Ordinals[componentIndex];
    }
}
