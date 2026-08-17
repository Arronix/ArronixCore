// The media-shape contracts are experimental; this fixture asserts against them directly.
#pragma warning disable ARX0013
using System;
using System.Collections.Generic;
using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Music.Tests.Shape;

/// <summary>
/// Re-states, against this kind, the rules the host's validation gate will apply at load.
/// </summary>
/// <remarks>
/// Duplicating the rules here is deliberate. The gate lives in a project this one cannot reference, and a
/// reference extension that only discovers its shape is malformed when a host tries to load it is not
/// much of a reference. Every assertion below is one numbered validation rule.
/// </remarks>
[TestFixture]
public class ShapeInvariantTests
{
    private static MediaShape Declaration => MusicShape.Declaration;

    [Test]
    public void LevelIdentifiersAreUniqueAndThereIsExactlyOneRoot()
    {
        var ids = Declaration.Levels.Select(level => level.Id).ToList();

        Assert.That(ids, Is.Unique);
        Assert.That(Declaration.Levels.Count(level => level.Parent is null), Is.EqualTo(1));
    }

    [Test]
    public void TheHierarchyIsALinearChain()
    {
        foreach (var level in Declaration.Levels)
        {
            var children = Declaration.Levels.Count(candidate => candidate.Parent == level.Id);

            Assert.That(children, Is.LessThanOrEqualTo(1), $"Level '{level.Id}' branches.");
        }
    }

    [Test]
    public void EveryParentResolvesAndTheGraphIsAcyclic()
    {
        var byId = Declaration.Levels.ToDictionary(level => level.Id);

        foreach (var level in Declaration.Levels)
        {
            var seen = new HashSet<MediaLevelId> { level.Id };
            var cursor = level;

            while (cursor.Parent is { } parent)
            {
                Assert.That(byId.ContainsKey(parent), Is.True, $"Parent '{parent}' does not resolve.");
                Assert.That(seen.Add(parent), Is.True, $"Level '{level.Id}' is part of a cycle.");
                cursor = byId[parent];
            }
        }
    }

    [Test]
    public void ExactlyOneLevelIsTheLibraryEntry()
    {
        Assert.That(
            Declaration.Levels.Count(level => level.Roles.HasFlag(MediaLevelRoles.LibraryEntry)),
            Is.EqualTo(1));
    }

    [Test]
    public void TheRequiredRolesAreAllPresent()
    {
        Assert.That(
            Declaration.Levels.Any(level => level.Roles.HasFlag(MediaLevelRoles.AcquisitionUnit)),
            Is.True);

        Assert.That(
            Declaration.Levels.Any(level => level.Roles.HasFlag(MediaLevelRoles.CompletenessUnit)),
            Is.True);

        Assert.That(
            Declaration.Levels.Any(level => level.Roles.HasFlag(MediaLevelRoles.FileBearing)),
            Is.True);
    }

    [Test]
    public void EveryReferencedCoordinateSpaceExists()
    {
        var declared = Declaration.CoordinateSpaces.Select(space => space.SpaceId).ToHashSet(StringComparer.Ordinal);

        foreach (var level in Declaration.Levels)
        {
            foreach (var spaceId in level.CoordinateSpaceIds)
            {
                Assert.That(declared, Does.Contain(spaceId));
            }
        }
    }

    [Test]
    public void ALevelWithSpacesHasExactlyOneCanonicalSpace()
    {
        foreach (var level in Declaration.Levels.Where(level => level.CoordinateSpaceIds.Count > 0))
        {
            var canonical = Declaration.CoordinateSpaces
                .Where(space => level.CoordinateSpaceIds.Contains(space.SpaceId) && space.IsCanonical)
                .ToList();

            Assert.That(canonical, Has.Count.EqualTo(1), $"Level '{level.Id}' has no single canonical space.");
        }
    }

    [Test]
    public void EverySequenceAxisNamesARealOrdinalComponentOfItsOwnLevel()
    {
        foreach (var level in Declaration.Levels)
        {
            foreach (var axis in level.SequenceAxes)
            {
                Assert.That(level.CoordinateSpaceIds, Does.Contain(axis.SpaceId));

                var space = Declaration.CoordinateSpaces.Single(
                    candidate => string.Equals(candidate.SpaceId, axis.SpaceId, StringComparison.Ordinal));

                Assert.That(space.Kind, Is.EqualTo(CoordinateKind.Ordinal));
                Assert.That(axis.ComponentIndex, Is.InRange(0, space.Components.Count - 1));
            }
        }
    }

    [Test]
    public void TheCarrierRunCarriesNoPolicyRecord()
    {
        // The contrast with a broadcast kind's equivalent run, which does carry one. Nothing is monitored,
        // named or scheduled per carrier here.
        var axis = Declaration.Levels
            .SelectMany(level => level.SequenceAxes)
            .Single(candidate => string.Equals(candidate.AxisId, MusicShape.CarrierAxisId, StringComparison.Ordinal));

        Assert.That(axis.HasPolicyRecord, Is.False);
        Assert.That(axis.Exceptions, Is.Empty);
    }

    [Test]
    public void FormatFamiliesAreWellFormed()
    {
        var seenExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var family in Declaration.FormatFamilies)
        {
            Assert.That(family.Ladder, Is.Not.Empty);
            Assert.That(family.Ladder.Select(tier => tier.Rank), Is.Unique);
            Assert.That(
                family.Ladder.Any(tier => tier.Rank == family.Unknown!.Rank),
                Is.False,
                "The unknown tier must not sit on the ladder.");

            foreach (var extension in family.FileExtensions)
            {
                Assert.That(seenExtensions.Add(extension), Is.True, $"Extension '{extension}' is claimed twice.");
            }
        }
    }

    [Test]
    public void EveryLevelHasExactlyOneTitleFieldAndUniqueFieldIdentifiers()
    {
        foreach (var level in Declaration.Levels)
        {
            Assert.That(
                level.Fields.Select(field => field.FieldId),
                Is.Unique,
                $"Level '{level.Id}' declares a field identifier twice.");

            Assert.That(
                level.Fields.Count(field => field.Semantics.HasFlag(FieldSemantics.Title)),
                Is.EqualTo(1),
                $"Level '{level.Id}' does not have exactly one title field.");
        }
    }

    [Test]
    public void EveryFacetAndSearchKindResolvesAgainstTheHierarchy()
    {
        var ids = Declaration.Levels.Select(level => level.Id).ToHashSet();

        foreach (var facet in Declaration.SelectionFacets)
        {
            Assert.That(ids, Does.Contain(facet.AppliesToLevelId));
        }

        foreach (var searchKind in Declaration.SearchKinds)
        {
            Assert.That(ids, Does.Contain(searchKind.TargetLevelId));
            Assert.That(searchKind.Categories.Count, Is.GreaterThan(0));

            if (searchKind.Scope.Kind == AcquisitionScopeKind.Ancestor)
            {
                Assert.That(searchKind.Scope.AncestorLevelId, Is.Not.Null);
                Assert.That(ids, Does.Contain(searchKind.Scope.AncestorLevelId!.Value));
                Assert.That(
                    IsAncestor(searchKind.Scope.AncestorLevelId!.Value, searchKind.TargetLevelId),
                    Is.True);
            }

            if (searchKind.Scope.Kind == AcquisitionScopeKind.SequenceSpan)
            {
                Assert.That(
                    Declaration.Levels.SelectMany(level => level.SequenceAxes)
                        .Any(axis => string.Equals(
                            axis.AxisId,
                            searchKind.Scope.SequenceAxisId,
                            StringComparison.Ordinal)),
                    Is.True);
            }
        }
    }

    [Test]
    public void TokensAreDeclaredOnceAndCarryHelp()
    {
        Assert.That(Declaration.Tokens, Is.Not.Empty);
        Assert.That(Declaration.Tokens.Select(token => token.Name), Is.Unique);

        foreach (var token in Declaration.Tokens)
        {
            Assert.That(token.Name, Does.StartWith("{"));
            Assert.That(token.Name, Does.EndWith("}"));
            Assert.That(token.Description, Is.Not.Empty);
            Assert.That(token.ExampleValue, Is.Not.Empty);
        }
    }

    [Test]
    public void TheDeclaredTokensAreExactlyWhatTheNamingPolicyResolves()
    {
        // The cross-check the loader performs between the manifest and the shape, done here between the
        // shape and the implementation so a drift is caught at build time rather than at load time.
        Assert.That(new MusicRenamePolicy().ValidateTemplate(MusicRenamePolicy.MultiCarrierTemplate), Is.True);
        Assert.That(new MusicRenamePolicy().ValidateTemplate(MusicRenamePolicy.SingleCarrierTemplate), Is.True);
        Assert.That(new MusicRenamePolicy().ValidateTemplate("{Not A Token}"), Is.False);
    }

    private static bool IsAncestor(MediaLevelId ancestor, MediaLevelId descendant)
    {
        var byId = Declaration.Levels.ToDictionary(level => level.Id);
        var cursor = byId[descendant];

        while (cursor.Parent is { } parent)
        {
            if (parent == ancestor)
            {
                return true;
            }

            cursor = byId[parent];
        }

        return false;
    }
}
