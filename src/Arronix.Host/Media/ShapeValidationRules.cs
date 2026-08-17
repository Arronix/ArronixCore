using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Shape;

// The media-shape contracts are experimental. This file checks declarations written against them.
#pragma warning disable ARX0013

namespace Arronix.Host.Media;

/// <summary>
/// The sixteen rules a declared shape must satisfy before the host will resolve it.
/// </summary>
/// <remarks>
/// <para>
/// Each rule traces to a surveyed failure rather than to a preference. They are numbered in the
/// specification and the numbers are repeated in the comments here, so a rule can be argued about against
/// the evidence that produced it rather than against taste.
/// </para>
/// <para>
/// Every rule runs even when an earlier one has already failed, except where a later rule would need a
/// structure the earlier one proved absent. The whole list arrives in one build.
/// </para>
/// </remarks>
internal static class ShapeValidationRules
{
    /// <summary>
    /// Runs every rule.
    /// </summary>
    /// <param name="shape">The declaration.</param>
    /// <param name="defects">The list every fault is appended to.</param>
    /// <returns>
    /// The levels in topological order when the graph is sound; otherwise <see langword="null"/>, because
    /// every later rule needs the ordering.
    /// </returns>
    internal static IReadOnlyList<MediaLevel>? Check(MediaShape shape, List<ShapeDefect> defects)
    {
        var ordered = CheckLevelGraph(shape, defects);
        var levelsById = shape.Levels
            .GroupBy(level => level.Id)
            .ToDictionary(group => group.Key, group => group.First());

        CheckRoles(shape, defects, levelsById);
        CheckFileBinding(shape, defects, levelsById, ordered);
        CheckCoordinates(shape, defects);
        CheckGroupingsAndFacets(shape, defects, levelsById);
        CheckFormatFamilies(shape, defects);
        CheckFieldsAndSearches(shape, defects, levelsById, ordered);

        return ordered;
    }

    // Rules 2, 3 and 4.
    private static IReadOnlyList<MediaLevel>? CheckLevelGraph(MediaShape shape, List<ShapeDefect> defects)
    {
        if (shape.Levels.Count == 0)
        {
            defects.Add(new ShapeDefect("levels", "A shape declares at least one level.", CoreErrorCode.PluginShapeInvalid));
            return null;
        }

        var duplicates = shape.Levels
            .GroupBy(level => level.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            defects.Add(new ShapeDefect(
                $"levels[{duplicate}]",
                $"Level identifier '{duplicate}' is declared more than once.",
                CoreErrorCode.PluginShapeInvalid));
        }

        if (duplicates.Count > 0)
        {
            return null;
        }

        var levelsById = shape.Levels.ToDictionary(level => level.Id);
        var roots = shape.Levels.Where(level => level.Parent is null).ToList();

        if (roots.Count != 1)
        {
            defects.Add(new ShapeDefect(
                "levels",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A shape has exactly one root level; {roots.Count} levels declare no parent."),
                CoreErrorCode.PluginShapeInvalid));
            return null;
        }

        var dangling = false;
        foreach (var level in shape.Levels)
        {
            if (level.Parent is { } parent && !levelsById.ContainsKey(parent))
            {
                defects.Add(new ShapeDefect(
                    $"levels[{level.Id}].parent",
                    $"Parent '{parent}' is not a level of this shape.",
                    CoreErrorCode.PluginShapeInvalid));
                dangling = true;
            }
        }

        if (dangling)
        {
            return null;
        }

        // Rule 4: at most one child per level. The parent pointer already carries the freedom to branch;
        // this milestone validates linearity so that relaxing it later is a change to this method rather
        // than to a contract.
        foreach (var group in shape.Levels
            .Where(level => level.Parent is not null)
            .GroupBy(level => level.Parent!.Value)
            .Where(group => group.Count() > 1))
        {
            defects.Add(new ShapeDefect(
                $"levels[{group.Key}]",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Level '{group.Key}' has {group.Count()} children; a level has at most one in this contract version."),
                CoreErrorCode.PluginShapeInvalid));
        }

        // Rule 3: acyclic. Walking from each level to the root with a bounded step count detects a cycle
        // without allocating a visited set per level.
        var ordered = new List<MediaLevel>();
        var cursor = roots[0];
        var childrenByParent = shape.Levels
            .Where(level => level.Parent is not null)
            .GroupBy(level => level.Parent!.Value)
            .ToDictionary(group => group.Key, group => group.First());

        while (true)
        {
            ordered.Add(cursor);

            if (ordered.Count > shape.Levels.Count)
            {
                defects.Add(new ShapeDefect(
                    "levels",
                    "The level graph contains a cycle.",
                    CoreErrorCode.PluginShapeInvalid));
                return null;
            }

            if (!childrenByParent.TryGetValue(cursor.Id, out var child))
            {
                break;
            }

            cursor = child;
        }

        if (ordered.Count != shape.Levels.Count)
        {
            defects.Add(new ShapeDefect(
                "levels",
                "Every level is reachable from the root; the graph is disconnected or cyclic.",
                CoreErrorCode.PluginShapeInvalid));
            return null;
        }

        return ordered;
    }

    // Rules 5, 6 and 7.
    private static void CheckRoles(
        MediaShape shape,
        List<ShapeDefect> defects,
        Dictionary<MediaLevelId, MediaLevel> levelsById)
    {
        RequireExactlyOne(shape, defects, MediaLevelRoles.LibraryEntry);
        RequireAtLeastOne(shape, defects, MediaLevelRoles.AcquisitionUnit);
        RequireAtLeastOne(shape, defects, MediaLevelRoles.CompletenessUnit);
        RequireAtLeastOne(shape, defects, MediaLevelRoles.FileBearing);

        var variants = shape.Levels.Where(level => level.Roles.HasFlag(MediaLevelRoles.VariantAxis)).ToList();

        if (variants.Count > 1)
        {
            defects.Add(new ShapeDefect(
                "levels",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"At most one level carries the variant role; {variants.Count} do."),
                CoreErrorCode.PluginShapeInvalid));
        }

        foreach (var level in shape.Levels)
        {
            var isVariant = level.Roles.HasFlag(MediaLevelRoles.VariantAxis);

            if (isVariant != (level.Variant is not null))
            {
                defects.Add(new ShapeDefect(
                    $"levels[{level.Id}].variant",
                    isVariant
                        ? "A level carrying the variant role declares its variant selection."
                        : "A level declaring variant selection carries the variant role.",
                    CoreErrorCode.PluginShapeInvalid));
            }

            if (isVariant)
            {
                if (level.Parent is not { } parentId)
                {
                    defects.Add(new ShapeDefect(
                        $"levels[{level.Id}].parent",
                        "A variant level has a parent, because the competing manifestations are manifestations of something.",
                        CoreErrorCode.PluginShapeInvalid));
                }
                else if (levelsById.TryGetValue(parentId, out var parent)
                    && !parent.Roles.HasFlag(MediaLevelRoles.AcquisitionUnit))
                {
                    defects.Add(new ShapeDefect(
                        $"levels[{level.Id}].parent",
                        $"The parent of a variant level carries the acquisition role; '{parentId}' does not.",
                        CoreErrorCode.PluginShapeInvalid));
                }
            }
        }
    }

    // Rules 8, 9 and 13.
    private static void CheckFileBinding(
        MediaShape shape,
        List<ShapeDefect> defects,
        Dictionary<MediaLevelId, MediaLevel> levelsById,
        IReadOnlyList<MediaLevel>? ordered)
    {
        var binding = shape.FileBinding;

        var anchorResolved = levelsById.TryGetValue(binding.AnchorLevelId, out var anchor);
        var unitResolved = levelsById.TryGetValue(binding.UnitLevelId, out var unit);

        if (!anchorResolved)
        {
            defects.Add(new ShapeDefect(
                "fileBinding.anchor",
                $"Anchor level '{binding.AnchorLevelId}' is not a level of this shape.",
                CoreErrorCode.PluginShapeInvalid));
        }
        else if (!anchor!.Roles.HasFlag(MediaLevelRoles.FileBearing))
        {
            defects.Add(new ShapeDefect(
                "fileBinding.anchor",
                $"Anchor level '{binding.AnchorLevelId}' carries the file-bearing role.",
                CoreErrorCode.PluginShapeInvalid));
        }

        if (!unitResolved)
        {
            defects.Add(new ShapeDefect(
                "fileBinding.unit",
                $"Unit level '{binding.UnitLevelId}' is not a level of this shape.",
                CoreErrorCode.PluginShapeInvalid));
        }
        else if (!unit!.Roles.HasFlag(MediaLevelRoles.FileBearing))
        {
            defects.Add(new ShapeDefect(
                "fileBinding.unit",
                $"Unit level '{binding.UnitLevelId}' carries the file-bearing role.",
                CoreErrorCode.PluginShapeInvalid));
        }

        if (anchorResolved && unitResolved && ordered is not null)
        {
            var anchorIndex = IndexOf(ordered, binding.AnchorLevelId);
            var unitIndex = IndexOf(ordered, binding.UnitLevelId);

            if (anchorIndex >= 0 && unitIndex >= 0 && unitIndex < anchorIndex)
            {
                defects.Add(new ShapeDefect(
                    "fileBinding.unit",
                    $"Unit level '{binding.UnitLevelId}' is the anchor level or a descendant of it.",
                    CoreErrorCode.PluginShapeInvalid));
            }
        }

        // Rule 9. An ordinal on the join only carries information when one unit spans several files.
        if (binding.OrdinalIsMeaningful && binding.AtMostOneFilePerUnit)
        {
            defects.Add(new ShapeDefect(
                "fileBinding.ordinalIsMeaningful",
                "An ordinal is meaningful only when a unit may span more than one file.",
                CoreErrorCode.PluginShapeInvalid));
        }

        // Rule 13.
        var spacesById = SpacesById(shape);

        foreach (var constraint in binding.SpanConstraints)
        {
            if (!spacesById.TryGetValue(constraint.SpaceId, out var space))
            {
                defects.Add(new ShapeDefect(
                    $"fileBinding.spanConstraints[{constraint.SpaceId}]",
                    $"Coordinate space '{constraint.SpaceId}' is not declared by this shape.",
                    CoreErrorCode.PluginShapeInvalid));
                continue;
            }

            if (!space.Components.Any(component =>
                string.Equals(component.ComponentId, constraint.ComponentId, StringComparison.Ordinal)))
            {
                defects.Add(new ShapeDefect(
                    $"fileBinding.spanConstraints[{constraint.SpaceId}]",
                    $"Space '{constraint.SpaceId}' has no component '{constraint.ComponentId}'.",
                    CoreErrorCode.PluginShapeInvalid));
            }
        }
    }

    // Rules 10, 11 and 12.
    private static void CheckCoordinates(MediaShape shape, List<ShapeDefect> defects)
    {
        foreach (var group in shape.CoordinateSpaces
            .GroupBy(space => space.SpaceId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            defects.Add(new ShapeDefect(
                $"coordinateSpaces[{group.Key}]",
                $"Coordinate space '{group.Key}' is declared more than once.",
                CoreErrorCode.PluginShapeInvalid));
        }

        var spacesById = SpacesById(shape);

        foreach (var level in shape.Levels)
        {
            var resolved = new List<CoordinateSpace>();

            foreach (var spaceId in level.CoordinateSpaceIds)
            {
                if (spacesById.TryGetValue(spaceId, out var space))
                {
                    resolved.Add(space);
                }
                else
                {
                    defects.Add(new ShapeDefect(
                        $"levels[{level.Id}].coordinateSpaceIds",
                        $"Coordinate space '{spaceId}' is not declared by this shape.",
                        CoreErrorCode.PluginShapeInvalid));
                }
            }

            // Rule 11. Identity and completeness are measured in exactly one space, or the level carries no
            // coordinates at all.
            if (resolved.Count > 0)
            {
                var canonical = resolved.Count(space => space.IsCanonical);

                if (canonical != 1)
                {
                    defects.Add(new ShapeDefect(
                        $"levels[{level.Id}].coordinateSpaceIds",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"A level that carries coordinates references exactly one canonical space; this one references {canonical}."),
                        CoreErrorCode.PluginShapeInvalid));
                }
            }

            // Rule 12.
            foreach (var axis in level.SequenceAxes)
            {
                if (!spacesById.TryGetValue(axis.SpaceId, out var space))
                {
                    defects.Add(new ShapeDefect(
                        $"levels[{level.Id}].sequenceAxes[{axis.AxisId}].spaceId",
                        $"Coordinate space '{axis.SpaceId}' is not declared by this shape.",
                        CoreErrorCode.PluginShapeInvalid));
                    continue;
                }

                if (!level.CoordinateSpaceIds.Contains(axis.SpaceId, StringComparer.Ordinal))
                {
                    defects.Add(new ShapeDefect(
                        $"levels[{level.Id}].sequenceAxes[{axis.AxisId}].spaceId",
                        $"Space '{axis.SpaceId}' is not one this level carries.",
                        CoreErrorCode.PluginShapeInvalid));
                }

                if (space.Kind != CoordinateKind.Ordinal)
                {
                    defects.Add(new ShapeDefect(
                        $"levels[{level.Id}].sequenceAxes[{axis.AxisId}].spaceId",
                        $"A sequence axis names a component of an ordinal space; '{axis.SpaceId}' is {space.Kind}.",
                        CoreErrorCode.PluginShapeInvalid));
                    continue;
                }

                if (axis.ComponentIndex < 0 || axis.ComponentIndex >= space.Components.Count)
                {
                    defects.Add(new ShapeDefect(
                        $"levels[{level.Id}].sequenceAxes[{axis.AxisId}].componentIndex",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Component index {axis.ComponentIndex} is outside space '{axis.SpaceId}', which has {space.Components.Count} components."),
                        CoreErrorCode.PluginShapeInvalid));
                }
            }
        }
    }

    // Rule 14.
    private static void CheckGroupingsAndFacets(
        MediaShape shape,
        List<ShapeDefect> defects,
        Dictionary<MediaLevelId, MediaLevel> levelsById)
    {
        foreach (var axis in shape.GroupingAxes)
        {
            if (!levelsById.ContainsKey(axis.MemberLevelId))
            {
                defects.Add(new ShapeDefect(
                    $"groupingAxes[{axis.AxisId}].memberLevelId",
                    $"Member level '{axis.MemberLevelId}' is not a level of this shape.",
                    CoreErrorCode.PluginShapeInvalid));
            }

            // A designated primary member only means something when a member can belong to several groups;
            // under many-to-one the single group is the primary one by construction.
            if (axis.HasPrimaryMember && axis.Arity != GroupingArity.ManyToMany)
            {
                defects.Add(new ShapeDefect(
                    $"groupingAxes[{axis.AxisId}].hasPrimaryMember",
                    "A primary member is meaningful only on a many-to-many grouping axis.",
                    CoreErrorCode.PluginShapeInvalid));
            }
        }

        foreach (var group in shape.GroupingAxes
            .GroupBy(axis => axis.AxisId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            defects.Add(new ShapeDefect(
                $"groupingAxes[{group.Key}]",
                $"Grouping axis '{group.Key}' is declared more than once.",
                CoreErrorCode.PluginShapeInvalid));
        }

        foreach (var facet in shape.SelectionFacets)
        {
            if (!levelsById.ContainsKey(facet.AppliesToLevelId))
            {
                defects.Add(new ShapeDefect(
                    $"selectionFacets[{facet.FacetId}].appliesToLevelId",
                    $"Level '{facet.AppliesToLevelId}' is not a level of this shape.",
                    CoreErrorCode.PluginShapeInvalid));
            }
        }
    }

    // Rule 15.
    private static void CheckFormatFamilies(MediaShape shape, List<ShapeDefect> defects)
    {
        if (shape.FormatFamilies.Count == 0)
        {
            defects.Add(new ShapeDefect(
                "formatFamilies",
                "A shape declares at least one format family, because a file with no family has no quality ladder.",
                CoreErrorCode.PluginShapeInvalid));
        }

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var family in shape.FormatFamilies)
        {
            foreach (var extension in family.FileExtensions)
            {
                var normalized = extension.StartsWith('.') ? extension[1..] : extension;

                if (seen.TryGetValue(normalized, out var owner))
                {
                    defects.Add(new ShapeDefect(
                        $"formatFamilies[{family.FamilyId}].fileExtensions",
                        $"Extension '{extension}' is claimed by both '{owner}' and '{family.FamilyId}'; the extension sets are the discriminator and must not overlap.",
                        CoreErrorCode.PluginShapeInvalid));
                }
                else
                {
                    seen[normalized] = family.FamilyId;
                }
            }

            if (family.Ladder.Count == 0)
            {
                defects.Add(new ShapeDefect(
                    $"formatFamilies[{family.FamilyId}].ladder",
                    "A format family declares a non-empty quality ladder.",
                    CoreErrorCode.PluginShapeInvalid));
                continue;
            }

            foreach (var group in family.Ladder.GroupBy(tier => tier.Rank).Where(group => group.Count() > 1))
            {
                defects.Add(new ShapeDefect(
                    $"formatFamilies[{family.FamilyId}].ladder",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Rank {group.Key} is used by {group.Count()} tiers; ranks order the ladder and must be distinct."),
                    CoreErrorCode.PluginShapeInvalid));
            }

            // The unknown tier is the sentinel for "not recognized", so a ladder containing it would rank an
            // unrecognized file against recognized ones — the interleaving that made one surveyed
            // application treat any audiobook as an upgrade over any ebook.
            if (family.Ladder.Any(tier => tier == family.Unknown))
            {
                defects.Add(new ShapeDefect(
                    $"formatFamilies[{family.FamilyId}].unknown",
                    "The unknown tier sits outside the ladder rather than in it.",
                    CoreErrorCode.PluginShapeInvalid));
            }
        }

        foreach (var group in shape.FormatFamilies
            .GroupBy(family => family.FamilyId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            defects.Add(new ShapeDefect(
                $"formatFamilies[{group.Key}]",
                $"Format family '{group.Key}' is declared more than once.",
                CoreErrorCode.PluginShapeInvalid));
        }
    }

    // Rule 16.
    private static void CheckFieldsAndSearches(
        MediaShape shape,
        List<ShapeDefect> defects,
        Dictionary<MediaLevelId, MediaLevel> levelsById,
        IReadOnlyList<MediaLevel>? ordered)
    {
        foreach (var level in shape.Levels)
        {
            foreach (var group in level.Fields
                .GroupBy(field => field.FieldId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
            {
                defects.Add(new ShapeDefect(
                    $"levels[{level.Id}].fields[{group.Key}]",
                    $"Field '{group.Key}' is declared more than once on this level.",
                    CoreErrorCode.PluginShapeInvalid));
            }

            var titles = level.Fields.Count(field => field.Semantics.HasFlag(FieldSemantics.Title));

            if (titles != 1)
            {
                defects.Add(new ShapeDefect(
                    $"levels[{level.Id}].fields",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Exactly one field carries the title meaning; {titles} do. Without it a consumer has nothing to call the item."),
                    CoreErrorCode.PluginShapeInvalid));
            }
        }

        foreach (var group in shape.SearchKinds
            .GroupBy(kind => kind.SearchKindId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            defects.Add(new ShapeDefect(
                $"searchKinds[{group.Key}]",
                $"Search '{group.Key}' is declared more than once.",
                CoreErrorCode.PluginShapeInvalid));
        }

        foreach (var search in shape.SearchKinds)
        {
            if (!levelsById.ContainsKey(search.TargetLevelId))
            {
                defects.Add(new ShapeDefect(
                    $"searchKinds[{search.SearchKindId}].targetLevelId",
                    $"Target level '{search.TargetLevelId}' is not a level of this shape.",
                    CoreErrorCode.PluginShapeInvalid));
                continue;
            }

            switch (search.Scope.Kind)
            {
                case AcquisitionScopeKind.SequenceSpan:
                    CheckSequenceSpanScope(shape, defects, search);
                    break;

                case AcquisitionScopeKind.Ancestor:
                    CheckAncestorScope(defects, search, ordered);
                    break;

                case AcquisitionScopeKind.Single:
                default:
                    break;
            }
        }
    }

    private static void CheckSequenceSpanScope(MediaShape shape, List<ShapeDefect> defects, SearchKind search)
    {
        var axisId = search.Scope.SequenceAxisId;

        if (string.IsNullOrWhiteSpace(axisId))
        {
            defects.Add(new ShapeDefect(
                $"searchKinds[{search.SearchKindId}].scope.sequenceAxisId",
                "A sequence-span scope names the axis it spans.",
                CoreErrorCode.PluginShapeInvalid));
            return;
        }

        var known = shape.Levels
            .SelectMany(level => level.SequenceAxes)
            .Any(axis => string.Equals(axis.AxisId, axisId, StringComparison.Ordinal));

        if (!known)
        {
            defects.Add(new ShapeDefect(
                $"searchKinds[{search.SearchKindId}].scope.sequenceAxisId",
                $"Sequence axis '{axisId}' is not declared by any level of this shape.",
                CoreErrorCode.PluginShapeInvalid));
        }
    }

    private static void CheckAncestorScope(
        List<ShapeDefect> defects,
        SearchKind search,
        IReadOnlyList<MediaLevel>? ordered)
    {
        var ancestorId = search.Scope.AncestorLevelId;

        if (ancestorId is not { } ancestor)
        {
            defects.Add(new ShapeDefect(
                $"searchKinds[{search.SearchKindId}].scope.ancestorLevelId",
                "An ancestor scope names the level it broadens to.",
                CoreErrorCode.PluginShapeInvalid));
            return;
        }

        if (ordered is null)
        {
            return;
        }

        var ancestorIndex = IndexOf(ordered, ancestor);
        var targetIndex = IndexOf(ordered, search.TargetLevelId);

        if (ancestorIndex < 0 || targetIndex < 0 || ancestorIndex >= targetIndex)
        {
            defects.Add(new ShapeDefect(
                $"searchKinds[{search.SearchKindId}].scope.ancestorLevelId",
                $"Level '{ancestor}' is a strict ancestor of target level '{search.TargetLevelId}'.",
                CoreErrorCode.PluginShapeInvalid));
        }
    }

    private static void RequireExactlyOne(MediaShape shape, List<ShapeDefect> defects, MediaLevelRoles role)
    {
        var count = shape.Levels.Count(level => level.Roles.HasFlag(role));

        if (count != 1)
        {
            defects.Add(new ShapeDefect(
                "levels",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Exactly one level carries the {role} role; {count} do."),
                CoreErrorCode.PluginShapeInvalid));
        }
    }

    private static void RequireAtLeastOne(MediaShape shape, List<ShapeDefect> defects, MediaLevelRoles role)
    {
        if (!shape.Levels.Any(level => level.Roles.HasFlag(role)))
        {
            defects.Add(new ShapeDefect(
                "levels",
                $"At least one level carries the {role} role.",
                CoreErrorCode.PluginShapeInvalid));
        }
    }

    private static Dictionary<string, CoordinateSpace> SpacesById(MediaShape shape)
    {
        var map = new Dictionary<string, CoordinateSpace>(StringComparer.Ordinal);

        foreach (var space in shape.CoordinateSpaces)
        {
            map[space.SpaceId] = space;
        }

        return map;
    }

    private static int IndexOf(IReadOnlyList<MediaLevel> ordered, MediaLevelId id)
    {
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }
}
