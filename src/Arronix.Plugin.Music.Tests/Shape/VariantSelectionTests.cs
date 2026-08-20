using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Music.Tests.Shape;

/// <summary>
/// Proves that competing pressings of one work are expressible as a selectable variant, with the
/// import-time switch declared rather than emergent.
/// </summary>
[TestFixture]
public class VariantSelectionTests
{
    private static MediaShape Declaration => MusicShape.Declaration;

    [Test]
    public void ExactlyOneLevelCarriesTheVariantRole()
    {
        var variants = Declaration.Levels
            .Where(level => level.Roles.HasFlag(MediaLevelRoles.VariantAxis))
            .ToList();

        Assert.That(variants, Has.Count.EqualTo(1));
        Assert.That(variants[0].Id, Is.EqualTo(MusicShape.PressingLevel));
    }

    [Test]
    public void TheVariantDeclarationExistsIfAndOnlyIfTheRoleDoes()
    {
        foreach (var level in Declaration.Levels)
        {
            var carriesRole = level.Roles.HasFlag(MediaLevelRoles.VariantAxis);

            Assert.That(
                level.Variant is not null,
                Is.EqualTo(carriesRole),
                $"Level '{level.Id}' declares a variant and a role that disagree.");
        }
    }

    [Test]
    public void TheVariantsParentIsWhatIsAcquired()
    {
        var variant = Declaration.Levels.Single(
            level => level.Roles.HasFlag(MediaLevelRoles.VariantAxis));

        var parent = Declaration.Levels.Single(level => level.Id == variant.Parent!.Value);

        Assert.That(parent.Roles.HasFlag(MediaLevelRoles.AcquisitionUnit), Is.True);
    }

    [Test]
    public void ImportingFilesMaySwitchTheSelection()
    {
        var variant = Declaration.Levels
            .Single(level => level.Roles.HasFlag(MediaLevelRoles.VariantAxis))
            .Variant!;

        Assert.That(variant.Triggers.HasFlag(SelectionTrigger.OnImport), Is.True);
        Assert.That(variant.AutoSwitchByDefault, Is.True);
    }

    [Test]
    public void CompletenessIsCountedAgainstTheSelectedPressing()
    {
        var variant = Declaration.Levels
            .Single(level => level.Roles.HasFlag(MediaLevelRoles.VariantAxis))
            .Variant!;

        Assert.That(variant.CompletenessIsVariantRelative, Is.True);
    }

    [Test]
    public void TheVariantLevelIsCatalogOnly()
    {
        // The selection is library state and belongs on the work above; a pressing itself owns no user
        // state, which is what stops two places disagreeing about which one is chosen.
        var variant = Declaration.Levels.Single(
            level => level.Roles.HasFlag(MediaLevelRoles.VariantAxis));

        Assert.That(variant.Identity.HasCatalogRecord, Is.True);
        Assert.That(variant.Identity.HasLibraryRecord, Is.False);
        Assert.That(variant.MonitorDimensions, Is.Empty);
    }
}
