using System.Linq;
using System.Reflection;
using System.Text;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Movies.Tests.ClientContract;

/// <summary>
/// The generated client contract, exercised over a complete movie graph.
/// </summary>
/// <remarks>
/// A payload a browser will be handed is worth exactly as much as the typed value it turns into, so these
/// cases construct one movie carrying every shape the common item admits — artwork, ratings with their
/// scale, voice and sample size, a media-owned lifecycle whose stage is computed rather than stored,
/// collections and external identifiers — serialize it through the generated writer, and read it back.
/// Anything that survives serialization and dies in construction is exactly the failure a contract like
/// this exists to prevent, and it is invisible to a test that only checks that metadata was generated.
/// </remarks>
[TestFixture]
public sealed class MovieClientContractTests
{
    /// <summary>Gets the declaration, found the way a consumer finds it: by its exact base type.</summary>
    internal static ClientContractEntryPointAttribute Declaration { get; } =
        typeof(Movie).Assembly.GetCustomAttributes<ClientContractEntryPointAttribute>().Single();

    private static Movie RoundTrip(Movie movie) => (Movie)Declaration.Deserialize(Declaration.Serialize(movie));

    internal static Movie Complete() => new()
    {
        ExternalIds = ExternalIdSet.Of(ExternalId.Of("tmdb", 27205), ExternalId.Of("imdb", "tt1375666")),
        Title = "Inception",
        TitleLanguage = Language.English,
        OriginalTitle = "Inception",
        OriginalLanguage = Language.English,
        AlternateTitles = ["Origen", "Начало"],
        Translations =
        [
            new Localized<ItemInfo>(new Language("de", "German"), new ItemInfo("Inception", "Ein Dieb, der Geheimnisse stiehlt.")),
        ],
        Year = 2010,
        SecondaryYear = 2009,
        Lifecycle = new MovieReleaseTimeline
        {
            InCinemas = new DateOnly(2010, 7, 16),
            Physical = new DateOnly(2010, 12, 7),
            Digital = new DateOnly(2010, 11, 23),
            EvaluatedOn = new DateOnly(2026, 8, 27),
        },
        CatalogState = CatalogRecordState.Active,
        Collections =
        [
            new MediaCollection<Movie>
            {
                ExternalIds = ExternalIdSet.Of(ExternalId.Of("tmdb", 8091)),
                Title = "Christopher Nolan Collection",
                TitleLanguage = Language.English,
                Overview = "Films directed by Christopher Nolan.",
                Artwork = ArtworkSet.Of(new ArtworkImage("poster", new Uri("https://example.test/nolan.jpg"), 500, 750)),
                MemberCount = 11,
            },
        ],
        Overview = "A thief who steals corporate secrets through dream-sharing technology.",
        Runtime = TimeSpan.FromMinutes(148),
        Organization = "Warner Bros. Pictures",
        Certification = new ContentCertification("US", "MPA", "PG-13", 13),
        Genres = ["Action", "Science Fiction"],
        Keywords = ["dream", "heist"],
        Website = new Uri("https://example.test/inception"),
        Preview = new Uri("https://example.test/inception/trailer"),
        Artwork = ArtworkSet.Of(
            new ArtworkImage("poster", new Uri("https://example.test/poster.jpg"), 1000, 1500),
            new ArtworkImage("fanart", new Uri("https://example.test/fanart.jpg"), 1920, 1080)),
        Popularity = 84.5d,
        Ratings =
        [
            new Rating("tmdb", 8.6m, RatingScale.OutOfTen, RatingVoice.Audience, 37412),
            new Rating("critics", 87m, RatingScale.Percent, RatingVoice.Critic, 320),
        ],
    };

    [Test]
    public void ACompleteMovieSurvivesTheGeneratedWriterAndReader()
    {
        var original = Complete();

        var payload = Declaration.Serialize(original);
        var restored = (Movie)Declaration.Deserialize(payload);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Title, Is.EqualTo("Inception"));
            Assert.That(restored.OriginalLanguage?.Code, Is.EqualTo("en"));
            Assert.That(restored.Year, Is.EqualTo(2010));
            Assert.That(restored.SecondaryYear, Is.EqualTo(2009));
            Assert.That(restored.AlternateTitles, Is.EqualTo(new[] { "Origen", "Начало" }));
            Assert.That(restored.Translations.Single().Language.Code, Is.EqualTo("de"));
            Assert.That(restored.Translations.Single().Value.Overview, Is.EqualTo("Ein Dieb, der Geheimnisse stiehlt."));
            Assert.That(restored.Runtime, Is.EqualTo(TimeSpan.FromMinutes(148)));
            Assert.That(restored.Organization, Is.EqualTo("Warner Bros. Pictures"));
            Assert.That(restored.Certification!.Authority, Is.EqualTo("MPA"));
            Assert.That(restored.Certification.MinimumAge, Is.EqualTo(13));
            Assert.That(restored.Genres, Is.EqualTo(new[] { "Action", "Science Fiction" }));
            Assert.That(restored.Website, Is.EqualTo(new Uri("https://example.test/inception")));
            Assert.That(restored.Popularity, Is.EqualTo(84.5d));
            Assert.That(restored.CatalogState, Is.EqualTo(CatalogRecordState.Active));
        });
    }

    [Test]
    public void ArtworkSurvivesWithItsRoleAndItsMeasurements()
    {
        var restored = RoundTrip(Complete());

        Assert.Multiple(() =>
        {
            Assert.That(restored.Artwork.Images, Has.Count.EqualTo(2));
            Assert.That(restored.Artwork.TryGet("poster", out var poster), Is.True);
            Assert.That(poster!.Address, Is.EqualTo(new Uri("https://example.test/poster.jpg")));
            Assert.That(poster.Width, Is.EqualTo(1000));
            Assert.That(poster.Height, Is.EqualTo(1500));
        });
    }

    /// <remarks>
    /// The case that actually bites. A rating validates its value against its scale in its constructor, so
    /// a reader that constructs the scale by its parameterless form and then assigns nothing produces a
    /// zero-width scale and the rating refuses a value that was always valid. The failure is a throw from
    /// domain code during deserialization, which is why it is asserted on the reconstructed value rather
    /// than on the metadata.
    /// </remarks>
    [Test]
    public void RatingsSurviveWithTheirScaleVoiceAndSampleSize()
    {
        var restored = RoundTrip(Complete());

        Assert.Multiple(() =>
        {
            Assert.That(restored.Ratings, Has.Count.EqualTo(2));

            var audience = restored.Ratings[0];
            Assert.That(audience.Source, Is.EqualTo("tmdb"));
            Assert.That(audience.Value, Is.EqualTo(8.6m));
            Assert.That(audience.Scale.Minimum, Is.EqualTo(0m));
            Assert.That(audience.Scale.Maximum, Is.EqualTo(10m));
            Assert.That(audience.Voice, Is.EqualTo(RatingVoice.Audience));
            Assert.That(audience.SampleSize, Is.EqualTo(37412));
            Assert.That(audience.NormalizedValue, Is.EqualTo(0.86m));

            var critic = restored.Ratings[1];
            Assert.That(critic.Scale.Maximum, Is.EqualTo(100m));
            Assert.That(critic.Voice, Is.EqualTo(RatingVoice.Critic));
        });
    }

    [Test]
    public void TheLifecycleSurvivesAndItsStageIsComputedFromIt()
    {
        var restored = RoundTrip(Complete());

        Assert.Multiple(() =>
        {
            Assert.That(restored.Lifecycle.InCinemas, Is.EqualTo(new DateOnly(2010, 7, 16)));
            Assert.That(restored.Lifecycle.Physical, Is.EqualTo(new DateOnly(2010, 12, 7)));
            Assert.That(restored.Lifecycle.Digital, Is.EqualTo(new DateOnly(2010, 11, 23)));
            Assert.That(restored.Lifecycle.EvaluatedOn, Is.EqualTo(new DateOnly(2026, 8, 27)));
            Assert.That(restored.Lifecycle.AvailableOn, Is.EqualTo(new DateOnly(2010, 11, 23)));

            // Not read from the payload: Status has no setter, so this value was produced by movie-owned
            // code running over a movie-owned lifecycle after the value was constructed.
            Assert.That(restored.Status, Is.EqualTo(MovieReleaseStage.Released));
        });
    }

    [Test]
    public void CollectionsAndExternalIdentifiersSurvive()
    {
        var restored = RoundTrip(Complete());

        Assert.Multiple(() =>
        {
            Assert.That(restored.ExternalIds.TryGet("tmdb", out var tmdb), Is.True);
            Assert.That(tmdb.Value, Is.EqualTo("27205"));
            Assert.That(restored.ExternalIds.TryGet("imdb", out var imdb), Is.True);
            Assert.That(imdb.Value, Is.EqualTo("tt1375666"));

            var collection = restored.Collections.Single();
            Assert.That(collection.Title, Is.EqualTo("Christopher Nolan Collection"));
            Assert.That(collection.MemberCount, Is.EqualTo(11));
            Assert.That(collection.Artwork.Images.Single().Role, Is.EqualTo("poster"));
            Assert.That(collection.ExternalIds.Values.Single().Scheme, Is.EqualTo("tmdb"));
        });
    }

    [Test]
    public void TheProjectionCarriesEveryDeclaredFieldInSchemaOrder()
    {
        var projection = Declaration.Project(Complete());
        var schema = Declaration.Schema;

        Assert.Multiple(() =>
        {
            Assert.That(projection.EntityTypeName, Is.EqualTo(Declaration.EntityType.FullName));
            Assert.That(projection.Fields.Select(field => field.Descriptor.FieldId),
                Is.EqualTo(schema.Select(descriptor => descriptor.FieldId)));
        });
    }

    [Test]
    public void TheProjectionRendersArtworkRatingsStatusAndCollections()
    {
        var projection = Declaration.Project(Complete());

        FieldValue Value(string fieldId) =>
            projection.Fields.Single(field => field.Descriptor.FieldId == fieldId).Value;

        Assert.Multiple(() =>
        {
            var artwork = Value("artwork");
            Assert.That(artwork.Kind, Is.EqualTo(FieldValueKind.Artwork));
            Assert.That(artwork.Items!.Select(item => item.Link!.ToString()),
                Is.EqualTo(new[] { "https://example.test/poster.jpg", "https://example.test/fanart.jpg" }));

            var status = Value("status");
            Assert.That(status.Kind, Is.EqualTo(FieldValueKind.Enumerated));
            Assert.That(status.Text, Is.EqualTo("released"));

            var ratings = Value("ratings");
            Assert.That(ratings.Kind, Is.EqualTo(FieldValueKind.Composite));
            Assert.That(ratings.Items, Has.Count.EqualTo(2));

            var collections = Value("collections");
            Assert.That(collections.Kind, Is.EqualTo(FieldValueKind.Composite));
            Assert.That(collections.Items, Has.Count.EqualTo(1));

            var externalIds = Value("externalIds");
            Assert.That(externalIds.Kind, Is.EqualTo(FieldValueKind.ExternalIdentifier));
            Assert.That(externalIds.Items!.Select(item => item.External!.Value.Scheme),
                Is.EqualTo(new[] { "tmdb", "imdb" }));

            var lifecycle = Value("lifecycle");
            Assert.That(lifecycle.Kind, Is.EqualTo(FieldValueKind.Composite));
        });
    }

    [Test]
    public void ProjectingSomethingThatIsNotTheDeclaredEntityIsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => Declaration.Project("not a movie"),
                Throws.ArgumentException.With.Message.Contains("Arronix.Media.Movies.Movie"));

            Assert.That(
                () => Declaration.Serialize("not a movie"),
                Throws.ArgumentException.With.Message.Contains("Arronix.Media.Movies.Movie"));
        });
    }

    [Test]
    public void APayloadThatIsNotAMovieIsRefusedRatherThanPartiallyRead()
    {
        Assert.That(
            () => Declaration.Deserialize(Encoding.UTF8.GetBytes("{\"title\":")),
            Throws.InstanceOf<System.Text.Json.JsonException>());
    }
}
