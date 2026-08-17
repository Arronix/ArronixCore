#pragma warning disable ARX0013 // Shape contracts are experimental; these tests exercise the typed surface.
#pragma warning disable ARX0020 // Media contracts are experimental; these tests exercise the typed surface.

using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Movies.Tests.Fixtures;
using Arronix.Plugin.Movies.Tests.Support;

namespace Arronix.Plugin.Movies.Tests.Shape;

/// <summary>
/// Reading a typed movie through the descriptor-shaped view a consumer that cannot load this assembly gets.
/// </summary>
/// <remarks>
/// This is the assertion the typed model made possible and the string surface could not host: there was no
/// item type to project. It is also what keeps the typed model honest for everybody who is not a .NET
/// client — a command line reads the derived structure and asks for values by field identifier, and has to
/// get exactly what the descriptor said it would.
/// </remarks>
[TestFixture]
public class ItemProjectionTests
{
    private static MoviesCatalog Catalog { get; } = MoviesCatalog.CreateSeeded();

    private static Movie Godfather { get; } = MoviesSeedProjection.ToMovie(
        Catalog.Movies.Single(static movie => movie.TmdbId == 238),
        Catalog.Collections.Single(static collection => collection.Title.StartsWith("The Godfather", StringComparison.Ordinal)));

    [Test]
    public void ProjectsEverySeededMovieWithoutLosingAField()
    {
        foreach (var record in Catalog.Movies)
        {
            var collection = record.CollectionTmdbId is { } id
                && Catalog.TryGetCollection(id, out var group)
                ? group
                : null;

            var view = MoviesDeclaration.Model.Project(MoviesSeedProjection.ToMovie(record, collection));

            Assert.That(view.Title, Is.EqualTo(record.Title));
            Assert.That(
                view.Fields.Keys,
                Is.EquivalentTo(MoviesDeclaration.Fields.Keys),
                "A view carries a value for every declared field, present or absent.");
        }
    }

    [Test]
    public void AddressesTheItemByTheKindAndTheDerivedLevel()
    {
        var view = MoviesDeclaration.Model.Project(Godfather);

        Assert.Multiple(() =>
        {
            Assert.That(view.Ref.Kind, Is.EqualTo(Movies.Kind));
            Assert.That(view.Ref.Level, Is.EqualTo(MoviesDeclaration.Level.Id));
            Assert.That(view.Ref.Id.Value, Is.EqualTo(Godfather.Key.Value));
        });
    }

    /// <summary>
    /// The identifier set crosses as identifiers rather than as two vendor-named scalar fields, so a
    /// consumer reads "the catalogs this movie is known to" without knowing any catalog's name.
    /// </summary>
    [Test]
    public void CarriesTheIdentifierSetRatherThanOneFieldPerCatalog()
    {
        var view = MoviesDeclaration.Model.Project(Godfather);

        Assert.Multiple(() =>
        {
            Assert.That(view.ExternalIds.Select(static id => id.Scheme), Is.EquivalentTo(new[] { "tmdb", "imdb" }));
            Assert.That(MoviesDeclaration.Fields, Does.Not.ContainKey("tmdbId"));
            Assert.That(MoviesDeclaration.Fields, Does.Not.ContainKey("imdbId"));
        });
    }

    [Test]
    public void ReadsOneFieldWithoutProjectingTheWholeItem()
        => Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Model.Read(Godfather, "title").Text, Is.EqualTo("The Godfather"));
            Assert.That(MoviesDeclaration.Model.Read(Godfather, "year").Number, Is.EqualTo(1972));
            Assert.That(
                MoviesDeclaration.Model.Read(Godfather, "status").Text,
                Is.EqualTo("released"),
                "An enumerated value crosses as the identifier the derived choices are keyed by.");
        });

    [Test]
    public void RefusesAFieldTheStructureDoesNotDeclare()
        => Assert.Throws<ArgumentException>(() => MoviesDeclaration.Model.Read(Godfather, "sortTitle"));

    /// <summary>
    /// A repeated composite crosses as a list of composites with named components, which is the whole of
    /// what the three position-correlated lists could not say. A consumer no longer has to be told out of
    /// band that index <c>i</c> of one field belongs with index <c>i</c> of the others.
    /// </summary>
    [Test]
    public void CarriesARepeatedCompositeWithItsCorrelationIntact()
    {
        var descriptor = MoviesDeclaration.Fields["ratings"];
        var value = MoviesDeclaration.Model.Read(Godfather, "ratings");

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.ValueKind, Is.EqualTo(FieldValueKind.Composite));
            Assert.That(descriptor.Multivalued, Is.True);
            Assert.That(
                descriptor.Components.Select(static component => component.FieldId),
                Is.EqualTo(new[] { "source", "value", "scale", "voice", "votes" }));
            Assert.That(value.Items, Is.Not.Null.And.Not.Empty);
        });
    }

    /// <summary>
    /// The collection crosses as a reference carrying both a handle and the group's own title. The handle's
    /// level slot holds the <i>axis</i> identifier, because a group is deliberately not a level and a
    /// reference has nowhere else to put it — worth an owner ruling rather than a silent convention.
    /// </summary>
    [Test]
    public void AddressesAGroupByItsAxisBecauseAGroupIsNotALevel()
    {
        var value = MoviesDeclaration.Model.Read(Godfather, "collection");

        Assert.Multiple(() =>
        {
            Assert.That(value.Kind, Is.EqualTo(FieldValueKind.Reference));
            Assert.That(value.Reference!.Value.Level.Value, Is.EqualTo("collection"));
            Assert.That(value.Text, Is.EqualTo("The Godfather Collection"));
        });
    }

    /// <summary>
    /// Artwork crosses as one set of addresses rather than four fields differing only by a role name.
    /// </summary>
    [Test]
    public void CarriesArtworkAsOneSetRatherThanOneFieldPerRole()
    {
        var value = MoviesDeclaration.Model.Read(Godfather, "images");

        Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Fields["images"].ValueKind, Is.EqualTo(FieldValueKind.Artwork));
            Assert.That(value.Items, Is.Not.Null.And.Not.Empty);

            foreach (var role in new[] { "poster", "fanart", "banner", "clearLogo" })
            {
                Assert.That(MoviesDeclaration.Fields, Does.Not.ContainKey(role));
            }
        });
    }

    /// <summary>
    /// The two recomputations the kind binds agree with what the seeded catalog says, which is the only
    /// evidence available that the code replacing two string grammars replaced them faithfully.
    /// </summary>
    [Test]
    public void RecomputesTheReleaseDateTheCatalogWouldHaveComputed()
    {
        foreach (var record in Catalog.Movies.Where(static movie => movie.DigitalRelease is not null
            || movie.PhysicalRelease is not null))
        {
            var movie = MoviesSeedProjection.ToMovie(record);

            Assert.That(Movies.ReleaseDateOf(movie), Is.EqualTo(record.ReleaseDate), record.Title);
        }
    }

    [TestCase(2026, 8, 17, MovieStatus.Released)]
    public void RecomputesTheStatusFromTheThreeDates(int year, int month, int day, MovieStatus expected)
    {
        var movie = MoviesSeedProjection.ToMovie(
            Catalog.Movies.Single(static candidate => candidate.TmdbId == 238));

        Assert.That(Movies.StatusOf(movie, new DateOnly(year, month, day)), Is.EqualTo(expected));
    }

    /// <summary>
    /// A film with a cinema date, no home date and more than the theatrical window behind it counts as
    /// released — the clause that looks like a defect and is not, because otherwise it is never searched
    /// for again.
    /// </summary>
    [Test]
    public void CountsAFilmWithNoHomeReleaseAsOutOnceTheTheatricalWindowHasPassed()
    {
        var cinemaOnly = new Movie
        {
            Key = Abstractions.Identity.MediaItemId.FromInt64(1),
            Title = "Cinema Only",
            InCinemas = new DateOnly(2026, 1, 1),
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                Movies.StatusOf(cinemaOnly, new DateOnly(2026, 2, 1)),
                Is.EqualTo(MovieStatus.InCinemas));
            Assert.That(
                Movies.StatusOf(cinemaOnly, new DateOnly(2026, 6, 1)),
                Is.EqualTo(MovieStatus.Released));
            Assert.That(
                Movies.StatusOf(cinemaOnly, new DateOnly(2025, 12, 1)),
                Is.EqualTo(MovieStatus.Announced));
        });
    }

    [Test]
    public void CountsAFilmWithNoDateAtAllAsUnannounced()
        => Assert.That(
            Movies.StatusOf(
                new Movie { Key = Abstractions.Identity.MediaItemId.FromInt64(2), Title = "Nothing Known" },
                new DateOnly(2026, 8, 17)),
            Is.EqualTo(MovieStatus.Tba));

    /// <summary>
    /// A premiere year is interesting only when it disagrees with the catalog year, which is exactly the
    /// case a release name spells with the other one.
    /// </summary>
    [TestCase(2017, 2016, 2016)]
    [TestCase(2017, 2017, null)]
    public void RecomputesTheSecondaryYearOnlyWhenThePremiereDisagrees(
        int catalogYear,
        int premiereYear,
        int? expected)
        => Assert.That(
            Movies.SecondaryYearOf(catalogYear, new DateOnly(premiereYear, 6, 1)),
            Is.EqualTo(expected));

    [Test]
    public void RecomputesNoSecondaryYearWithoutAPremiere()
        => Assert.That(Movies.SecondaryYearOf(2017, null), Is.Null);
}
