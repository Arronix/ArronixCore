using System;
using System.Collections.Generic;
using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Media.Movies;

namespace Arronix.Sample.MovieCatalog;

/// <summary>
/// The invented titles this sample package supplies.
/// </summary>
/// <remarks>
/// <para>
/// Every value here is made up, and it lives in this package because that is the only place it may live.
/// A sample catalog is a catalog: it answers in the movies media domain's own <see cref="Movie"/> type,
/// through the public cataloger contract, from its own assembly. Nothing about it is a shortcut around
/// admission, pairing or identity, so what an evaluator clicks through is the real path a credentialed
/// production catalog takes.
/// </para>
/// <para>
/// The rows are held as one immutable table rather than generated, so the same search returns the same
/// catalog facts on every call and after every restart. There is no clock: each entry states the date its
/// facts were evaluated on, which is what makes a lifecycle stage a stable fact of the row rather than a
/// property of when the process happened to run.
/// </para>
/// </remarks>
internal static class SampleMovies
{
    /// <summary>The scheme this package is the identity authority for.</summary>
    public const string Scheme = "sample";

    /// <summary>The date every row states its catalog facts were evaluated on.</summary>
    private static readonly DateOnly EvaluatedOn = new(2026, 1, 1);

    /// <summary>A 40x60 solid raster, inline, so a poster costs no request and reaches no host.</summary>
    private const string SlatePoster =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAA8CAIAAACb22+3AAAAOElEQVR42u3NQQkAAAgEsMvl3xL2D2IMEQb7L9" +
        "VzImKxWCwWi8VisVgsFovFYrFYLBaLxWKx+FO8WCjyyi0DNskAAAAASUVORK5CYII=";

    private const string AmberPoster =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAA8CAIAAACb22+3AAAAOElEQVR42u3NQQkAAAgEsMtjSVPZzxgiDPZfpu" +
        "tExGKxWCwWi8VisVgsFovFYrFYLBaLxWLxp3gB9A7R9VtwykAAAAAASUVORK5CYII=";

    private const string MossPoster =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAA8CAIAAACb22+3AAAAN0lEQVR42u3NQQkAAAgEsGt2ReyfwxgiDPZfOj" +
        "0RsVgsFovFYrFYLBaLxWKxWCwWi8VisVj8KV57QvwqI4El5AAAAABJRU5ErkJggg==";

    private const string PlumPoster =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAA8CAIAAACb22+3AAAAOElEQVR42u3NQQkAAAgEsMtlD1/272EMEQb7L1" +
        "N9ImKxWCwWi8VisVgsFovFYrFYLBaLxWKx+FO8JDKSOYphsosAAAAASUVORK5CYII=";

    private const string ClayPoster =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAA8CAIAAACb22+3AAAAOElEQVR42u3NQQkAAAgEsItlB5uZ2hgiDPZfpu" +
        "tExGKxWCwWi8VisVgsFovFYrFYLBaLxWLxp3gBs0LaaGw+dUUAAAAASUVORK5CYII=";

    private const string OceanPoster =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAA8CAIAAACb22+3AAAAOElEQVR42u3NQQkAAAgEsGtjIlPY/28MEQb7L9" +
        "VzImKxWCwWi8VisVgsFovFYrFYLBaLxWKx+FO8iSR2GXzclZMAAAAASUVORK5CYII=";

    private const string AshPoster =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAA8CAIAAACb22+3AAAAN0lEQVR42u3NsQkAAAgDsD7Yxf+P8QwRAtmTdk" +
        "5ELBaLxWKxWCwWi8VisVgsFovFYrFYLBZ/iherGaT5d5efuwAAAABJRU5ErkJggg==";

    private const string RustPoster =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACgAAAA8CAIAAACb22+3AAAAOElEQVR42u3NQQkAAAgEsAtkALOZ3hgiDPZfpu" +
        "tExGKxWCwWi8VisVgsFovFYrFYLBaLxWLxp3gBMtshuQPFuhcAAAAASUVORK5CYII=";

    private static readonly MediaCollection<Movie> HarborlightSequence = new()
    {
        ExternalIds = ExternalIdSet.Of(ExternalId.Of(Scheme, "collection-harborlight")),
        Title = "The Harborlight Sequence",
        Overview = "Three invented films following one lighthouse keeper across four decades.",
        Artwork = ArtworkSet.Of(new ArtworkImage("poster", new Uri(OceanPoster), 40, 60)),
        MemberCount = 3,
    };

    /// <summary>The sample catalog's whole content, in a stable order.</summary>
    public static IReadOnlyList<Movie> All { get; } =
    [
        Row(
            "1001",
            "The Harborlight Keeper",
            1998,
            "A lighthouse keeper on an invented stretch of coast counts the ships that never arrive.",
            OceanPoster,
            ["Drama"],
            ["lighthouse", "coastal", "sample"],
            new TimeSpan(1, 52, 0),
            "Northmark Pictures",
            new ContentCertification("US", "MPA", "PG", 8),
            7.8m,
            4210,
            new MovieReleaseTimeline
            {
                InCinemas = new DateOnly(1998, 9, 18),
                Physical = new DateOnly(1999, 3, 2),
                Digital = new DateOnly(2004, 6, 15),
                EvaluatedOn = EvaluatedOn,
            },
            [HarborlightSequence]),
        Row(
            "1002",
            "Harborlight: The Long Winter",
            2003,
            "The second invented Harborlight film, in which the light fails and the town argues about who owes it.",
            SlatePoster,
            ["Drama", "Mystery"],
            ["lighthouse", "sequel", "sample"],
            new TimeSpan(2, 6, 0),
            "Northmark Pictures",
            new ContentCertification("US", "MPA", "PG-13", 13),
            7.1m,
            2980,
            new MovieReleaseTimeline
            {
                InCinemas = new DateOnly(2003, 11, 7),
                Physical = new DateOnly(2004, 4, 20),
                Digital = new DateOnly(2008, 1, 9),
                EvaluatedOn = EvaluatedOn,
            },
            [HarborlightSequence]),
        Row(
            "1003",
            "Harborlight: Last Light",
            2027,
            "An announced third Harborlight film. Nothing has been released, which is exactly what the catalog says.",
            MossPoster,
            ["Drama"],
            ["lighthouse", "announced", "sample"],
            null,
            "Northmark Pictures",
            null,
            null,
            null,
            new MovieReleaseTimeline { EvaluatedOn = EvaluatedOn },
            [HarborlightSequence]),
        Row(
            "1004",
            "Cartographers of the Quiet Sea",
            2015,
            "Two invented surveyors map a sea that keeps moving underneath them.",
            AmberPoster,
            ["Adventure", "Drama"],
            ["maps", "expedition", "sample"],
            new TimeSpan(2, 18, 0),
            "Vellum Road Films",
            new ContentCertification("US", "MPA", "PG-13", 13),
            8.3m,
            15720,
            new MovieReleaseTimeline
            {
                InCinemas = new DateOnly(2015, 5, 22),
                Physical = new DateOnly(2015, 10, 6),
                Digital = new DateOnly(2015, 9, 15),
                EvaluatedOn = EvaluatedOn,
            },
            []),
        Row(
            "1005",
            "A Field Guide to Falling Objects",
            2021,
            "An invented documentary-shaped comedy about people who catalog what the sky drops on them.",
            PlumPoster,
            ["Comedy"],
            ["documentary", "sample"],
            new TimeSpan(1, 34, 0),
            "Sixth Terrace",
            new ContentCertification("US", "MPA", "R", 17),
            6.4m,
            870,
            new MovieReleaseTimeline
            {
                InCinemas = new DateOnly(2021, 7, 30),
                Digital = new DateOnly(2021, 8, 27),
                EvaluatedOn = EvaluatedOn,
            },
            []),
        Row(
            "1006",
            "The Ninth Aperture",
            2019,
            "An invented thriller in which a projectionist finds one more frame than the reel should hold.",
            RustPoster,
            ["Thriller", "Mystery"],
            ["projection", "sample"],
            new TimeSpan(1, 47, 0),
            "Ardent Hollow",
            new ContentCertification("US", "MPA", "R", 17),
            7.5m,
            9310,
            new MovieReleaseTimeline
            {
                InCinemas = new DateOnly(2019, 2, 8),
                Physical = new DateOnly(2019, 6, 11),
                Digital = new DateOnly(2019, 5, 28),
                EvaluatedOn = EvaluatedOn,
            },
            []),
        Row(
            "1007",
            "Slow Ascent",
            2012,
            "An invented mountain picture with no summit in it.",
            AshPoster,
            ["Drama", "Adventure"],
            ["mountain", "sample"],
            new TimeSpan(2, 3, 0),
            "Vellum Road Films",
            new ContentCertification("US", "MPA", "PG", 8),
            6.9m,
            5120,
            new MovieReleaseTimeline
            {
                InCinemas = new DateOnly(2012, 3, 16),
                Physical = new DateOnly(2012, 8, 7),
                Digital = new DateOnly(2012, 7, 24),
                EvaluatedOn = EvaluatedOn,
            },
            []),
        Row(
            "1008",
            "Every Second Tuesday",
            2026,
            "An invented small-town picture released to cinemas but not yet to anything an evaluator owns.",
            ClayPoster,
            ["Comedy", "Drama"],
            ["small town", "sample"],
            new TimeSpan(1, 41, 0),
            "Sixth Terrace",
            null,
            null,
            null,
            new MovieReleaseTimeline
            {
                InCinemas = new DateOnly(2025, 12, 5),
                EvaluatedOn = EvaluatedOn,
            },
            []),
    ];

    /// <summary>Finds the row a sample identifier names.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The movie, or <see langword="null"/> when this catalog does not own it.</returns>
    public static Movie? Find(ExternalId id)
        => Owns(id)
            ? All.FirstOrDefault(movie => movie.ExternalIds.Values.Any(value =>
                string.Equals(value.Value, id.Value, StringComparison.Ordinal)))
            : null;

    /// <summary>Determines whether an identifier is one this catalog assigned.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns><see langword="true"/> when the scheme is this catalog's own.</returns>
    public static bool Owns(ExternalId id)
        => string.Equals(id.Scheme, Scheme, StringComparison.Ordinal);

    private static Movie Row(
        string id,
        string title,
        int year,
        string overview,
        string poster,
        IReadOnlyList<string> genres,
        IReadOnlyList<string> keywords,
        TimeSpan? runtime,
        string organization,
        ContentCertification? certification,
        decimal? audienceRating,
        int? votes,
        MovieReleaseTimeline lifecycle,
        IReadOnlyList<MediaCollection<Movie>> collections) => new()
    {
        ExternalIds = ExternalIdSet.Of(ExternalId.Of(Scheme, id)),
        Title = title,
        Year = year,
        Overview = overview,
        Runtime = runtime,
        Organization = organization,
        Certification = certification,
        Genres = genres,
        Keywords = keywords,
        Artwork = ArtworkSet.Of(new ArtworkImage("poster", new Uri(poster), 40, 60)),
        Ratings = audienceRating is { } rating && votes is { } sampleSize
            ? [new Rating("sample audience", rating, RatingScale.OutOfTen, RatingVoice.Audience, sampleSize)]
            : [],
        CatalogState = CatalogRecordState.Active,
        Lifecycle = lifecycle,
        Collections = collections,
    };
}
