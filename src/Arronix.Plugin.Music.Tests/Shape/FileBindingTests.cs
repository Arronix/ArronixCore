using System.Collections.Generic;
using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Music.Tests.Shape;

/// <summary>
/// Proves the property this kind exists to prove: the level a release is acquired at is not the level a
/// file satisfies.
/// </summary>
/// <remarks>
/// If any of these fail the shape model has lost the ability to express recorded music, and the correct
/// response is to say so rather than to move a role.
/// </remarks>
[TestFixture]
public class FileBindingTests
{
    private static MediaShape Declaration => MusicShape.Declaration;

    private static MediaLevel LevelOf(MediaLevelId id) =>
        Declaration.Levels.Single(level => level.Id == id);

    [Test]
    public void AnchorLevelIsTheAcquisitionUnit()
    {
        var anchor = LevelOf(Declaration.FileBinding.AnchorLevelId);

        Assert.That(anchor.Roles.HasFlag(MediaLevelRoles.AcquisitionUnit), Is.True);
        Assert.That(anchor.Id, Is.EqualTo(MusicShape.WorkLevel));
    }

    [Test]
    public void UnitLevelIsTheCompletenessUnit()
    {
        var unit = LevelOf(Declaration.FileBinding.UnitLevelId);

        Assert.That(unit.Roles.HasFlag(MediaLevelRoles.CompletenessUnit), Is.True);
        Assert.That(unit.Id, Is.EqualTo(MusicShape.RecordingLevel));
    }

    [Test]
    public void AnchorAndUnitAreDifferentLevels()
    {
        Assert.That(
            Declaration.FileBinding.AnchorLevelId,
            Is.Not.EqualTo(Declaration.FileBinding.UnitLevelId),
            "A single file-bearing level cannot express this kind.");
    }

    [Test]
    public void TheAcquisitionUnitDoesNotCountTowardsCompleteness()
    {
        var anchor = LevelOf(Declaration.FileBinding.AnchorLevelId);

        Assert.That(anchor.Roles.HasFlag(MediaLevelRoles.CompletenessUnit), Is.False);
    }

    [Test]
    public void TheCompletenessUnitIsNotWhatIsAcquired()
    {
        var unit = LevelOf(Declaration.FileBinding.UnitLevelId);

        Assert.That(unit.Roles.HasFlag(MediaLevelRoles.AcquisitionUnit), Is.False);
    }

    [Test]
    public void BothBindingLevelsCarryFileBearing()
    {
        Assert.That(
            LevelOf(Declaration.FileBinding.AnchorLevelId).Roles.HasFlag(MediaLevelRoles.FileBearing),
            Is.True);

        Assert.That(
            LevelOf(Declaration.FileBinding.UnitLevelId).Roles.HasFlag(MediaLevelRoles.FileBearing),
            Is.True);
    }

    [Test]
    public void TheUnitLevelIsADescendantOfTheAnchorLevel()
    {
        var ancestry = new List<MediaLevelId>();
        var cursor = LevelOf(Declaration.FileBinding.UnitLevelId);

        while (cursor.Parent is { } parent)
        {
            ancestry.Add(parent);
            cursor = LevelOf(parent);
        }

        Assert.That(ancestry, Does.Contain(Declaration.FileBinding.AnchorLevelId));
    }

    [Test]
    public void TheVariantLevelSitsBetweenTheAnchorAndTheUnit()
    {
        var unit = LevelOf(Declaration.FileBinding.UnitLevelId);
        var parent = LevelOf(unit.Parent!.Value);

        Assert.That(parent.Roles.HasFlag(MediaLevelRoles.VariantAxis), Is.True);
        Assert.That(parent.Parent, Is.EqualTo(Declaration.FileBinding.AnchorLevelId));
    }

    [Test]
    public void CardinalityIsOneFilePerUnitAndSeveralUnitsPerFile()
    {
        // One file may satisfy several recordings - a single-file rip of a whole pressing - but no
        // recording is satisfied by two files at once.
        Assert.That(Declaration.FileBinding.AtMostOneFilePerUnit, Is.True);
        Assert.That(Declaration.FileBinding.AtMostOneUnitPerFile, Is.False);
    }

    [Test]
    public void TheLinkOrdinalCarriesNoMeaning()
    {
        // The ordinal only means something when one item spans several files, which never happens here.
        Assert.That(Declaration.FileBinding.OrdinalIsMeaningful, Is.False);
        Assert.That(
            Declaration.FileBinding.OrdinalIsMeaningful && Declaration.FileBinding.AtMostOneFilePerUnit,
            Is.False,
            "An ordinal on a one-file-per-item binding would be uninterpretable.");
    }

    [Test]
    public void AFileAnchorNamesTheWorkAndNeverThePressing()
    {
        // The reason ownership survives a change of selection: nothing about the anchor mentions which
        // pressing was chosen.
        var anchor = LevelOf(Declaration.FileBinding.AnchorLevelId);

        Assert.That(anchor.Roles.HasFlag(MediaLevelRoles.VariantAxis), Is.False);
    }
}
