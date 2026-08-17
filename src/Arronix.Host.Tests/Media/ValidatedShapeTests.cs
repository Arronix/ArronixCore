using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Host.Media;
using Arronix.Host.Tests.Support;
using FluentAssertions;

// The shape contracts are experimental; these tests exercise the host's gate over them.
#pragma warning disable ARX0003
#pragma warning disable ARX0013

namespace Arronix.Host.Tests.Media;

/// <summary>
/// The gate's promise: after it, no host lookup can fail.
/// </summary>
[TestFixture]
internal sealed class ValidatedShapeTests
{
    [Test]
    public void AFusedShapeValidatesAndResolvesEveryRoleToTheSameLevel()
    {
        ValidatedShape.TryValidate(ShapeFixtures.Fused(), out var shape, out var defects)
            .Should().BeTrue(string.Join("; ", defects.Select(defect => defect.Message)));

        shape!.LibraryEntry.Id.Should().Be(ShapeFixtures.Catalog);
        shape.AcquisitionUnit.Id.Should().Be(ShapeFixtures.Catalog);
        shape.CompletenessUnit.Id.Should().Be(ShapeFixtures.Catalog);
        shape.FileAnchor.Id.Should().Be(ShapeFixtures.Catalog);
        shape.FileUnit.Id.Should().Be(ShapeFixtures.Catalog);
        shape.VariantLevel.Should().BeNull();
    }

    [Test]
    public void ALayeredShapeOrdersLevelsRootFirst()
    {
        ValidatedShape.TryValidate(ShapeFixtures.Layered(), out var shape, out var defects)
            .Should().BeTrue(string.Join("; ", defects.Select(defect => defect.Message)));

        shape!.Levels.Select(level => level.Id).Should().Equal(
            ShapeFixtures.Catalog,
            ShapeFixtures.Work,
            ShapeFixtures.Variant,
            ShapeFixtures.Part);
    }

    [Test]
    public void TheFileAnchorMaySitAboveTheUnitItSatisfies()
    {
        ValidatedShape.TryValidate(ShapeFixtures.Layered(), out var shape, out _).Should().BeTrue();

        shape!.FileAnchor.Id.Should().Be(ShapeFixtures.Work);
        shape.FileUnit.Id.Should().Be(ShapeFixtures.Part);
    }

    [Test]
    public void PathToWalksFromTheRootDownToTheLevel()
    {
        ValidatedShape.TryValidate(ShapeFixtures.Layered(), out var shape, out _).Should().BeTrue();

        shape!.PathTo(ShapeFixtures.Part).Select(level => level.Id).Should().Equal(
            ShapeFixtures.Catalog,
            ShapeFixtures.Work,
            ShapeFixtures.Variant,
            ShapeFixtures.Part);
    }

    [Test]
    public void LevelOfIsTotalOverEveryIdentifierTheShapeDeclares()
    {
        ValidatedShape.TryValidate(ShapeFixtures.Layered(), out var shape, out _).Should().BeTrue();

        foreach (var level in shape!.Levels)
        {
            shape.LevelOf(level.Id).Should().BeSameAs(level);
        }
    }

    [Test]
    public void AnExtensionIsMatchedToItsFamilyWithOrWithoutALeadingDot()
    {
        ValidatedShape.TryValidate(ShapeFixtures.Layered(), out var shape, out _).Should().BeTrue();

        shape!.FamilyForExtension("mkv")!.FamilyId.Should().Be("primary");
        shape.FamilyForExtension(".MKV")!.FamilyId.Should().Be("primary");
        shape.FamilyForExtension("mp3")!.FamilyId.Should().Be("secondary");
        shape.FamilyForExtension("txt").Should().BeNull();
    }

    [Test]
    public void TheCanonicalSpaceIsResolvedForALevelThatCarriesCoordinates()
    {
        ValidatedShape.TryValidate(ShapeFixtures.Layered(), out var shape, out _).Should().BeTrue();

        shape!.CanonicalSpaceOf(shape.LevelOf(ShapeFixtures.Part))!.SpaceId
            .Should().Be(ShapeFixtures.OrdinalSpaceId);

        shape.CanonicalSpaceOf(shape.LevelOf(ShapeFixtures.Catalog)).Should().BeNull();
    }

    [Test]
    public void RequiringASearchTheShapeDoesNotDeclareFails()
    {
        ValidatedShape.TryValidate(ShapeFixtures.Layered(), out var shape, out _).Should().BeTrue();

        shape!.RequireSearchKind("span").SearchKindId.Should().Be("span");

        var act = () => shape.RequireSearchKind("nothing-like-this");
        act.Should().Throw<ArronixException>();
    }

    [Test]
    public void AFailedValidationProducesNoShapeAtAll()
    {
        var broken = ShapeFixtures.Fused() with { Levels = [] };

        ValidatedShape.TryValidate(broken, out var shape, out var defects).Should().BeFalse();

        shape.Should().BeNull();
        defects.Should().NotBeEmpty();
    }
}
