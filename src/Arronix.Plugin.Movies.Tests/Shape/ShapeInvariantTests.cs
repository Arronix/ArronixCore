#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.
#pragma warning disable ARX0021 // Quality contracts are experimental; these tests exercise the axes model.

using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Shape;

/// <summary>
/// Re-states, against this kind, the rules the host's shape gate applies at load.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion below is one of the gate's numbered rules, restated against the structure the host
/// <i>derives</i> from the item type. That is the interesting change: none of these values is written down
/// anywhere any more — the level, its roles, the singleton space, the file binding's four flags and the
/// monitoring dimension are all produced without the kind saying anything, because a kind whose items are
/// their own acquisition units has nothing to say about any of them.
/// </para>
/// </remarks>
[TestFixture]
public class ShapeInvariantTests
{
    /// <summary>The coordinate space a kind whose items address themselves derives.</summary>
    private const string SingletonSpaceId = "singleton";

    /// <summary>The title-driven search: what a source without identifier support can be asked.</summary>
    private const string TitleSearchKindId = "movie";

    /// <summary>The identifier-driven search a source must opt into supporting.</summary>
    private const string IdentifierSearchKindId = "movie-id";

    private static MediaShape Declaration => MoviesDeclaration.Shape;

    [Test]
    public void DeclaresAtLeastOneLevel() => Assert.That(Declaration.Levels, Is.Not.Empty);

    [Test]
    public void DeclaresUniqueLevelIdentifiers()
        => Assert.That(Declaration.Levels.Select(static level => level.Id), Is.Unique);

    [Test]
    public void DeclaresExactlyOneRootLevel()
        => Assert.That(Declaration.Levels.Count(static level => level.Parent is null), Is.EqualTo(1));

    /// <summary>
    /// A movie is the degenerate hierarchy: one level, no parent, no children. It is the case that proves
    /// the level model does not require depth.
    /// </summary>
    [Test]
    public void DeclaresExactlyOneLevelAndNothingBelowIt()
        => Assert.Multiple(() =>
        {
            Assert.That(Declaration.Levels, Has.Count.EqualTo(1));
            Assert.That(Declaration.Levels[0].Id, Is.EqualTo(MoviesDeclaration.Level.Id));
            Assert.That(Declaration.Levels[0].Parent, Is.Null);
        });

    /// <summary>
    /// All five roles on one level. A closed position enum could not say this, and a movie is the case that
    /// proves the roles have to be flags.
    /// </summary>
    [Test]
    public void CarriesEveryRoleOnTheSingleLevelExceptTheVariantAxis()
    {
        var roles = Declaration.Levels[0].Roles;

        Assert.Multiple(() =>
        {
            Assert.That(roles.HasFlag(MediaLevelRoles.LibraryEntry), Is.True);
            Assert.That(roles.HasFlag(MediaLevelRoles.AcquisitionUnit), Is.True);
            Assert.That(roles.HasFlag(MediaLevelRoles.CompletenessUnit), Is.True);
            Assert.That(roles.HasFlag(MediaLevelRoles.FileBearing), Is.True);
            Assert.That(
                roles.HasFlag(MediaLevelRoles.VariantAxis),
                Is.False,
                "An edition is a marker on a file, not a sibling item the library picks between.");
        });
    }

    [TestCase(MediaLevelRoles.LibraryEntry)]
    [TestCase(MediaLevelRoles.AcquisitionUnit)]
    [TestCase(MediaLevelRoles.CompletenessUnit)]
    public void CarriesExactlyOneLevelForEachSingularRole(MediaLevelRoles role)
        => Assert.That(
            Declaration.Levels.Count(level => level.Roles.HasFlag(role)),
            Is.EqualTo(1),
            $"Exactly one level carries the {role} role.");

    [Test]
    public void CarriesAtLeastOneFileBearingLevel()
        => Assert.That(
            Declaration.Levels.Any(static level => level.Roles.HasFlag(MediaLevelRoles.FileBearing)),
            Is.True);

    [Test]
    public void DeclaresNoVariantLevelAndNoVariantSelection()
        => Assert.Multiple(() =>
        {
            Assert.That(
                Declaration.Levels.Count(static level => level.Roles.HasFlag(MediaLevelRoles.VariantAxis)),
                Is.Zero);
            Assert.That(Declaration.Levels.All(static level => level.Variant is null), Is.True);
        });

    [Test]
    public void DeclaresExactlyOneTitleField()
    {
        var titles = Declaration.Levels[0].Fields
            .Count(static field => field.Semantics.HasFlag(FieldSemantics.Title));

        Assert.That(titles, Is.EqualTo(1), "Without a title a consumer has nothing to call the item.");
    }

    [Test]
    public void DeclaresUniqueFieldIdentifiers()
        => Assert.That(Declaration.Levels[0].Fields.Select(static field => field.FieldId), Is.Unique);

    [Test]
    public void DeclaresEveryFieldOnTheOnlyLevelThereIs()
        => Assert.That(Declaration.Levels[0].Fields, Is.Not.Empty);

    [Test]
    public void DeclaresUniqueCoordinateSpaces()
        => Assert.That(Declaration.CoordinateSpaces.Select(static space => space.SpaceId), Is.Unique);

    /// <summary>
    /// One coordinate space, and it is a singleton. Everything downstream of the coordinate model is free
    /// for this kind precisely because of this line.
    /// </summary>
    [Test]
    public void DeclaresASingleCanonicalSingletonSpace()
    {
        Assert.That(Declaration.CoordinateSpaces, Has.Count.EqualTo(1));

        var space = Declaration.CoordinateSpaces[0];

        Assert.Multiple(() =>
        {
            Assert.That(space.SpaceId, Is.EqualTo(SingletonSpaceId));
            Assert.That(space.Kind, Is.EqualTo(CoordinateKind.Singleton));
            Assert.That(space.IsCanonical, Is.True);
            Assert.That(space.IsDense, Is.True);
        });
    }

    [Test]
    public void ReferencesExactlyOneCanonicalSpaceFromTheLevel()
    {
        var level = Declaration.Levels[0];
        var canonical = level.CoordinateSpaceIds
            .Select(id => Declaration.CoordinateSpaces.Single(space => space.SpaceId == id))
            .Count(static space => space.IsCanonical);

        Assert.That(canonical, Is.EqualTo(1));
    }

    [Test]
    public void DeclaresNoSequenceAxis()
        => Assert.That(
            Declaration.Levels[0].SequenceAxes,
            Is.Empty,
            "A movie has no ordinal to span, which is why a season-pack scope is not expressible here "
            + "and does not need to be.");

    [Test]
    public void DeclaresAtLeastOneFormatFamily()
        => Assert.That(Declaration.FormatFamilies, Is.Not.Empty);

    /// <summary>
    /// A family says how its files are read exactly once. Declaring both a ladder and an axis model would
    /// be two answers to one question; declaring neither leaves nothing able to say what one of its files
    /// is.
    /// </summary>
    [Test]
    public void DeclaresAQualityModelInsteadOfALadder()
    {
        foreach (var family in Declaration.FormatFamilies)
        {
            Assert.That(family.Quality, Is.Not.Null, family.FamilyId);
            Assert.That(family.Ladder, Is.Empty, family.FamilyId);
        }
    }

    /// <summary>
    /// A sentinel rung exists only because a ladder has nowhere else to put "we do not know". An axis
    /// reading carries its own typed absence and the policy — not the data — decides what an absent
    /// reading is worth, so there is no sentinel to place anywhere.
    /// </summary>
    [Test]
    public void DeclaresNoUnknownSentinelAtAll()
    {
        foreach (var family in Declaration.FormatFamilies)
        {
            Assert.That(family.Unknown, Is.Null, family.FamilyId);
        }
    }

    [Test]
    public void DeclaresNoOverlappingFileExtensionsBetweenFamilies()
    {
        var claimed = Declaration.FormatFamilies
            .SelectMany(static family => family.FileExtensions)
            .ToList();

        Assert.That(claimed, Is.Unique, "The extension sets are the discriminator.");
    }

    [Test]
    public void DeclaresUniqueSearchKinds()
        => Assert.That(Declaration.SearchKinds.Select(static kind => kind.SearchKindId), Is.Unique);

    [Test]
    public void TargetsADeclaredLevelFromEverySearchKind()
    {
        var levels = Declaration.Levels.Select(static level => level.Id).ToHashSet();

        foreach (var kind in Declaration.SearchKinds)
        {
            Assert.That(levels, Does.Contain(kind.TargetLevelId), kind.SearchKindId);
        }
    }

    /// <summary>
    /// Both search kinds are single-item scopes. A sequence-span scope would have to name an axis and an
    /// ancestor scope a level, and this shape has neither to name.
    /// </summary>
    [Test]
    public void ScopesEverySearchToASingleItem()
    {
        foreach (var kind in Declaration.SearchKinds)
        {
            Assert.That(kind.Scope.Kind, Is.EqualTo(AcquisitionScopeKind.Single), kind.SearchKindId);
            Assert.That(kind.Scope.SequenceAxisId, Is.Null);
            Assert.That(kind.Scope.AncestorLevelId, Is.Null);
        }
    }

    [Test]
    public void DeclaresBothATitleSearchAndAnIdentifierSearch()
        => Assert.That(
            Declaration.SearchKinds.Select(static kind => kind.SearchKindId),
            Is.EquivalentTo(new[] { TitleSearchKindId, IdentifierSearchKindId }));

    [Test]
    public void AppliesEverySelectionFacetToADeclaredLevel()
    {
        var levels = Declaration.Levels.Select(static level => level.Id).ToHashSet();

        foreach (var facet in Declaration.SelectionFacets)
        {
            Assert.That(levels, Does.Contain(facet.AppliesToLevelId), facet.FacetId);
        }
    }

    [Test]
    public void DeclaresUniqueGroupingAxes()
        => Assert.That(Declaration.GroupingAxes.Select(static axis => axis.AxisId), Is.Unique);

    [Test]
    public void NamesADeclaredMemberLevelFromEveryGroupingAxis()
    {
        var levels = Declaration.Levels.Select(static level => level.Id).ToHashSet();

        foreach (var axis in Declaration.GroupingAxes)
        {
            Assert.That(levels, Does.Contain(axis.MemberLevelId), axis.AxisId);
        }
    }

    [Test]
    public void NamesItselfInBothTheSingularAndThePluralForm()
        => Assert.Multiple(() =>
        {
            Assert.That(Declaration.Name, Is.EqualTo("Movie"));
            Assert.That(Declaration.PluralName, Is.EqualTo("Movies"));
            Assert.That(Declaration.Kind, Is.EqualTo(Movies.Kind));
        });

    /// <summary>
    /// The structure is derived once and held, not rebuilt per read. A shape recomputed on every request
    /// would make every downstream reference comparison meaningless and would re-reflect over the entity
    /// each time a consumer asked what a movie is.
    /// </summary>
    [Test]
    public void HoldsOneDerivedStructureRatherThanRebuildingIt()
        => Assert.That(MoviesDeclaration.Model.Shape, Is.SameAs(MoviesDeclaration.Model.Shape));

    /// <summary>
    /// The level identifier, the coordinate space and both search kinds are the identifiers every other
    /// section cross-references. Under the string surface they were constants a use site could mistype;
    /// they are derived from the entity's own name now, so this is the one place that states them.
    /// </summary>
    [Test]
    public void DerivesItsIdentifiersFromTheEntityRatherThanFromConstants()
        => Assert.Multiple(() =>
        {
            Assert.That(Declaration.Levels[0].Id.Value, Is.EqualTo("movie"));
            Assert.That(Declaration.CoordinateSpaces[0].SpaceId, Is.EqualTo(SingletonSpaceId));
            Assert.That(Declaration.GroupingAxes[0].AxisId, Is.EqualTo("collection"));
        });
}
