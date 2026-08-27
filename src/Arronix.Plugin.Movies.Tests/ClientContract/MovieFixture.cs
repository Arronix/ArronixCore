using System.IO;
using System.Linq;
using System.Reflection;
using Arronix.Abstractions.Client;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Movies.Tests.ClientContract;

/// <summary>
/// The serialized movie the G07 browser proof reads, and the graph it is written from.
/// </summary>
/// <remarks>
/// Written by the contract's own <see cref="ClientContractEntryPointAttribute.Serialize"/>, so the file in
/// the repository is generated evidence rather than a document somebody keeps in step by hand. Its images
/// are inline so the proof fetches nothing and reaches nowhere.
/// </remarks>
internal static class MovieFixture
{
    /// <summary>Where the generated payload lives, relative to the repository root.</summary>
    internal const string RelativePath = "eng/proofs/fixtures/g07/movie.json";

    /// <summary>The environment variable that turns the drift case into a regeneration.</summary>
    internal const string RegenerateVariable = "ARRONIX_REGENERATE_G07_FIXTURE";

    /// <summary>An 8×12 solid PNG, so the declared measurements are the image's own.</summary>
    internal const string PosterAddress =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAgAAAAMCAIAAADQ/GvKAAAAEklEQVR42mNQcGjAihhGJdARABgLVAFPROX0AAAAAElFTkSuQmCC";

    /// <summary>A 1×1 GIF, so the fixture exercises a second raster container.</summary>
    internal const string FanartAddress =
        "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    /// <summary>Gets the declaration, found the way a consumer finds it: by its exact base type.</summary>
    internal static ClientContractEntryPointAttribute Declaration { get; } =
        typeof(Movie).Assembly.GetCustomAttributes<ClientContractEntryPointAttribute>().Single();

    /// <summary>Builds the movie the published fixture is written from.</summary>
    /// <remarks>
    /// A complete graph on purpose: every field the proof reads back — artwork with role and measurements,
    /// ratings with their scale and voice, the lifecycle its status is computed from, collections, external
    /// identifiers — has to be non-empty for its absence to be a failure rather than a default.
    /// </remarks>
    internal static Movie Canonical() => new()
    {
        ExternalIds = ExternalIdSet.Of(ExternalId.Of("tmdb", 27205), ExternalId.Of("imdb", "tt1375666")),
        Title = "Inception",
        TitleLanguage = Language.English,
        OriginalTitle = "Inception",
        OriginalLanguage = Language.English,
        AlternateTitles = ["Origen", "Начало"],
        Translations =
        [
            new Localized<ItemInfo>(
                new Language("de", "German"),
                new ItemInfo("Inception", "Ein Dieb, der Geheimnisse stiehlt.")),
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
                Artwork = ArtworkSet.Of(new ArtworkImage("poster", new Uri(PosterAddress), 8, 12)),
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
            new ArtworkImage("poster", new Uri(PosterAddress), 8, 12),
            new ArtworkImage("fanart", new Uri(FanartAddress), 1, 1)),
        Popularity = 84.5d,
        Ratings =
        [
            new Rating("tmdb", 8.6m, RatingScale.OutOfTen, RatingVoice.Audience, 37412),
            new Rating("critics", 87m, RatingScale.Percent, RatingVoice.Critic, 320),
        ],
    };

    /// <summary>Writes the canonical movie exactly as the contract writes one.</summary>
    internal static byte[] Serialize() => Declaration.Serialize(Canonical());

    /// <summary>The published fixture's absolute path.</summary>
    internal static string Path() => System.IO.Path.Combine(Root(), RelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>Walks up from the test binary until the solution file appears.</summary>
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(System.IO.Path.Combine(directory.FullName, "Arronix.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("The repository root was not found above the test binary.");
    }
}
