using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Movies.Tests.Serialization;

/// <summary>
/// Whether a browser could read a typed movie out of generated metadata, asked of the real graph.
/// </summary>
/// <remarks>
/// <para>
/// G07.2 requires trimming and ahead-of-time-safe serialization of a complete <see cref="Movie"/>, which
/// means <see cref="JsonTypeInfo"/> produced while the assembly compiled rather than a reflecting
/// serializer discovering the shape at run time. These cases establish that the framework's generator
/// covers this graph on the pinned SDK, and that a movie carrying every kind of value it can carry
/// survives the round trip.
/// </para>
/// <para>
/// Deserialization is asserted through <see cref="JsonSerializer.Deserialize(string, JsonTypeInfo)"/>,
/// which is the overload that carries no trimming annotation. A case that used the reflecting overload
/// would pass here and prove nothing about the client this metadata exists for.
/// </para>
/// </remarks>
[TestFixture]
public sealed class MovieContractSerializationTests
{
    private static JsonTypeInfo<Movie> MovieInfo => MovieContractSerialization.Default.Movie;

    [Test]
    public void TheGeneratorProducesMetadataForTheWholeMovieGraph()
    {
        var context = MovieContractSerialization.Default;

        Assert.Multiple(() =>
        {
            Assert.That(MovieInfo.Kind, Is.EqualTo(JsonTypeInfoKind.Object));

            foreach (var reached in new[]
                     {
                         typeof(Movie),
                         typeof(MovieReleaseTimeline),
                         typeof(MediaCollection<Movie>),
                         typeof(ExternalIdSet),
                         typeof(ExternalId),
                         typeof(ArtworkSet),
                         typeof(ArtworkImage),
                         typeof(Rating),
                         typeof(RatingScale),
                         typeof(RatingVoice),
                         typeof(ContentCertification),
                         typeof(Localized<ItemInfo>),
                         typeof(ItemInfo),
                         typeof(Language),
                         typeof(CatalogRecordState),
                     })
            {
                Assert.That(
                    context.GetTypeInfo(reached),
                    Is.Not.Null,
                    $"'{reached}' is reachable from a movie and the generator produced no metadata for it, "
                    + "so a trimmed client would fail at whichever value touched it first.");
            }
        });
    }

    /// <remarks>
    /// A consequence of the derived-value rule, and one worth asserting rather than noticing later. A
    /// movie's release stage reached the wire only through <c>Movie.Status</c> and
    /// <c>MovieReleaseTimeline.Stage</c>, and a scale's validity only through <c>RatingScale.IsValid</c>;
    /// with both computed properties excluded, neither type is a wire type at all. Nothing is lost — the
    /// stage is a CLR value a consumer recomputes from the milestones it did receive — but the wire
    /// surface is genuinely smaller, and a later change that put either back would be putting a derived
    /// value back with it.
    /// </remarks>
    [Test]
    public void ATypeReachedOnlyThroughADerivedValueIsNotAWireType()
    {
        var context = MovieContractSerialization.Default;

        Assert.Multiple(() =>
        {
            Assert.That(context.GetTypeInfo(typeof(MovieReleaseStage)), Is.Null);
            Assert.That(context.GetTypeInfo(typeof(bool)), Is.Null);
            Assert.That(
                context.GetTypeInfo(typeof(DateOnly?)),
                Is.Not.Null,
                "The milestones the stage is computed from remain on the wire.");
        });
    }

    [Test]
    public void EveryMovieValueSurvivesTheRoundTrip()
    {
        var json = JsonSerializer.Serialize(Fixture(), MovieInfo);
        var back = JsonSerializer.Deserialize(json, MovieInfo);

        Assert.That(back, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(back!.Title, Is.EqualTo("Blade Runner 2049"));
            Assert.That(back.OriginalTitle, Is.EqualTo("Blade Runner 2049"));
            Assert.That(back.Year, Is.EqualTo(2017));
            Assert.That(back.SecondaryYear, Is.EqualTo(2016));
            Assert.That(back.Runtime, Is.EqualTo(TimeSpan.FromMinutes(164)));
            Assert.That(back.Organization, Is.EqualTo("Alcon Entertainment"));
            Assert.That(back.Popularity, Is.EqualTo(87.5d));
            Assert.That(back.CatalogState, Is.EqualTo(CatalogRecordState.Active));
            Assert.That(back.Overview, Is.EqualTo("A young blade runner uncovers a secret."));
            Assert.That(back.Website, Is.EqualTo(new Uri("https://example.test/br2049")));
            Assert.That(back.Preview, Is.EqualTo(new Uri("https://example.test/br2049/trailer")));
            Assert.That(back.Genres, Is.EqualTo(new[] { "Science Fiction", "Drama" }));
            Assert.That(back.Keywords, Is.EqualTo(new[] { "replicant", "dystopia" }));
            Assert.That(back.AlternateTitles, Is.EqualTo(new[] { "BR2049" }));
            Assert.That(back.TitleLanguage, Is.EqualTo(Language.English));
            Assert.That(back.OriginalLanguage, Is.EqualTo(Language.English));
        });

        Assert.Multiple(() =>
        {
            Assert.That(
                back!.ExternalIds.Values,
                Is.EqualTo(new[] { ExternalId.Of("tmdb", 335984), ExternalId.Of("imdb", "tt1856101") }));

            Assert.That(back.Artwork.Images, Has.Count.EqualTo(2));
            Assert.That(back.Artwork.Images[0].Role, Is.EqualTo("poster"));
            Assert.That(back.Artwork.Images[0].Address, Is.EqualTo(new Uri("https://example.test/poster.jpg")));
            Assert.That(back.Artwork.Images[0].Width, Is.EqualTo(1000));
            Assert.That(back.Artwork.Images[0].Height, Is.EqualTo(1500));
            Assert.That(back.Artwork.Images[1].Role, Is.EqualTo("fanart"));
            Assert.That(back.Artwork.Images[1].Width, Is.EqualTo(3840));

            Assert.That(back.Certification, Is.Not.Null);
            Assert.That(back.Certification!.Region, Is.EqualTo("US"));
            Assert.That(back.Certification.Authority, Is.EqualTo("MPA"));
            Assert.That(back.Certification.Code, Is.EqualTo("R"));
            Assert.That(back.Certification.MinimumAge, Is.EqualTo(17));

            Assert.That(back.Translations, Has.Count.EqualTo(1));
            Assert.That(back.Translations[0].Language.Code, Is.EqualTo("de"));
            Assert.That(back.Translations[0].Value.Title, Is.EqualTo("Blade Runner 2049"));
            Assert.That(back.Translations[0].Value.Overview, Is.EqualTo("Ein junger Blade Runner."));

            Assert.That(back.Collections, Has.Count.EqualTo(1));
            Assert.That(back.Collections[0].Title, Is.EqualTo("Blade Runner Collection"));
            Assert.That(back.Collections[0].MemberCount, Is.EqualTo(2));
            Assert.That(back.Collections[0].Overview, Is.EqualTo("Two films."));
            Assert.That(back.Collections[0].Artwork.Images, Has.Count.EqualTo(1));
            Assert.That(
                back.Collections[0].ExternalIds.Values,
                Is.EqualTo(new[] { ExternalId.Of("tmdb", 422837) }));
        });
    }

    /// <remarks>
    /// The case that fails without <c>RatingScale</c>'s declared constructor. A scale is a validated
    /// interval with no settable member, so a deserializer that rebuilds it through the implicit
    /// parameterless constructor produces zero to zero — and the rating that carries it then throws for a
    /// value of 8.6, from inside generated code that names neither the scale nor the payload.
    /// </remarks>
    [Test]
    public void ARatingAndItsScaleSurviveTheRoundTrip()
    {
        var back = JsonSerializer.Deserialize(JsonSerializer.Serialize(Fixture(), MovieInfo), MovieInfo)!;

        Assert.That(back.Ratings, Has.Count.EqualTo(2));

        Assert.Multiple(() =>
        {
            Assert.That(back.Ratings[0].Source, Is.EqualTo("tmdb"));
            Assert.That(back.Ratings[0].Value, Is.EqualTo(8.6m));
            Assert.That(back.Ratings[0].Scale, Is.EqualTo(RatingScale.OutOfTen));
            Assert.That(back.Ratings[0].Scale.Minimum, Is.EqualTo(0m));
            Assert.That(back.Ratings[0].Scale.Maximum, Is.EqualTo(10m));
            Assert.That(back.Ratings[0].Voice, Is.EqualTo(RatingVoice.Audience));
            Assert.That(back.Ratings[0].SampleSize, Is.EqualTo(14_233L));
            Assert.That(back.Ratings[0].NormalizedValue, Is.EqualTo(0.86m));

            Assert.That(back.Ratings[1].Scale, Is.EqualTo(RatingScale.Percent));
            Assert.That(back.Ratings[1].Voice, Is.EqualTo(RatingVoice.Critic));
            Assert.That(back.Ratings[1].SampleSize, Is.Null);
        });
    }

    [Test]
    public void ALifecycleSurvivesTheRoundTripAndItsStageIsRecomputed()
    {
        var back = JsonSerializer.Deserialize(JsonSerializer.Serialize(Fixture(), MovieInfo), MovieInfo)!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Lifecycle.InCinemas, Is.EqualTo(new DateOnly(2017, 10, 6)));
            Assert.That(back.Lifecycle.Physical, Is.EqualTo(new DateOnly(2018, 1, 16)));
            Assert.That(back.Lifecycle.Digital, Is.EqualTo(new DateOnly(2017, 12, 26)));
            Assert.That(back.Lifecycle.EvaluatedOn, Is.EqualTo(new DateOnly(2026, 8, 27)));
            Assert.That(back.Lifecycle.AvailableOn, Is.EqualTo(new DateOnly(2017, 12, 26)));
            Assert.That(back.Status, Is.EqualTo(MovieReleaseStage.Released));
        });
    }

    /// <remarks>
    /// <para>
    /// A derived value is computed from values the payload already carries, so a written copy is a second
    /// source of truth that an untrusted payload can make disagree with the first. Default generated
    /// metadata writes every get-only computed property, which is why each of the five is declared
    /// <c>[JsonIgnore]</c>: <c>Movie.Status</c>, <c>MovieReleaseTimeline.AvailableOn</c> and
    /// <c>.Stage</c>, <c>Rating.NormalizedValue</c>, and <c>RatingScale.IsValid</c>.
    /// </para>
    /// <para>
    /// This asserts both halves of that rule, because either alone is weak. Absence from the payload is
    /// what stops the disagreement existing; a forged member being unable to change what is read is what
    /// says the rule survives a sender that ignores it.
    /// </para>
    /// </remarks>
    [Test]
    public void NoDerivedValueIsWrittenAndAForgedOneCannotChangeWhatIsRead()
    {
        var json = JsonSerializer.Serialize(Fixture(), MovieInfo);

        using (var payload = JsonDocument.Parse(json))
        {
            var root = payload.RootElement;
            var lifecycle = root.GetProperty("Lifecycle");
            var rating = root.GetProperty("Ratings")[0];

            Assert.Multiple(() =>
            {
                Assert.That(root.TryGetProperty("Status", out _), Is.False);
                Assert.That(lifecycle.TryGetProperty("Stage", out _), Is.False);
                Assert.That(lifecycle.TryGetProperty("AvailableOn", out _), Is.False);
                Assert.That(rating.TryGetProperty("NormalizedValue", out _), Is.False);
                Assert.That(rating.GetProperty("Scale").TryGetProperty("IsValid", out _), Is.False);
            });
        }

        // A forged payload states all five anyway. Nothing reads them, so nothing changes.
        var forged = json
            .Replace("\"Lifecycle\":{", "\"Status\":0,\"Lifecycle\":{\"Stage\":0,\"AvailableOn\":\"1970-01-01\",", StringComparison.Ordinal)
            .Replace("\"Scale\":{", "\"NormalizedValue\":0.01,\"Scale\":{\"IsValid\":false,", StringComparison.Ordinal);

        Assert.That(forged, Is.Not.EqualTo(json), "The forgery must actually change the payload.");

        var back = JsonSerializer.Deserialize(forged, MovieInfo)!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Status, Is.EqualTo(MovieReleaseStage.Released));
            Assert.That(back.Lifecycle.Stage, Is.EqualTo(MovieReleaseStage.Released));
            Assert.That(back.Lifecycle.AvailableOn, Is.EqualTo(new DateOnly(2017, 12, 26)));
            Assert.That(back.Ratings[0].NormalizedValue, Is.EqualTo(0.86m));
            Assert.That(back.Ratings[0].Scale.IsValid, Is.True);
        });
    }

    /// <remarks>
    /// The other half of the same rule, and the one a careless implementation of it breaks. A value with no
    /// setter is not the same as a derived value: a rating's source, value, scale, voice and sample size are
    /// all get-only and all authoritative, because the constructor writes them. Excluding them would drop
    /// authoritative facts off the wire, which is the worse of the two mistakes.
    /// </remarks>
    [Test]
    public void AnAuthoritativeGetOnlyValueIsStillWritten()
    {
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(Fixture(), MovieInfo));
        var rating = payload.RootElement.GetProperty("Ratings")[0];

        Assert.Multiple(() =>
        {
            Assert.That(rating.GetProperty("Source").GetString(), Is.EqualTo("tmdb"));
            Assert.That(rating.GetProperty("Value").GetDecimal(), Is.EqualTo(8.6m));
            Assert.That(rating.GetProperty("Voice").GetInt32(), Is.EqualTo((int)RatingVoice.Audience));
            Assert.That(rating.GetProperty("SampleSize").GetInt64(), Is.EqualTo(14_233L));
            Assert.That(rating.GetProperty("Scale").GetProperty("Minimum").GetDecimal(), Is.EqualTo(0m));
            Assert.That(rating.GetProperty("Scale").GetProperty("Maximum").GetDecimal(), Is.EqualTo(10m));

            var certification = payload.RootElement.GetProperty("Certification");
            Assert.That(certification.GetProperty("Region").GetString(), Is.EqualTo("US"));
            Assert.That(certification.GetProperty("Authority").GetString(), Is.EqualTo("MPA"));
            Assert.That(certification.GetProperty("Code").GetString(), Is.EqualTo("R"));
            Assert.That(certification.GetProperty("MinimumAge").GetInt32(), Is.EqualTo(17));

            var translation = payload.RootElement.GetProperty("Translations")[0];
            Assert.That(translation.GetProperty("Language").GetProperty("Code").GetString(), Is.EqualTo("de"));
            Assert.That(translation.GetProperty("Value").GetProperty("Title").GetString(), Is.EqualTo("Blade Runner 2049"));

            Assert.That(payload.RootElement.GetProperty("Lifecycle").GetProperty("EvaluatedOn").GetString(), Is.EqualTo("2026-08-27"));
        });
    }

    /// <remarks>
    /// The generated metadata states the wire form, so it is worth reading once rather than inferring.
    /// Member names are the declared property names: the generator applies no naming policy of its own,
    /// and the platform has not yet decided whether the client wire form carries one.
    /// </remarks>
    [Test]
    public void TheWireFormUsesTheDeclaredPropertyNames()
    {
        var members = MovieInfo.Properties.Select(property => property.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(members, Does.Contain("Title"));
            Assert.That(members, Does.Contain("ExternalIds"));
            Assert.That(members, Does.Contain("CatalogState"));
            Assert.That(members, Does.Not.Contain("title"));
        });
    }

    private static Movie Fixture() => new()
    {
        ExternalIds = ExternalIdSet.Of(ExternalId.Of("tmdb", 335984), ExternalId.Of("imdb", "tt1856101")),
        Title = "Blade Runner 2049",
        TitleLanguage = Language.English,
        OriginalTitle = "Blade Runner 2049",
        OriginalLanguage = Language.English,
        AlternateTitles = ["BR2049"],
        Translations =
        [
            new Localized<ItemInfo>(
                new Language("de", "German"),
                new ItemInfo("Blade Runner 2049", "Ein junger Blade Runner."))
        ],
        Year = 2017,
        SecondaryYear = 2016,
        Lifecycle = new MovieReleaseTimeline
        {
            InCinemas = new DateOnly(2017, 10, 6),
            Physical = new DateOnly(2018, 1, 16),
            Digital = new DateOnly(2017, 12, 26),
            EvaluatedOn = new DateOnly(2026, 8, 27),
        },
        CatalogState = CatalogRecordState.Active,
        Collections =
        [
            new MediaCollection<Movie>
            {
                ExternalIds = ExternalIdSet.Of(ExternalId.Of("tmdb", 422837)),
                Title = "Blade Runner Collection",
                TitleLanguage = Language.English,
                Overview = "Two films.",
                Artwork = ArtworkSet.Of(new ArtworkImage("poster", new Uri("https://example.test/collection.jpg"))),
                MemberCount = 2,
            }
        ],
        Overview = "A young blade runner uncovers a secret.",
        Runtime = TimeSpan.FromMinutes(164),
        Organization = "Alcon Entertainment",
        Certification = new ContentCertification("US", "MPA", "R", 17),
        Genres = ["Science Fiction", "Drama"],
        Keywords = ["replicant", "dystopia"],
        Website = new Uri("https://example.test/br2049"),
        Preview = new Uri("https://example.test/br2049/trailer"),
        Artwork = ArtworkSet.Of(
            new ArtworkImage("poster", new Uri("https://example.test/poster.jpg"), 1000, 1500),
            new ArtworkImage("fanart", new Uri("https://example.test/fanart.jpg"), 3840, 2160)),
        Popularity = 87.5d,
        Ratings =
        [
            new Rating("tmdb", 8.6m, RatingScale.OutOfTen, RatingVoice.Audience, 14_233),
            new Rating("critics", 88m, RatingScale.Percent, RatingVoice.Critic)
        ],
    };
}
