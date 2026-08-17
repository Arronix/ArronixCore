#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.

using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Movies.Tests.Fixtures;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Shape;

/// <summary>
/// The collection grouping axis: the only cross-cutting structure a movie library has, and the one place
/// this kind exercises the shape's grouping vocabulary.
/// </summary>
/// <remarks>
/// A collection is <b>not</b> a level. It is not the parent of a movie, movies do not live inside it, and a
/// movie that leaves the library does not take it with it. Declaring it as a level would be the obvious
/// mistake and would make every movie's identity depend on whether the catalog happens to file it under a
/// franchise this week.
/// </remarks>
[TestFixture]
public class CollectionAxisTests
{
    private static GroupingAxis Axis { get; } = MoviesDeclaration.Shape.GroupingAxes.Single();

    [Test]
    public void IsDeclaredOnceAndNamesTheMovieLevelAsItsMembers()
        => Assert.Multiple(() =>
        {
            Assert.That(Axis.AxisId, Is.EqualTo("collection"));
            Assert.That(Axis.MemberLevelId, Is.EqualTo(MoviesDeclaration.Level.Id));
            Assert.That(Axis.Name, Is.EqualTo("Collection"));
            Assert.That(Axis.PluralName, Is.EqualTo("Collections"));
        });

    /// <summary>
    /// A movie belongs to at most one collection — and nothing declares that any more. The arity, the
    /// member position and the primary-member flag are all read off the member property being a single,
    /// optional reference, so there is nothing that can contradict the type.
    /// </summary>
    [Test]
    public void DerivesItsArityFromTheMemberPropertyRatherThanDeclaringIt()
        => Assert.Multiple(() =>
        {
            Assert.That(Axis.Arity, Is.EqualTo(GroupingArity.ManyToOne));
            Assert.That(Axis.Position, Is.EqualTo(MemberPosition.None));
            Assert.That(Axis.HasPrimaryMember, Is.False);
            Assert.That(
                Axis.Arity != GroupingArity.ManyToMany && Axis.HasPrimaryMember,
                Is.False,
                "A primary member is meaningful only on a many-to-many grouping axis.");
        });

    /// <summary>
    /// A collection outlives its members. It is fetched and stored on its own key rather than assembled
    /// from whatever the library happens to hold, which is what an independent lifetime means.
    /// </summary>
    [Test]
    public void OutlivesItsMembers()
        => Assert.Multiple(() =>
        {
            Assert.That(Axis.Lifetime, Is.EqualTo(GroupLifetime.Independent));
            Assert.That(Axis.HasOwnMetadata, Is.True);
        });

    [Test]
    public void IsBothMonitorableAndASourceOfItemsToAdd()
        => Assert.Multiple(() =>
        {
            Assert.That(Axis.IsMonitorable, Is.True);
            Assert.That(Axis.IsDiscoverySource, Is.True);
        });

    /// <summary>
    /// The membership is one reference on the member, not a label plus a join key the item carried as data
    /// with no foreign key between them. There is nothing left for two fields to disagree about.
    /// </summary>
    [Test]
    public void CarriesTheMembershipAsOneReferenceOnTheMember()
    {
        var field = MoviesDeclaration.Fields["collection"];

        Assert.Multiple(() =>
        {
            Assert.That(field.ValueKind, Is.EqualTo(FieldValueKind.Reference));
            Assert.That(field.Multivalued, Is.False);
            Assert.That(field.Semantics.HasFlag(FieldSemantics.Groupable), Is.True);
            Assert.That(
                MoviesDeclaration.Fields,
                Does.Not.ContainKey("collectionTmdbId"),
                "The join key the item used to carry as data is the reference itself now.");
        });
    }

    /// <summary>
    /// <b>The defect this fixture used to pin is closed.</b> The axis asserted that a group had metadata of
    /// its own and offered nowhere to describe it, so the field names lived in a constants class that
    /// nothing validated and no client could discover. A group is a type now, so its fields derive by
    /// exactly the rules an item's do — one derivation, not two.
    /// </summary>
    [Test]
    public void DescribesItsOwnMetadataFieldByField()
    {
        var fields = Axis.Fields.ToDictionary(static field => field.FieldId, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(Axis.HasOwnMetadata, Is.True, "It says metadata exists...");
            Assert.That(Axis.Fields, Is.Not.Empty, "...and now it describes it.");

            Assert.That(
                fields["title"].Semantics.HasFlag(FieldSemantics.Title),
                Is.True,
                "A consumer that could not name a collection could not list one.");
            Assert.That(fields["overview"].ValueKind, Is.EqualTo(FieldValueKind.MultilineText));
            Assert.That(fields["images"].Semantics.HasFlag(FieldSemantics.Artwork), Is.True);
            Assert.That(fields["memberCount"].ValueKind, Is.EqualTo(FieldValueKind.Count));
            Assert.That(
                fields["externalIds"].ValueKind,
                Is.EqualTo(FieldValueKind.ExternalIdentifier),
                "A collection identifier is its own key space, and the type system is what says so.");
        });
    }

    /// <summary>
    /// A collection's identifiers and a movie's identifiers are different key spaces and must never be
    /// compared. That used to be smuggled into a scheme name; it is the difference between two properties
    /// on two types now, and neither type names a catalog.
    /// </summary>
    [Test]
    public void NamesNoCatalogOnEitherSideOfTheKeySpaceSplit()
        => Assert.Multiple(() =>
        {
            Assert.That(Axis.ExternalIds, Is.Empty, "Composed by the host from the installed catalogers.");
            Assert.That(
                MoviesDeclaration.Level.Identity.ExternalIds,
                Is.Empty,
                "And the same on the level, for the same reason.");
        });

    /// <summary>
    /// The seeded catalog holds real collections with real members, so the axis is not a declaration with
    /// nothing behind it.
    /// </summary>
    [Test]
    public void ResolvesRealMembersInTheSeededCatalog()
    {
        var catalog = MoviesCatalog.CreateSeeded();

        Assert.That(catalog.Collections, Is.Not.Empty);

        foreach (var collection in catalog.Collections)
        {
            var members = catalog.MembersOf(collection.TmdbId);

            Assert.That(members, Is.Not.Empty, collection.Title);
            Assert.That(
                members.All(member => member.CollectionTmdbId == collection.TmdbId),
                Is.True,
                collection.Title);
        }
    }

    /// <summary>
    /// Membership is optional, and a majority of a real library is outside every collection. A model that
    /// made the group mandatory would have to invent one per film.
    /// </summary>
    [Test]
    public void LeavesMostOfTheLibraryOutsideEveryCollection()
    {
        var catalog = MoviesCatalog.CreateSeeded();
        var ungrouped = catalog.Movies.Count(static movie => movie.CollectionTmdbId is null);

        Assert.That(ungrouped, Is.GreaterThan(0));
    }

    [Test]
    public void ResolvesEveryDeclaredMembershipToAKnownCollection()
    {
        var catalog = MoviesCatalog.CreateSeeded();

        foreach (var movie in catalog.Movies.Where(static movie => movie.CollectionTmdbId is not null))
        {
            Assert.That(
                catalog.TryGetCollection(movie.CollectionTmdbId!.Value, out _),
                Is.True,
                movie.Title);
        }
    }
}
