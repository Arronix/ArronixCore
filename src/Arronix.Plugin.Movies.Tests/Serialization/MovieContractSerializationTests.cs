using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Media;
using Arronix.Plugin.Movies.Tests.ClientContract;

namespace Arronix.Plugin.Movies.Tests.Serialization;

/// <summary>
/// What the generated serialization metadata actually covers, and what it puts on the wire.
/// </summary>
/// <remarks>
/// <para>
/// G07.2 requires trimming and ahead-of-time-safe serialization of a complete <see cref="Movie"/>, which
/// means <see cref="JsonTypeInfo"/> produced while the assembly compiled rather than a reflecting
/// serializer discovering the shape at run time. These cases ask that of the contract's own context —
/// the one the declared entry point reads through — rather than of a context written for a test, so a
/// difference between what is tested and what a browser gets cannot exist.
/// </para>
/// <para>
/// Round trips of the movie graph itself live in <c>MovieClientContractTests</c>, which drives the same
/// context through the same declaration.
/// </para>
/// </remarks>
[TestFixture]
public sealed class MovieContractSerializationTests
{
    private static ClientContractEntryPointAttribute Declaration => MovieClientContractTests.Declaration;

    private static JsonTypeInfo MovieInfo => Declaration.EntityTypeInfo;

    [Test]
    public void TheGeneratorProducesMetadataForTheWholeMovieGraph()
    {
        var context = Declaration.SerializationContext;

        Assert.Multiple(() =>
        {
            Assert.That(MovieInfo.Kind, Is.EqualTo(JsonTypeInfoKind.Object));

            foreach (var reached in new[]
                     {
                         typeof(Movie),
                         typeof(MovieReleaseTimeline),
                         typeof(MediaCollection<Movie>),
                         typeof(ArtworkSet),
                         typeof(ArtworkImage),
                         typeof(ExternalIdSet),
                         typeof(Rating),
                         typeof(RatingScale),
                         typeof(ContentCertification),
                         typeof(Localized<ItemInfo>),
                         typeof(ItemInfo),
                         typeof(Language),
                     })
            {
                Assert.That(context.GetTypeInfo(reached), Is.Not.Null, reached.Name);
            }
        });
    }

    /// <remarks>
    /// A type a payload can only reach through a value the contract computes is not on the wire at all.
    /// The release stage is the worked example: both properties that expose it are derived, so nothing
    /// serializes it and the context generates nothing for it.
    /// </remarks>
    [Test]
    public void ATypeReachedOnlyThroughADerivedValueIsNotAWireType()
    {
        Assert.That(Declaration.SerializationContext.GetTypeInfo(typeof(MovieReleaseStage)), Is.Null);
    }

    [Test]
    public void NoDerivedValueIsWritten()
    {
        using var payload = JsonDocument.Parse(Declaration.Serialize(MovieClientContractTests.Complete()));
        var root = payload.RootElement;
        var lifecycle = root.GetProperty("lifecycle");
        var rating = root.GetProperty("ratings")[0];

        Assert.Multiple(() =>
        {
            Assert.That(root.TryGetProperty("status", out _), Is.False);
            Assert.That(lifecycle.TryGetProperty("stage", out _), Is.False);
            Assert.That(lifecycle.TryGetProperty("availableOn", out _), Is.False);
            Assert.That(rating.TryGetProperty("normalizedValue", out _), Is.False);
            Assert.That(rating.GetProperty("scale").TryGetProperty("isValid", out _), Is.False);
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
        using var payload = JsonDocument.Parse(Declaration.Serialize(MovieClientContractTests.Complete()));
        var root = payload.RootElement;
        var rating = root.GetProperty("ratings")[0];

        Assert.Multiple(() =>
        {
            Assert.That(rating.GetProperty("source").GetString(), Is.EqualTo("tmdb"));
            Assert.That(rating.GetProperty("value").GetDecimal(), Is.EqualTo(8.6m));
            Assert.That(rating.GetProperty("voice").GetInt32(), Is.EqualTo((int)RatingVoice.Audience));
            Assert.That(rating.GetProperty("sampleSize").GetInt64(), Is.EqualTo(37_412L));
            Assert.That(rating.GetProperty("scale").GetProperty("minimum").GetDecimal(), Is.EqualTo(0m));
            Assert.That(rating.GetProperty("scale").GetProperty("maximum").GetDecimal(), Is.EqualTo(10m));

            var certification = root.GetProperty("certification");
            Assert.That(certification.GetProperty("region").GetString(), Is.EqualTo("US"));
            Assert.That(certification.GetProperty("authority").GetString(), Is.EqualTo("MPA"));
            Assert.That(certification.GetProperty("code").GetString(), Is.EqualTo("PG-13"));
            Assert.That(certification.GetProperty("minimumAge").GetInt32(), Is.EqualTo(13));

            var translation = root.GetProperty("translations")[0];
            Assert.That(translation.GetProperty("language").GetProperty("code").GetString(), Is.EqualTo("de"));
            Assert.That(translation.GetProperty("value").GetProperty("title").GetString(), Is.EqualTo("Inception"));

            Assert.That(root.GetProperty("lifecycle").GetProperty("evaluatedOn").GetString(), Is.EqualTo("2026-08-27"));
        });
    }

    /// <remarks>
    /// The contract declares the camel-case policy, so that is the wire form. Reading it off the metadata
    /// rather than off a payload states it once, where a consumer would look.
    /// </remarks>
    [Test]
    public void TheWireFormUsesTheCamelCaseDeclaredNames()
    {
        var members = MovieInfo.Properties.Select(property => property.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(members, Does.Contain("title"));
            Assert.That(members, Does.Contain("externalIds"));
            Assert.That(members, Does.Contain("catalogState"));
            Assert.That(members, Does.Not.Contain("Title"));
        });
    }
}
