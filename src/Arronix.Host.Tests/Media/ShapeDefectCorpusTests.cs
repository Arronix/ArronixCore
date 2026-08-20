using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;
using Arronix.Host.Tests.Support;
using FluentAssertions;


namespace Arronix.Host.Tests.Media;

/// <summary>
/// One malformed shape per rule, each asserted to produce the fault it was built to produce.
/// </summary>
/// <remarks>
/// The corpus exists because a validator's real failure mode is silence: a rule that stops firing looks
/// exactly like a codebase with no violations of it. Each case here breaks one thing and states what the
/// gate should say about it.
/// </remarks>
[TestFixture]
internal sealed class ShapeDefectCorpusTests
{
    [Test]
    public void ShapeWithNoLevelsIsRefused()
        => Refuse(ShapeFixtures.Fused() with { Levels = [] }, "at least one level");

    [Test]
    public void DuplicateLevelIdentifierIsRefused()
    {
        var shape = ShapeFixtures.Fused();
        Refuse(shape with { Levels = [shape.Levels[0], shape.Levels[0]] }, "more than once");
    }

    [Test]
    public void TwoRootsAreRefused()
    {
        var layered = ShapeFixtures.Layered();
        Refuse(layered.WithLevel(ShapeFixtures.Work, level => level with { Parent = null }), "exactly one root");
    }

    [Test]
    public void ABranchingHierarchyIsRefused()
    {
        var layered = ShapeFixtures.Layered();
        Refuse(
            layered.WithLevel(ShapeFixtures.Part, level => level with { Parent = ShapeFixtures.Work }),
            "at most one");
    }

    [Test]
    public void NoLibraryEntryLevelIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape.WithLevel(ShapeFixtures.Catalog, level => level with { Roles = MediaLevelRoles.None }),
            "LibraryEntry");
    }

    [Test]
    public void TwoVariantLevelsAreRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape.WithLevel(ShapeFixtures.Part, level => level with
            {
                Roles = level.Roles | MediaLevelRoles.VariantAxis,
                Variant = new VariantSelection(),
            }),
            "At most one level carries the variant role");
    }

    [Test]
    public void AVariantLevelWithoutItsSelectionIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape.WithLevel(ShapeFixtures.Variant, level => level with { Variant = null }),
            "declares its variant selection");
    }

    [Test]
    public void AVariantLevelWhoseParentDoesNotAcquireIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape.WithLevel(ShapeFixtures.Work, level => level with { Roles = MediaLevelRoles.FileBearing })
                .WithLevel(ShapeFixtures.Catalog, level => level with
                {
                    Roles = level.Roles | MediaLevelRoles.AcquisitionUnit,
                }),
            "carries the acquisition role");
    }

    [Test]
    public void AFileBindingNamingALevelThatDoesNotBearFilesIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape with { FileBinding = shape.FileBinding with { AnchorLevelId = ShapeFixtures.Catalog } },
            "carries the file-bearing role");
    }

    [Test]
    public void AUnitAboveItsAnchorIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape with
            {
                FileBinding = shape.FileBinding with
                {
                    AnchorLevelId = ShapeFixtures.Part,
                    UnitLevelId = ShapeFixtures.Work,
                },
            },
            "descendant of it");
    }

    [Test]
    public void AMeaningfulOrdinalOnAOneFilePerUnitBindingIsRefused()
    {
        var shape = ShapeFixtures.Fused();
        Refuse(
            shape with { FileBinding = shape.FileBinding with { OrdinalIsMeaningful = true } },
            "more than one file");
    }

    [Test]
    public void ACoordinateSpaceThatIsNotDeclaredIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape.WithLevel(ShapeFixtures.Part, level => level with { CoordinateSpaceIds = ["nowhere"] }),
            "not declared by this shape");
    }

    [Test]
    public void ALevelWithCoordinatesAndNoCanonicalSpaceIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape with { CoordinateSpaces = [ShapeFixtures.OrdinalSpace() with { IsCanonical = false }] },
            "exactly one canonical space");
    }

    [Test]
    public void ASequenceAxisPointingPastItsSpaceIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape.WithLevel(ShapeFixtures.Part, level => level with
            {
                SequenceAxes = [level.SequenceAxes[0] with { ComponentIndex = 7 }],
            }),
            "outside space");
    }

    [Test]
    public void ASpanConstraintNamingAnUnknownComponentIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape with
            {
                FileBinding = shape.FileBinding with
                {
                    SpanConstraints = [new SpanConstraint(ShapeFixtures.OrdinalSpaceId, "not-a-component", SpanRule.MustNotSpan)],
                },
            },
            "no component");
    }

    [Test]
    public void APrimaryMemberOnAManyToOneGroupingIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape with
            {
                GroupingAxes = [shape.GroupingAxes[0] with { Arity = GroupingArity.ManyToOne }],
            },
            "many-to-many");
    }

    [Test]
    public void OverlappingExtensionSetsAcrossFamiliesAreRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape with
            {
                FormatFamilies =
                [
                    ShapeFixtures.Family("primary", ["mkv"]),
                    ShapeFixtures.Family("secondary", ["mkv"]),
                ],
            },
            "claimed by both");
    }

    [Test]
    public void ALadderWithTwoTiersOfOneRankIsRefused()
    {
        var shape = ShapeFixtures.Fused();
        Refuse(
            shape with
            {
                FormatFamilies =
                [
                    ShapeFixtures.Family("primary", ["mkv"]) with
                    {
                        Ladder = [new QualityTier("A", 1), new QualityTier("B", 1)],
                    },
                ],
            },
            "must be distinct");
    }

    [Test]
    public void AnUnknownTierInsideTheLadderIsRefused()
    {
        var shape = ShapeFixtures.Fused();
        var unknown = new QualityTier("Unknown", 0);

        Refuse(
            shape with
            {
                FormatFamilies =
                [
                    ShapeFixtures.Family("primary", ["mkv"]) with
                    {
                        Ladder = [unknown, new QualityTier("High", 2)],
                        Unknown = unknown,
                    },
                ],
            },
            "outside the ladder");
    }

    [Test]
    public void ALevelWithNoTitleFieldIsRefused()
    {
        var shape = ShapeFixtures.Fused();
        Refuse(shape.WithLevel(ShapeFixtures.Catalog, level => level with { Fields = [] }), "title meaning");
    }

    [Test]
    public void ASequenceSpanSearchNamingAnUnknownAxisIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape with
            {
                SearchKinds =
                [
                    .. shape.SearchKinds.Select(search => search.SearchKindId == "span"
                        ? search with
                        {
                            Scope = new AcquisitionScope
                            {
                                Kind = AcquisitionScopeKind.SequenceSpan,
                                SequenceAxisId = "not-an-axis",
                            },
                        }
                        : search),
                ],
            },
            "not declared by any level");
    }

    [Test]
    public void AnAncestorSearchNamingADescendantIsRefused()
    {
        var shape = ShapeFixtures.Layered();
        Refuse(
            shape with
            {
                SearchKinds =
                [
                    .. shape.SearchKinds.Select(search => search.SearchKindId == "ancestor"
                        ? search with
                        {
                            Scope = new AcquisitionScope
                            {
                                Kind = AcquisitionScopeKind.Ancestor,
                                AncestorLevelId = ShapeFixtures.Part,
                            },
                        }
                        : search),
                ],
            },
            "strict ancestor");
    }

    [Test]
    public void EveryDefectCarriesTheLoadFailureCodeAndAPath()
    {
        ValidatedShape.TryValidate(ShapeFixtures.Fused() with { Levels = [] }, out _, out var defects)
            .Should().BeFalse();

        defects.Should().OnlyContain(defect =>
            defect.Code == CoreErrorCode.PluginShapeInvalid && defect.Path.Length > 0);
    }

    private static void Refuse(MediaShape shape, string expected)
    {
        ValidatedShape.TryValidate(shape, out var validated, out var defects).Should().BeFalse();

        validated.Should().BeNull();
        defects.Should().Contain(defect => defect.Message.Contains(expected, StringComparison.Ordinal));
    }
}
