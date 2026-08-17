// Shape and intent contracts are experimental; this fixture asserts against them directly.
#pragma warning disable ARX0013, ARX0016
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Music.Tests.Catalog;

/// <summary>
/// Takes a selection of one pressing all the way round - shape, projection, value, and back - and shows
/// what changes and what does not when the selection moves.
/// </summary>
/// <remarks>
/// The library half of the round trip is deliberately simulated with a local variable rather than a store.
/// There is no persistence in this milestone, and the point being proved is about the <em>shape</em>: that
/// a selection is expressible as a reference at the variant level, that it changes the denominator of
/// completeness, and that it leaves file ownership untouched.
/// </remarks>
[TestFixture]
public class EditionSelectionRoundTripTests
{
    private const int WorkWithTwoPressings = 101;
    private const int OriginalPressing = 201;
    private const int ExpandedPressing = 202;

    private readonly MusicItemSource _source = new();

    [Test]
    public async Task AWorkProjectsSeveralCompetingPressings()
    {
        var pressings = await PressingsOfAsync(WorkWithTwoPressings);

        Assert.That(pressings, Has.Count.GreaterThan(1));
        Assert.That(
            pressings.Select(view => view.Ref.Id.Value),
            Is.EquivalentTo(new[] { OriginalPressing, ExpandedPressing }));
    }

    [Test]
    public async Task EveryPressingIsAChildOfTheWorkAndSitsAtTheVariantLevel()
    {
        foreach (var pressing in await PressingsOfAsync(WorkWithTwoPressings))
        {
            Assert.That(pressing.Ref.Level, Is.EqualTo(MusicShape.PressingLevel));
            Assert.That(pressing.Parent, Is.Not.Null);
            Assert.That(pressing.Parent!.Value.Level, Is.EqualTo(MusicShape.WorkLevel));
            Assert.That(pressing.Parent!.Value.Id.Value, Is.EqualTo(WorkWithTwoPressings));
        }
    }

    [Test]
    public void ASelectionRoundTripsThroughAFieldValueUnchanged()
    {
        var selected = Reference(MusicShape.PressingLevel, ExpandedPressing);

        // The wire form of a selection: a tagged value carrying a reference, which is what an action
        // parameter, a working-surface cell and a stored library facet all use.
        var carried = FieldValue.OfReference(selected);

        Assert.That(carried.Kind, Is.EqualTo(FieldValueKind.Reference));
        Assert.That(carried.Reference, Is.Not.Null);
        Assert.That(carried.Reference!.Value, Is.EqualTo(selected));
        Assert.That(carried.Reference!.Value.Level, Is.EqualTo(MusicShape.PressingLevel));
    }

    [Test]
    public async Task TheTwoPressingsHaveDifferentRunningOrders()
    {
        var original = MusicItemSource.RunningOrderOf(Reference(MusicShape.PressingLevel, OriginalPressing));
        var expanded = MusicItemSource.RunningOrderOf(Reference(MusicShape.PressingLevel, ExpandedPressing));

        Assert.That(original, Is.Not.Empty);
        Assert.That(expanded.Count, Is.GreaterThan(original.Count));

        // And every recording really does hang off its own pressing, so the two sets are disjoint.
        Assert.That(original.Intersect(expanded), Is.Empty);

        var projected = await ChildrenOfAsync(
            MusicShape.RecordingLevel,
            Reference(MusicShape.PressingLevel, ExpandedPressing));

        Assert.That(projected.Count, Is.EqualTo(expanded.Count));
    }

    [Test]
    public void SwitchingTheSelectionChangesTheDenominatorOfCompleteness()
    {
        // Eleven files held. Under the original pressing that is eleven of twelve; under the expanded one
        // the very same eleven files are eleven of seventeen. Both statements are true, which is exactly
        // what variant-relative completeness means.
        const int filesHeld = 11;

        var selected = Reference(MusicShape.PressingLevel, OriginalPressing);
        var wanted = MusicItemSource.RunningOrderOf(selected).Count;

        Assert.That(filesHeld, Is.LessThan(wanted));

        selected = Reference(MusicShape.PressingLevel, ExpandedPressing);
        var wantedAfter = MusicItemSource.RunningOrderOf(selected).Count;

        Assert.That(wantedAfter, Is.GreaterThan(wanted));
        Assert.That(
            MusicShape.Declaration.Levels
                .Single(level => level.Roles.HasFlag(MediaLevelRoles.VariantAxis))
                .Variant!.CompletenessIsVariantRelative,
            Is.True);
    }

    [Test]
    public void FileOwnershipSurvivesASwitchOfSelection()
    {
        // The anchor names the work. Nothing in it mentions a pressing, so re-selecting cannot orphan a
        // file - which is the whole reason the anchor and the unit are different levels.
        var anchorBefore = Reference(MusicShape.WorkLevel, WorkWithTwoPressings);
        var anchorAfter = Reference(MusicShape.WorkLevel, WorkWithTwoPressings);

        Assert.That(anchorAfter, Is.EqualTo(anchorBefore));
        Assert.That(anchorBefore.Level, Is.EqualTo(MusicShape.Declaration.FileBinding.AnchorLevelId));
        Assert.That(anchorBefore.Level, Is.Not.EqualTo(MusicShape.PressingLevel));
    }

    [Test]
    public async Task APressingResolvesFromItsExternalIdentifierAndBack()
    {
        var view = (await PressingsOfAsync(WorkWithTwoPressings))
            .Single(candidate => candidate.Ref.Id.Value == ExpandedPressing);

        var externalId = view.ExternalIds.Single();
        var resolved = await _source.ResolveExternalAsync(externalId);

        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Value, Is.EqualTo(view.Ref));

        var fetched = await _source.GetAsync(resolved.Value);

        Assert.That(fetched, Is.Not.Null);
        Assert.That(fetched!.Ref, Is.EqualTo(view.Ref));
        Assert.That(fetched.ExternalIds.Single(), Is.EqualTo(externalId));
    }

    [Test]
    public async Task ARecordingKnowsItsPositionOnItsCarrier()
    {
        var recordings = await ChildrenOfAsync(
            MusicShape.RecordingLevel,
            Reference(MusicShape.PressingLevel, ExpandedPressing));

        var onSecondCarrier = recordings
            .Where(view => view.Coordinates.TryGet(MusicShape.CarrierPositionSpaceId, out var reading)
                && reading.Value.Ordinals[0] == 2)
            .ToList();

        Assert.That(onSecondCarrier, Is.Not.Empty, "The expanded pressing spans two carriers.");

        foreach (var recording in onSecondCarrier)
        {
            Assert.That(
                recording.Coordinates.TryGet(MusicShape.CarrierPositionSpaceId, out var reading),
                Is.True);

            Assert.That(reading.Value.Kind, Is.EqualTo(CoordinateKind.Ordinal));
            Assert.That(reading.Value.Ordinals.Length, Is.EqualTo(2));
        }
    }

    private static MediaItemRef Reference(MediaLevelId level, int id) =>
        new(MusicShape.Kind, level, MediaItemId.FromInt64(id));

    private async Task<List<ItemView>> PressingsOfAsync(int workId) =>
        await ChildrenOfAsync(MusicShape.PressingLevel, Reference(MusicShape.WorkLevel, workId));

    private async Task<List<ItemView>> ChildrenOfAsync(MediaLevelId level, MediaItemRef parent)
    {
        var page = await _source.QueryAsync(new ItemQuery
        {
            Kind = MusicShape.Kind,
            Level = level,
            Parent = parent,
            PageSize = 200,
        });

        return [.. page.Items];
    }
}
