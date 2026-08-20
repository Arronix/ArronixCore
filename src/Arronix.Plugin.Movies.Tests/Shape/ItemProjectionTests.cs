
using System.Linq;
using Arronix.Abstractions.Media;
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
            Assert.That(view.TitleLanguage?.Code, Is.EqualTo("en"));
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
            Assert.That(view.Ref.Kind, Is.EqualTo(new Movies().Kind));
            Assert.That(view.Ref.Level, Is.EqualTo(MoviesDeclaration.Level.Id));
            Assert.That(view.Ref.Id.Value, Is.EqualTo(Godfather.Key.Value));
        });
    }

    [Test]
    public void RegistersTheClosedCommonItemTypeDirectly()
        => Assert.That(
            MoviesDeclaration.Model.ItemType,
            Is.EqualTo(typeof(Movie)));

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
                Is.EqualTo(new[] { "source", "value", "scale", "voice", "sampleSize", "normalizedValue" }));
            Assert.That(value.Items, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void CarriesNestedLocalizedItemInfoWithoutFlatteningItsLanguageOrPayload()
    {
        var movie = new Movie
        {
            Key = Godfather.Key,
            Title = Godfather.Title,
            Lifecycle = Godfather.Lifecycle,
            Translations =
            [
                new(
                    new Arronix.Abstractions.DTOs.Language("fr", "French"),
                    new ItemInfo("Le Parrain", "Chronique d'une famille")),
            ],
        };
        var value = MoviesDeclaration.Model.Read(movie, "translations");
        var localized = value.Items!.Single();
        var payload = localized.Items![1].Items!;

        Assert.Multiple(() =>
        {
            Assert.That(localized.Items![0].Language!.Code, Is.EqualTo("fr"));
            Assert.That(localized.Items[1].Kind, Is.EqualTo(FieldValueKind.Composite));
            Assert.That(payload[0].Text, Is.EqualTo("Le Parrain"));
            Assert.That(payload[1].Text, Is.EqualTo("Chronique d'une famille"));
        });
    }

    [Test]
    public void CarriesTheMovieOwnedReleaseTimelineAsAComposite()
    {
        var descriptor = MoviesDeclaration.Fields["lifecycle"];
        var value = MoviesDeclaration.Model.Read(Godfather, "lifecycle");

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.ValueKind, Is.EqualTo(FieldValueKind.Composite));
            Assert.That(
                descriptor.Components.Select(static component => component.FieldId),
                Does.Contain("inCinemas").And.Contain("physical").And.Contain("digital")
                    .And.Contain("availableOn").And.Contain("stage"));
            Assert.That(value.Items, Has.Count.EqualTo(descriptor.Components.Count));
            Assert.That(value.Items![0].Date, Is.EqualTo(new DateOnly(1972, 3, 14)));
            Assert.That(MoviesDeclaration.Fields, Does.Not.ContainKey("inCinemas"));
            Assert.That(MoviesDeclaration.Fields, Does.Not.ContainKey("physicalRelease"));
            Assert.That(MoviesDeclaration.Fields, Does.Not.ContainKey("digitalRelease"));
            Assert.That(MoviesDeclaration.Fields, Does.Not.ContainKey("releaseDate"));
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
        var value = MoviesDeclaration.Model.Read(Godfather, "collections");
        var collection = value.Items!.Single();

        Assert.Multiple(() =>
        {
            Assert.That(value.Kind, Is.EqualTo(FieldValueKind.Reference));
            Assert.That(collection.Reference!.Value.Level.Value, Is.EqualTo("collection"));
            Assert.That(collection.Text, Is.EqualTo("The Godfather Collection"));
        });
    }

    /// <summary>
    /// Artwork crosses as one set of addresses rather than four fields differing only by a role name.
    /// </summary>
    [Test]
    public void CarriesArtworkAsOneSetRatherThanOneFieldPerRole()
    {
        var value = MoviesDeclaration.Model.Read(Godfather, "artwork");

        Assert.Multiple(() =>
        {
            Assert.That(MoviesDeclaration.Fields["artwork"].ValueKind, Is.EqualTo(FieldValueKind.Artwork));
            Assert.That(value.Items, Is.Not.Null.And.Not.Empty);

            foreach (var role in new[] { "poster", "fanart", "banner", "clearLogo" })
            {
                Assert.That(MoviesDeclaration.Fields, Does.Not.ContainKey(role));
            }
        });
    }

    /// <summary>
    /// The availability-date calculation agrees with what the seeded catalog says, which is the evidence
    /// available that the code replacing the old string grammar preserved its meaning.
    /// </summary>
    [Test]
    public void RecomputesTheReleaseDateTheCatalogWouldHaveComputed()
    {
        foreach (var record in Catalog.Movies.Where(static movie => movie.DigitalRelease is not null
            || movie.PhysicalRelease is not null))
        {
            var movie = MoviesSeedProjection.ToMovie(record);

            Assert.That(movie.Lifecycle.AvailableOn, Is.EqualTo(record.ReleaseDate), record.Title);
        }
    }

    [TestCase(2026, 8, 17, MovieReleaseStage.Released)]
    public void RecomputesTheStatusFromTheThreeDates(int year, int month, int day, MovieReleaseStage expected)
    {
        var movie = MoviesSeedProjection.ToMovie(
            Catalog.Movies.Single(static candidate => candidate.TmdbId == 238));

        Assert.That(movie.Lifecycle.StageOn(new DateOnly(year, month, day)), Is.EqualTo(expected));
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
            Lifecycle = new MovieReleaseTimeline
            {
                InCinemas = new DateOnly(2026, 1, 1),
                EvaluatedOn = new DateOnly(2026, 2, 1)
            }
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                cinemaOnly.Lifecycle.StageOn(new DateOnly(2026, 2, 1)),
                Is.EqualTo(MovieReleaseStage.InCinemas));
            Assert.That(
                cinemaOnly.Lifecycle.StageOn(new DateOnly(2026, 1, 1)),
                Is.EqualTo(MovieReleaseStage.InCinemas));
            Assert.That(
                cinemaOnly.Lifecycle.StageOn(new DateOnly(2026, 6, 1)),
                Is.EqualTo(MovieReleaseStage.Released));
            Assert.That(
                cinemaOnly.Lifecycle.StageOn(new DateOnly(2025, 12, 1)),
                Is.EqualTo(MovieReleaseStage.Announced));
        });
    }

    [Test]
    public void CountsAFilmWithNoDateAtAllAsUnannounced()
        => Assert.That(
            new MovieReleaseTimeline { EvaluatedOn = new DateOnly(2026, 8, 17) }
                .StageOn(new DateOnly(2026, 8, 17)),
            Is.EqualTo(MovieReleaseStage.Tba));

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
