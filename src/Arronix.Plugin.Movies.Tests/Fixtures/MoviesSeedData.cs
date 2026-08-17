using System.Globalization;
using Arronix.Abstractions.DTOs;

namespace Arronix.Plugin.Movies.Tests.Fixtures;

/// <summary>
/// The languages the seeded catalog is written in.
/// </summary>
/// <remarks>
/// Named constants rather than literals at each use site, because the same language instance has to be
/// reference-comparable across a film's original language and its translation list for the projection's
/// index correlation to line up.
/// </remarks>
public static class MovieLanguages
{
    /// <summary>English.</summary>
    public static Language English => Language.English;

    /// <summary>French.</summary>
    public static Language French { get; } = new("fr", "French");

    /// <summary>German.</summary>
    public static Language German { get; } = new("de", "German");

    /// <summary>Spanish.</summary>
    public static Language Spanish { get; } = new("es", "Spanish");

    /// <summary>Italian.</summary>
    public static Language Italian { get; } = new("it", "Italian");

    /// <summary>Japanese.</summary>
    public static Language Japanese { get; } = new("ja", "Japanese");

    /// <summary>Korean.</summary>
    public static Language Korean { get; } = new("ko", "Korean");

    /// <summary>Portuguese.</summary>
    public static Language Portuguese { get; } = new("pt", "Portuguese");
}

/// <summary>
/// The films and collections the extension projects.
/// </summary>
/// <remarks>
/// <para>Real works with real catalog identifiers, chosen for coverage rather than for taste: three
/// multi-part collections and several standalone films, four original languages, certifications from four
/// different bands, films whose production year and release year disagree, films with no collection, films
/// with no home release, and one film per availability state so that every declared state is reachable in
/// the projection rather than only in the declaration.</para>
/// <para>Synopses are written here rather than taken from any catalog. Artwork addresses point at a
/// reserved example host: a seeded catalog that reached out to a real image CDN would make browsing the
/// reference extension a network operation.</para>
/// </remarks>
public static class MoviesSeedData
{
    private static readonly DateTimeOffset Synced = new(2026, 8, 1, 3, 15, 0, TimeSpan.Zero);

    /// <summary>Every collection the seeded films belong to.</summary>
    public static IReadOnlyList<MovieCollectionRecord> Collections { get; } =
    [
        new()
        {
            TmdbId = 119,
            Title = "The Lord of the Rings Collection",
            SortTitle = "lord of the rings collection",
            Overview = "Peter Jackson's three-film adaptation of Tolkien's novel.",
            Poster = CollectionPoster(119),
            Fanart = CollectionFanart(119),
            LastInfoSync = Synced
        },
        new()
        {
            TmdbId = 726_871,
            Title = "Dune Collection",
            SortTitle = "dune collection",
            Overview = "Denis Villeneuve's adaptation of Frank Herbert's novel, told in parts.",
            Poster = CollectionPoster(726_871),
            Fanart = CollectionFanart(726_871),
            LastInfoSync = Synced
        },
        new()
        {
            TmdbId = 230,
            Title = "The Godfather Collection",
            SortTitle = "godfather collection",
            Overview = "Coppola's chronicle of the Corleone family.",
            Poster = CollectionPoster(230),
            Fanart = CollectionFanart(230),
            LastInfoSync = Synced
        },
        new()
        {
            TmdbId = 10,
            Title = "Star Wars Collection",
            SortTitle = "star wars collection",
            Overview = "The original trilogy of the Skywalker saga.",
            Poster = CollectionPoster(10),
            Fanart = CollectionFanart(10),
            LastInfoSync = Synced
        },
        new()
        {
            TmdbId = 264,
            Title = "Back to the Future Collection",
            SortTitle = "back to the future collection",
            Overview = "Marty McFly and Doc Brown across three decades and one century.",
            Poster = CollectionPoster(264),
            Fanart = CollectionFanart(264),
            LastInfoSync = Synced
        },
        new()
        {
            TmdbId = 8_091,
            Title = "Alien Collection",
            SortTitle = "alien collection",
            Overview = "Ellen Ripley against the xenomorph.",
            Poster = CollectionPoster(8_091),
            Fanart = CollectionFanart(8_091),
            LastInfoSync = Synced
        },
        new()
        {
            TmdbId = 528,
            Title = "The Terminator Collection",
            SortTitle = "terminator collection",
            Overview = "Machines sent back to settle a war that has not happened yet.",
            Poster = CollectionPoster(528),
            Fanart = CollectionFanart(528),
            LastInfoSync = Synced
        },
        new()
        {
            TmdbId = 1_241,
            Title = "Harry Potter Collection",
            SortTitle = "harry potter collection",
            Overview = "The Hogwarts films, adapted from Rowling's novels.",
            Poster = CollectionPoster(1_241),
            Fanart = CollectionFanart(1_241),
            LastInfoSync = Synced
        },
        new()
        {
            TmdbId = 87_118,
            Title = "Toy Story Collection",
            SortTitle = "toy story collection",
            Overview = "Woody, Buzz, and the question of what a toy is for.",
            Poster = CollectionPoster(87_118),
            Fanart = CollectionFanart(87_118),
            LastInfoSync = Synced
        }
    ];

    /// <summary>Every film in the seeded catalog.</summary>
    public static IReadOnlyList<MovieRecord> Movies { get; } = Build();

    private static Uri Poster(int tmdbId)
        => new($"https://image.example/movie/{tmdbId.ToString(CultureInfo.InvariantCulture)}/poster.jpg");

    private static Uri Fanart(int tmdbId)
        => new($"https://image.example/movie/{tmdbId.ToString(CultureInfo.InvariantCulture)}/fanart.jpg");

    private static Uri Banner(int tmdbId)
        => new($"https://image.example/movie/{tmdbId.ToString(CultureInfo.InvariantCulture)}/banner.jpg");

    private static Uri ClearLogo(int tmdbId)
        => new($"https://image.example/movie/{tmdbId.ToString(CultureInfo.InvariantCulture)}/logo.png");

    private static Uri CollectionPoster(int tmdbId)
        => new($"https://image.example/collection/{tmdbId.ToString(CultureInfo.InvariantCulture)}/poster.jpg");

    private static Uri CollectionFanart(int tmdbId)
        => new($"https://image.example/collection/{tmdbId.ToString(CultureInfo.InvariantCulture)}/fanart.jpg");

    private static IReadOnlyList<MovieRecord> Build() =>
    [
        new()
        {
            Id = 1,
            TmdbId = 120,
            ImdbId = "tt0120737",
            Title = "The Lord of the Rings: The Fellowship of the Ring",
            SortTitle = "lord of the rings the fellowship of the ring",
            AlternativeTitles = ["The Fellowship of the Ring", "LOTR 1", "Fellowship of the Ring"],
            Translations =
            [
                new MovieTranslation(
                    MovieLanguages.German,
                    "Der Herr der Ringe: Die Gefaehrten",
                    "Ein Hobbit erbt einen Ring und den Auftrag, ihn zu zerstoeren."),
                new MovieTranslation(
                    MovieLanguages.French,
                    "Le Seigneur des anneaux : La Communaute de l'anneau",
                    null)
            ],
            Year = 2001,
            Status = MovieStatus.Released,
            Overview = "A hobbit inherits a ring and the errand of destroying it, and nine companions set out "
                + "to see the errand done.",
            RuntimeMinutes = 178,
            Studio = "New Line Cinema",
            Certification = "PG-13",
            Genres = ["Adventure", "Fantasy", "Action"],
            Keywords = ["middle-earth", "quest", "wizard", "based on novel", "epic"],
            Website = new Uri("https://www.lordoftherings.example/fellowship"),
            YouTubeTrailerId = "V75dMMIW2B4",
            Poster = Poster(120),
            Fanart = Fanart(120),
            Banner = Banner(120),
            ClearLogo = ClearLogo(120),
            InCinemas = new DateOnly(2001, 12, 19),
            PhysicalRelease = new DateOnly(2002, 8, 6),
            DigitalRelease = new DateOnly(2010, 4, 6),
            Popularity = 84.2,
            Ratings = new MovieRatings
            {
                Tmdb = 8.4,
                TmdbVotes = 24_600,
                Imdb = 8.9,
                ImdbVotes = 2_010_000,
                RottenTomatoes = 92,
                Metacritic = 92,
                Trakt = 8.7
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 119
        },
        new()
        {
            Id = 2,
            TmdbId = 121,
            ImdbId = "tt0167261",
            Title = "The Lord of the Rings: The Two Towers",
            SortTitle = "lord of the rings the two towers",
            AlternativeTitles = ["The Two Towers", "LOTR 2"],
            Translations =
            [
                new MovieTranslation(MovieLanguages.German, "Der Herr der Ringe: Die zwei Tuerme", null)
            ],
            Year = 2002,
            Status = MovieStatus.Released,
            Overview = "The fellowship is broken, the war for Rohan is joined, and the ring goes east with two "
                + "hobbits and a guide who wants it back.",
            RuntimeMinutes = 179,
            Studio = "New Line Cinema",
            Certification = "PG-13",
            Genres = ["Adventure", "Fantasy", "Action"],
            Keywords = ["middle-earth", "siege", "based on novel", "epic"],
            YouTubeTrailerId = "LbfMDwc4azU",
            Poster = Poster(121),
            Fanart = Fanart(121),
            InCinemas = new DateOnly(2002, 12, 18),
            PhysicalRelease = new DateOnly(2003, 8, 26),
            Popularity = 71.9,
            Ratings = new MovieRatings
            {
                Tmdb = 8.4,
                TmdbVotes = 22_100,
                Imdb = 8.8,
                ImdbVotes = 1_790_000,
                RottenTomatoes = 95,
                Metacritic = 87,
                Trakt = 8.6
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 119
        },
        new()
        {
            Id = 3,
            TmdbId = 122,
            ImdbId = "tt0167260",
            Title = "The Lord of the Rings: The Return of the King",
            SortTitle = "lord of the rings the return of the king",
            AlternativeTitles = ["The Return of the King", "LOTR 3"],
            Year = 2003,
            Status = MovieStatus.Released,
            Overview = "The last stand before the gates of Mordor, and the errand finished on a mountainside.",
            RuntimeMinutes = 201,
            Studio = "New Line Cinema",
            Certification = "PG-13",
            Genres = ["Adventure", "Fantasy", "Action"],
            Keywords = ["middle-earth", "coronation", "based on novel", "epic"],
            Poster = Poster(122),
            Fanart = Fanart(122),
            ClearLogo = ClearLogo(122),
            InCinemas = new DateOnly(2003, 12, 17),
            PhysicalRelease = new DateOnly(2004, 5, 25),
            Popularity = 78.4,
            Ratings = new MovieRatings
            {
                Tmdb = 8.5,
                TmdbVotes = 23_400,
                Imdb = 9.0,
                ImdbVotes = 1_980_000,
                RottenTomatoes = 93,
                Metacritic = 94,
                Trakt = 8.8
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 119
        },
        new()
        {
            Id = 4,
            TmdbId = 438_631,
            ImdbId = "tt1160419",
            Title = "Dune",
            SortTitle = "dune",
            OriginalTitle = "Dune: Part One",
            AlternativeTitles = ["Dune Part One", "Dune 2021"],
            Year = 2021,
            Status = MovieStatus.Released,
            Overview = "The heir of a fallen house is drawn into a war over the most valuable substance in the "
                + "galaxy, and into a prophecy he did not ask for.",
            RuntimeMinutes = 155,
            Studio = "Legendary Pictures",
            Certification = "PG-13",
            Genres = ["Science Fiction", "Adventure"],
            Keywords = ["desert", "based on novel", "prophecy", "spice"],
            Website = new Uri("https://www.dunemovie.example/"),
            YouTubeTrailerId = "8g18jFHCLXk",
            Poster = Poster(438_631),
            Fanart = Fanart(438_631),
            InCinemas = new DateOnly(2021, 9, 15),
            PhysicalRelease = new DateOnly(2022, 1, 11),
            DigitalRelease = new DateOnly(2021, 10, 22),
            Popularity = 91.7,
            Ratings = new MovieRatings
            {
                Tmdb = 7.8,
                TmdbVotes = 12_400,
                Imdb = 8.0,
                ImdbVotes = 862_000,
                RottenTomatoes = 83,
                Metacritic = 74,
                Trakt = 7.9
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 726_871
        },
        new()
        {
            Id = 5,
            TmdbId = 693_134,
            ImdbId = "tt15239678",
            Title = "Dune: Part Two",
            SortTitle = "dune part two",
            AlternativeTitles = ["Dune Part 2", "Dune II"],
            Year = 2024,
            Status = MovieStatus.Released,
            Overview = "The war for Arrakis is joined, and the prophecy turns out to have a cost.",
            RuntimeMinutes = 166,
            Studio = "Legendary Pictures",
            Certification = "PG-13",
            Genres = ["Science Fiction", "Adventure"],
            Keywords = ["desert", "based on novel", "holy war", "spice"],
            Poster = Poster(693_134),
            Fanart = Fanart(693_134),
            InCinemas = new DateOnly(2024, 2, 27),
            DigitalRelease = new DateOnly(2024, 4, 16),
            PhysicalRelease = new DateOnly(2024, 5, 14),
            Popularity = 96.3,
            Ratings = new MovieRatings
            {
                Tmdb = 8.2,
                TmdbVotes = 6_900,
                Imdb = 8.5,
                ImdbVotes = 618_000,
                RottenTomatoes = 92,
                Metacritic = 79,
                Trakt = 8.3
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 726_871
        },
        new()
        {
            Id = 6,
            TmdbId = 238,
            ImdbId = "tt0068646",
            Title = "The Godfather",
            SortTitle = "godfather",
            AlternativeTitles = ["Mario Puzo's The Godfather"],
            Translations =
            [
                new MovieTranslation(MovieLanguages.Italian, "Il padrino", "La famiglia Corleone a New York."),
                new MovieTranslation(MovieLanguages.Spanish, "El padrino", null)
            ],
            Year = 1972,
            Status = MovieStatus.Released,
            Overview = "The youngest son of a crime family swears he will never join it, and then inherits it.",
            RuntimeMinutes = 175,
            Studio = "Paramount Pictures",
            Certification = "R",
            Genres = ["Drama", "Crime"],
            Keywords = ["mafia", "based on novel", "family", "new york"],
            Poster = Poster(238),
            Fanart = Fanart(238),
            ClearLogo = ClearLogo(238),
            InCinemas = new DateOnly(1972, 3, 14),
            PhysicalRelease = new DateOnly(2001, 10, 9),
            Popularity = 68.4,
            Ratings = new MovieRatings
            {
                Tmdb = 8.7,
                TmdbVotes = 19_800,
                Imdb = 9.2,
                ImdbVotes = 2_030_000,
                RottenTomatoes = 97,
                Metacritic = 100,
                Trakt = 8.9
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 230
        },
        new()
        {
            Id = 7,
            TmdbId = 240,
            ImdbId = "tt0071562",
            Title = "The Godfather Part II",
            SortTitle = "godfather part ii",
            AlternativeTitles = ["The Godfather: Part II", "Godfather 2"],
            Year = 1974,
            Status = MovieStatus.Released,
            Overview = "Two stories in parallel: a father building the family, and a son hollowing it out.",
            RuntimeMinutes = 202,
            Studio = "Paramount Pictures",
            Certification = "R",
            Genres = ["Drama", "Crime"],
            Keywords = ["mafia", "prequel", "sequel", "sicily"],
            Poster = Poster(240),
            Fanart = Fanart(240),
            InCinemas = new DateOnly(1974, 12, 20),
            PhysicalRelease = new DateOnly(2001, 10, 9),
            Popularity = 51.2,
            Ratings = new MovieRatings
            {
                Tmdb = 8.6,
                TmdbVotes = 12_100,
                Imdb = 9.0,
                ImdbVotes = 1_380_000,
                RottenTomatoes = 96,
                Metacritic = 90,
                Trakt = 8.8
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 230
        },
        new()
        {
            Id = 8,
            TmdbId = 242,
            ImdbId = "tt0099674",
            Title = "The Godfather Part III",
            SortTitle = "godfather part iii",
            AlternativeTitles = ["The Godfather: Part III", "Mario Puzo's The Death of Michael Corleone"],
            Year = 1990,
            Status = MovieStatus.Released,
            Overview = "An aging head of the family tries to buy his way into legitimacy, and pays for it.",
            RuntimeMinutes = 162,
            Studio = "Paramount Pictures",
            Certification = "R",
            Genres = ["Drama", "Crime"],
            Keywords = ["mafia", "vatican", "opera"],
            Poster = Poster(242),
            Fanart = Fanart(242),
            InCinemas = new DateOnly(1990, 12, 25),
            PhysicalRelease = new DateOnly(2001, 10, 9),
            Popularity = 32.6,
            Ratings = new MovieRatings
            {
                Tmdb = 7.4,
                TmdbVotes = 6_400,
                Imdb = 7.6,
                ImdbVotes = 419_000,
                RottenTomatoes = 66,
                Metacritic = 60,
                Trakt = 7.5
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 230
        },
        new()
        {
            Id = 9,
            TmdbId = 11,
            ImdbId = "tt0076759",
            Title = "Star Wars",
            SortTitle = "star wars",
            AlternativeTitles =
            [
                "Star Wars: Episode IV - A New Hope",
                "A New Hope",
                "Star Wars Episode 4"
            ],
            Year = 1977,
            Status = MovieStatus.Released,
            Overview = "A farm boy, two droids and a smuggler go after a battle station.",
            RuntimeMinutes = 121,
            Studio = "Lucasfilm",
            Certification = "PG",
            Genres = ["Adventure", "Action", "Science Fiction"],
            Keywords = ["space opera", "rebellion", "droid", "the force"],
            Poster = Poster(11),
            Fanart = Fanart(11),
            ClearLogo = ClearLogo(11),
            InCinemas = new DateOnly(1977, 5, 25),
            PhysicalRelease = new DateOnly(2004, 9, 21),
            Popularity = 74.5,
            Ratings = new MovieRatings
            {
                Tmdb = 8.2,
                TmdbVotes = 20_400,
                Imdb = 8.6,
                ImdbVotes = 1_460_000,
                RottenTomatoes = 93,
                Metacritic = 90,
                Trakt = 8.4
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 10
        },
        new()
        {
            Id = 10,
            TmdbId = 1_891,
            ImdbId = "tt0080684",
            Title = "The Empire Strikes Back",
            SortTitle = "empire strikes back",
            AlternativeTitles = ["Star Wars: Episode V - The Empire Strikes Back", "Star Wars Episode 5"],
            Year = 1980,
            Status = MovieStatus.Released,
            Overview = "The rebellion is scattered, a apprentice is trained badly, and a parentage is revealed.",
            RuntimeMinutes = 124,
            Studio = "Lucasfilm",
            Certification = "PG",
            Genres = ["Adventure", "Action", "Science Fiction"],
            Keywords = ["space opera", "ice planet", "the force"],
            Poster = Poster(1_891),
            Fanart = Fanart(1_891),
            InCinemas = new DateOnly(1980, 5, 21),
            PhysicalRelease = new DateOnly(2004, 9, 21),
            Popularity = 62.8,
            Ratings = new MovieRatings
            {
                Tmdb = 8.4,
                TmdbVotes = 16_800,
                Imdb = 8.7,
                ImdbVotes = 1_390_000,
                RottenTomatoes = 95,
                Metacritic = 82,
                Trakt = 8.6
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 10
        },
        new()
        {
            Id = 11,
            TmdbId = 1_892,
            ImdbId = "tt0086190",
            Title = "Return of the Jedi",
            SortTitle = "return of the jedi",
            AlternativeTitles = ["Star Wars: Episode VI - Return of the Jedi", "Star Wars Episode 6"],
            Year = 1983,
            Status = MovieStatus.Released,
            Overview = "A rescue, a second battle station, and a son who refuses to fight his father.",
            RuntimeMinutes = 132,
            Studio = "Lucasfilm",
            Certification = "PG",
            Genres = ["Adventure", "Action", "Science Fiction"],
            Keywords = ["space opera", "forest moon", "the force"],
            Poster = Poster(1_892),
            Fanart = Fanart(1_892),
            InCinemas = new DateOnly(1983, 5, 25),
            PhysicalRelease = new DateOnly(2004, 9, 21),
            Popularity = 55.1,
            Ratings = new MovieRatings
            {
                Tmdb = 7.9,
                TmdbVotes = 15_200,
                Imdb = 8.3,
                ImdbVotes = 1_140_000,
                RottenTomatoes = 83,
                Metacritic = 58,
                Trakt = 8.1
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 10
        },
        new()
        {
            Id = 12,
            TmdbId = 105,
            ImdbId = "tt0088763",
            Title = "Back to the Future",
            SortTitle = "back to the future",
            Year = 1985,
            Status = MovieStatus.Released,
            Overview = "A teenager is sent thirty years back by a car, and has to arrange his own parents.",
            RuntimeMinutes = 116,
            Studio = "Universal Pictures",
            Certification = "PG",
            Genres = ["Adventure", "Comedy", "Science Fiction"],
            Keywords = ["time travel", "car", "1950s", "high school"],
            Poster = Poster(105),
            Fanart = Fanart(105),
            ClearLogo = ClearLogo(105),
            InCinemas = new DateOnly(1985, 7, 3),
            PhysicalRelease = new DateOnly(2002, 12, 17),
            Popularity = 66.9,
            Ratings = new MovieRatings
            {
                Tmdb = 8.3,
                TmdbVotes = 20_100,
                Imdb = 8.5,
                ImdbVotes = 1_310_000,
                RottenTomatoes = 93,
                Metacritic = 87,
                Trakt = 8.5
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 264
        },
        new()
        {
            Id = 13,
            TmdbId = 165,
            ImdbId = "tt0096874",
            Title = "Back to the Future Part II",
            SortTitle = "back to the future part ii",
            AlternativeTitles = ["Back to the Future 2"],
            Year = 1989,
            Status = MovieStatus.Released,
            Overview = "Forward thirty years, then sideways into a timeline that went wrong.",
            RuntimeMinutes = 108,
            Studio = "Universal Pictures",
            Certification = "PG",
            Genres = ["Adventure", "Comedy", "Science Fiction"],
            Keywords = ["time travel", "alternate timeline", "hoverboard"],
            Poster = Poster(165),
            Fanart = Fanart(165),
            InCinemas = new DateOnly(1989, 11, 22),
            PhysicalRelease = new DateOnly(2002, 12, 17),
            Popularity = 44.3,
            Ratings = new MovieRatings
            {
                Tmdb = 7.8,
                TmdbVotes = 12_900,
                Imdb = 8.0,
                ImdbVotes = 616_000,
                RottenTomatoes = 63,
                Metacritic = 57,
                Trakt = 7.9
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 264
        },
        new()
        {
            Id = 14,
            TmdbId = 196,
            ImdbId = "tt0099088",
            Title = "Back to the Future Part III",
            SortTitle = "back to the future part iii",
            AlternativeTitles = ["Back to the Future 3"],
            Year = 1990,
            Status = MovieStatus.Released,
            Overview = "Back to 1885, a locomotive, and a reason to stay.",
            RuntimeMinutes = 118,
            Studio = "Universal Pictures",
            Certification = "PG",
            Genres = ["Adventure", "Comedy", "Western"],
            Keywords = ["time travel", "old west", "locomotive"],
            Poster = Poster(196),
            Fanart = Fanart(196),
            InCinemas = new DateOnly(1990, 5, 25),
            PhysicalRelease = new DateOnly(2002, 12, 17),
            Popularity = 38.7,
            Ratings = new MovieRatings
            {
                Tmdb = 7.5,
                TmdbVotes = 11_400,
                Imdb = 7.4,
                ImdbVotes = 471_000,
                RottenTomatoes = 79,
                Metacritic = 55,
                Trakt = 7.6
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 264
        },
        new()
        {
            Id = 15,
            TmdbId = 348,
            ImdbId = "tt0078748",
            Title = "Alien",
            SortTitle = "alien",
            Year = 1979,
            Status = MovieStatus.Released,
            Overview = "A commercial towing crew answers a distress signal and brings something aboard.",
            RuntimeMinutes = 117,
            Studio = "20th Century Fox",
            Certification = "R",
            Genres = ["Horror", "Science Fiction"],
            Keywords = ["space", "creature", "claustrophobia", "android"],
            Poster = Poster(348),
            Fanart = Fanart(348),
            ClearLogo = ClearLogo(348),
            InCinemas = new DateOnly(1979, 5, 25),
            PhysicalRelease = new DateOnly(2003, 12, 2),
            Popularity = 59.4,
            Ratings = new MovieRatings
            {
                Tmdb = 8.2,
                TmdbVotes = 14_700,
                Imdb = 8.5,
                ImdbVotes = 946_000,
                RottenTomatoes = 93,
                Metacritic = 89,
                Trakt = 8.4
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 8_091
        },
        new()
        {
            Id = 16,
            TmdbId = 679,
            ImdbId = "tt0090605",
            Title = "Aliens",
            SortTitle = "aliens",
            Year = 1986,
            Status = MovieStatus.Released,
            Overview = "The survivor goes back, this time with marines, and it is not enough.",
            RuntimeMinutes = 137,
            SecondaryYear = 1991,
            Studio = "20th Century Fox",
            Certification = "R",
            Genres = ["Action", "Horror", "Science Fiction"],
            Keywords = ["space marines", "colony", "queen", "power loader"],
            Poster = Poster(679),
            Fanart = Fanart(679),
            InCinemas = new DateOnly(1986, 7, 18),
            PhysicalRelease = new DateOnly(2003, 12, 2),
            Popularity = 47.8,
            Ratings = new MovieRatings
            {
                Tmdb = 7.9,
                TmdbVotes = 12_300,
                Imdb = 8.4,
                ImdbVotes = 767_000,
                RottenTomatoes = 94,
                Metacritic = 84,
                Trakt = 8.2
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 8_091
        },
        new()
        {
            Id = 17,
            TmdbId = 218,
            ImdbId = "tt0088247",
            Title = "The Terminator",
            SortTitle = "terminator",
            Year = 1984,
            Status = MovieStatus.Released,
            Overview = "A machine and a soldier arrive in the same year, both looking for the same waitress.",
            RuntimeMinutes = 107,
            Studio = "Orion Pictures",
            Certification = "R",
            Genres = ["Action", "Science Fiction", "Thriller"],
            Keywords = ["time travel", "cyborg", "los angeles", "resistance"],
            Poster = Poster(218),
            Fanart = Fanart(218),
            InCinemas = new DateOnly(1984, 10, 26),
            PhysicalRelease = new DateOnly(2001, 8, 28),
            Popularity = 52.3,
            Ratings = new MovieRatings
            {
                Tmdb = 7.7,
                TmdbVotes = 13_100,
                Imdb = 8.1,
                ImdbVotes = 917_000,
                RottenTomatoes = 100,
                Metacritic = 84,
                Trakt = 7.9
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 528
        },
        new()
        {
            Id = 18,
            TmdbId = 280,
            ImdbId = "tt0103064",
            Title = "Terminator 2: Judgment Day",
            SortTitle = "terminator 2 judgment day",
            AlternativeTitles = ["T2", "Terminator 2"],
            Year = 1991,
            Status = MovieStatus.Released,
            Overview = "The same machine, reprogrammed, against a better one.",
            RuntimeMinutes = 137,
            Studio = "Carolco Pictures",
            Certification = "R",
            Genres = ["Action", "Science Fiction", "Thriller"],
            Keywords = ["time travel", "liquid metal", "nuclear war", "mother"],
            Poster = Poster(280),
            Fanart = Fanart(280),
            ClearLogo = ClearLogo(280),
            InCinemas = new DateOnly(1991, 7, 3),
            PhysicalRelease = new DateOnly(2000, 8, 29),
            Popularity = 58.7,
            Ratings = new MovieRatings
            {
                Tmdb = 8.1,
                TmdbVotes = 12_600,
                Imdb = 8.6,
                ImdbVotes = 1_190_000,
                RottenTomatoes = 91,
                Metacritic = 75,
                Trakt = 8.4
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 528
        },
        new()
        {
            Id = 19,
            TmdbId = 671,
            ImdbId = "tt0241527",
            Title = "Harry Potter and the Philosopher's Stone",
            SortTitle = "harry potter and the philosopher's stone",
            AlternativeTitles =
            [
                "Harry Potter and the Sorcerer's Stone",
                "Harry Potter 1",
                "HP1"
            ],
            Translations =
            [
                new MovieTranslation(MovieLanguages.French, "Harry Potter a l'ecole des sorciers", null),
                new MovieTranslation(MovieLanguages.German, "Harry Potter und der Stein der Weisen", null)
            ],
            Year = 2001,
            Status = MovieStatus.Released,
            Overview = "An orphan learns he is a wizard, and that a school has been expecting him.",
            RuntimeMinutes = 152,
            Studio = "Warner Bros. Pictures",
            Certification = "PG",
            Genres = ["Adventure", "Fantasy"],
            Keywords = ["magic school", "based on novel", "orphan", "quidditch"],
            Poster = Poster(671),
            Fanart = Fanart(671),
            InCinemas = new DateOnly(2001, 11, 16),
            PhysicalRelease = new DateOnly(2002, 5, 28),
            DigitalRelease = new DateOnly(2016, 6, 21),
            Popularity = 88.1,
            Ratings = new MovieRatings
            {
                Tmdb = 7.9,
                TmdbVotes = 27_500,
                Imdb = 7.6,
                ImdbVotes = 869_000,
                RottenTomatoes = 81,
                Metacritic = 65,
                Trakt = 7.8
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 1_241
        },
        new()
        {
            Id = 20,
            TmdbId = 12_444,
            ImdbId = "tt0926084",
            Title = "Harry Potter and the Deathly Hallows: Part 1",
            SortTitle = "harry potter and the deathly hallows part 1",
            AlternativeTitles = ["Harry Potter 7 Part 1", "HP7a"],
            Year = 2010,
            Status = MovieStatus.Released,
            Overview = "School is over, three of them are on the run, and the objects have to be found first.",
            RuntimeMinutes = 146,
            Studio = "Warner Bros. Pictures",
            Certification = "PG-13",
            Genres = ["Adventure", "Fantasy"],
            Keywords = ["based on novel", "on the run", "horcrux"],
            Poster = Poster(12_444),
            Fanart = Fanart(12_444),
            InCinemas = new DateOnly(2010, 11, 17),
            PhysicalRelease = new DateOnly(2011, 4, 15),
            DigitalRelease = new DateOnly(2016, 6, 21),
            Popularity = 64.7,
            Ratings = new MovieRatings
            {
                Tmdb = 7.8,
                TmdbVotes = 18_600,
                Imdb = 7.7,
                ImdbVotes = 570_000,
                RottenTomatoes = 77,
                Metacritic = 65,
                Trakt = 7.8
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 1_241
        },
        new()
        {
            Id = 21,
            TmdbId = 12_445,
            ImdbId = "tt1201607",
            Title = "Harry Potter and the Deathly Hallows: Part 2",
            SortTitle = "harry potter and the deathly hallows part 2",
            AlternativeTitles = ["Harry Potter 7 Part 2", "HP7b"],
            Year = 2011,
            Status = MovieStatus.Released,
            Overview = "The last of the objects, the battle for the school, and the end of the war.",
            RuntimeMinutes = 130,
            Studio = "Warner Bros. Pictures",
            Certification = "PG-13",
            Genres = ["Adventure", "Fantasy"],
            Keywords = ["based on novel", "final battle", "horcrux"],
            Poster = Poster(12_445),
            Fanart = Fanart(12_445),
            InCinemas = new DateOnly(2011, 7, 15),
            PhysicalRelease = new DateOnly(2011, 11, 11),
            DigitalRelease = new DateOnly(2016, 6, 21),
            Popularity = 72.9,
            Ratings = new MovieRatings
            {
                Tmdb = 8.1,
                TmdbVotes = 19_400,
                Imdb = 8.1,
                ImdbVotes = 921_000,
                RottenTomatoes = 96,
                Metacritic = 85,
                Trakt = 8.2
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 1_241
        },
        new()
        {
            Id = 22,
            TmdbId = 862,
            ImdbId = "tt0114709",
            Title = "Toy Story",
            SortTitle = "toy story",
            Year = 1995,
            Status = MovieStatus.Released,
            Overview = "A cowboy doll finds out he is no longer the favorite, and takes it badly.",
            RuntimeMinutes = 81,
            Studio = "Pixar",
            Certification = "G",
            Genres = ["Animation", "Adventure", "Family", "Comedy"],
            Keywords = ["toys come to life", "friendship", "jealousy"],
            Poster = Poster(862),
            Fanart = Fanart(862),
            ClearLogo = ClearLogo(862),
            InCinemas = new DateOnly(1995, 11, 22),
            PhysicalRelease = new DateOnly(2001, 10, 9),
            DigitalRelease = new DateOnly(2010, 3, 23),
            Popularity = 81.5,
            Ratings = new MovieRatings
            {
                Tmdb = 8.0,
                TmdbVotes = 18_200,
                Imdb = 8.3,
                ImdbVotes = 1_060_000,
                RottenTomatoes = 100,
                Metacritic = 96,
                Trakt = 8.2
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 87_118
        },
        new()
        {
            Id = 23,
            TmdbId = 863,
            ImdbId = "tt0120363",
            Title = "Toy Story 2",
            SortTitle = "toy story 2",
            Year = 1999,
            Status = MovieStatus.Released,
            Overview = "The cowboy doll is stolen by a collector and has to decide what he is for.",
            RuntimeMinutes = 92,
            Studio = "Pixar",
            Certification = "G",
            Genres = ["Animation", "Adventure", "Family", "Comedy"],
            Keywords = ["toys come to life", "collector", "rescue"],
            Poster = Poster(863),
            Fanart = Fanart(863),
            InCinemas = new DateOnly(1999, 11, 24),
            PhysicalRelease = new DateOnly(2000, 10, 17),
            DigitalRelease = new DateOnly(2010, 3, 23),
            Popularity = 57.3,
            Ratings = new MovieRatings
            {
                Tmdb = 7.6,
                TmdbVotes = 13_500,
                Imdb = 7.9,
                ImdbVotes = 631_000,
                RottenTomatoes = 100,
                Metacritic = 88,
                Trakt = 7.8
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 87_118
        },
        new()
        {
            Id = 24,
            TmdbId = 10_193,
            ImdbId = "tt0435761",
            Title = "Toy Story 3",
            SortTitle = "toy story 3",
            Year = 2010,
            Status = MovieStatus.Released,
            Overview = "The owner leaves for college and the toys end up somewhere much worse than the attic.",
            RuntimeMinutes = 103,
            Studio = "Pixar",
            Certification = "G",
            Genres = ["Animation", "Adventure", "Family", "Comedy"],
            Keywords = ["toys come to life", "daycare", "growing up"],
            Poster = Poster(10_193),
            Fanart = Fanart(10_193),
            InCinemas = new DateOnly(2010, 6, 16),
            PhysicalRelease = new DateOnly(2010, 11, 2),
            DigitalRelease = new DateOnly(2010, 11, 2),
            Popularity = 63.1,
            Ratings = new MovieRatings
            {
                Tmdb = 7.8,
                TmdbVotes = 15_100,
                Imdb = 8.3,
                ImdbVotes = 892_000,
                RottenTomatoes = 98,
                Metacritic = 92,
                Trakt = 8.1
            },
            LastInfoSync = Synced,
            CollectionTmdbId = 87_118
        },
        new()
        {
            Id = 25,
            TmdbId = 78,
            ImdbId = "tt0083658",
            Title = "Blade Runner",
            SortTitle = "blade runner",
            AlternativeTitles = ["Blade Runner: The Final Cut", "Blade Runner Director's Cut"],
            Year = 1982,

            // A film with two legitimate years: the theatrical release and the Final Cut a release name may
            // carry instead. This is exactly what the disambiguation field is for.
            SecondaryYear = 2007,
            Status = MovieStatus.Released,
            Overview = "A policeman whose job is retiring replicants is sent after four of them, and starts "
                + "asking the wrong question.",
            RuntimeMinutes = 117,
            Studio = "Warner Bros. Pictures",
            Certification = "R",
            Genres = ["Science Fiction", "Drama", "Thriller"],
            Keywords = ["dystopia", "android", "los angeles", "neo-noir", "based on novel"],
            Poster = Poster(78),
            Fanart = Fanart(78),
            ClearLogo = ClearLogo(78),
            InCinemas = new DateOnly(1982, 6, 25),
            PhysicalRelease = new DateOnly(2007, 12, 18),
            Popularity = 49.6,
            Ratings = new MovieRatings
            {
                Tmdb = 7.9,
                TmdbVotes = 13_800,
                Imdb = 8.1,
                ImdbVotes = 828_000,
                RottenTomatoes = 89,
                Metacritic = 84,
                Trakt = 8.0
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 26,
            TmdbId = 335_984,
            ImdbId = "tt1856101",
            Title = "Blade Runner 2049",
            SortTitle = "blade runner 2049",
            AlternativeTitles = ["BR2049"],
            Year = 2017,
            Status = MovieStatus.Released,
            Overview = "A newer blade runner uncovers a secret that could finish what is left of society.",
            RuntimeMinutes = 164,
            Studio = "Alcon Entertainment",
            Certification = "R",
            Genres = ["Science Fiction", "Drama"],
            Keywords = ["dystopia", "android", "sequel", "neo-noir"],
            Website = new Uri("https://www.bladerunnermovie.example/"),
            YouTubeTrailerId = "gCcx85zbxz4",
            Poster = Poster(335_984),
            Fanart = Fanart(335_984),
            InCinemas = new DateOnly(2017, 10, 4),
            PhysicalRelease = new DateOnly(2018, 1, 16),
            DigitalRelease = new DateOnly(2017, 12, 26),
            Popularity = 63.5,
            Ratings = new MovieRatings
            {
                Tmdb = 7.6,
                TmdbVotes = 13_200,
                Imdb = 8.0,
                ImdbVotes = 706_000,
                RottenTomatoes = 88,
                Metacritic = 81,
                Trakt = 7.8
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 27,
            TmdbId = 550,
            ImdbId = "tt0137523",
            Title = "Fight Club",
            SortTitle = "fight club",
            Year = 1999,
            Status = MovieStatus.Released,
            Overview = "An insomniac and a soap salesman start a club, and it stops being about fighting.",
            RuntimeMinutes = 139,
            Studio = "Fox 2000 Pictures",
            Certification = "R",
            Genres = ["Drama", "Thriller"],
            Keywords = ["based on novel", "insomnia", "unreliable narrator", "anti-consumerism"],
            Poster = Poster(550),
            Fanart = Fanart(550),
            InCinemas = new DateOnly(1999, 10, 15),
            PhysicalRelease = new DateOnly(2000, 6, 6),
            DigitalRelease = new DateOnly(2009, 9, 22),
            Popularity = 77.2,
            Ratings = new MovieRatings
            {
                Tmdb = 8.4,
                TmdbVotes = 29_300,
                Imdb = 8.8,
                ImdbVotes = 2_320_000,
                RottenTomatoes = 79,
                Metacritic = 67,
                Trakt = 8.5
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 28,
            TmdbId = 680,
            ImdbId = "tt0110912",
            Title = "Pulp Fiction",
            SortTitle = "pulp fiction",
            Year = 1994,
            Status = MovieStatus.Released,
            Overview = "Several Los Angeles stories, told out of order, that turn out to be one.",
            RuntimeMinutes = 154,
            Studio = "Miramax",
            Certification = "R",
            Genres = ["Crime", "Thriller"],
            Keywords = ["nonlinear", "hitman", "los angeles", "briefcase"],
            Poster = Poster(680),
            Fanart = Fanart(680),
            InCinemas = new DateOnly(1994, 9, 10),
            PhysicalRelease = new DateOnly(2002, 5, 21),
            Popularity = 73.6,
            Ratings = new MovieRatings
            {
                Tmdb = 8.5,
                TmdbVotes = 27_800,
                Imdb = 8.9,
                ImdbVotes = 2_260_000,
                RottenTomatoes = 92,
                Metacritic = 95,
                Trakt = 8.6
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 29,
            TmdbId = 155,
            ImdbId = "tt0468569",
            Title = "The Dark Knight",
            SortTitle = "dark knight",
            Year = 2008,
            Status = MovieStatus.Released,
            Overview = "A city, a vigilante, and an adversary who wants nothing that can be bargained for.",
            RuntimeMinutes = 152,
            Studio = "Warner Bros. Pictures",
            Certification = "PG-13",
            Genres = ["Drama", "Action", "Crime", "Thriller"],
            Keywords = ["superhero", "gotham", "chaos", "imax"],
            Poster = Poster(155),
            Fanart = Fanart(155),
            ClearLogo = ClearLogo(155),
            InCinemas = new DateOnly(2008, 7, 16),
            PhysicalRelease = new DateOnly(2008, 12, 9),
            DigitalRelease = new DateOnly(2008, 12, 9),
            Popularity = 89.4,
            Ratings = new MovieRatings
            {
                Tmdb = 8.5,
                TmdbVotes = 32_100,
                Imdb = 9.0,
                ImdbVotes = 2_900_000,
                RottenTomatoes = 94,
                Metacritic = 84,
                Trakt = 8.7
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 30,
            TmdbId = 27_205,
            ImdbId = "tt1375666",
            Title = "Inception",
            SortTitle = "inception",
            Year = 2010,
            Status = MovieStatus.Released,
            Overview = "A thief who steals from dreams is hired to put something into one instead.",
            RuntimeMinutes = 148,
            Studio = "Warner Bros. Pictures",
            Certification = "PG-13",
            Genres = ["Action", "Science Fiction", "Adventure"],
            Keywords = ["dream", "heist", "subconscious", "totem"],
            Poster = Poster(27_205),
            Fanart = Fanart(27_205),
            InCinemas = new DateOnly(2010, 7, 15),
            PhysicalRelease = new DateOnly(2010, 12, 7),
            DigitalRelease = new DateOnly(2010, 12, 7),
            Popularity = 86.8,
            Ratings = new MovieRatings
            {
                Tmdb = 8.4,
                TmdbVotes = 36_400,
                Imdb = 8.8,
                ImdbVotes = 2_600_000,
                RottenTomatoes = 87,
                Metacritic = 74,
                Trakt = 8.6
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 31,
            TmdbId = 157_336,
            ImdbId = "tt0816692",
            Title = "Interstellar",
            SortTitle = "interstellar",
            Year = 2014,
            Status = MovieStatus.Released,
            Overview = "A farmer and former pilot leaves a dying Earth to find somewhere else to put everyone.",
            RuntimeMinutes = 169,
            Studio = "Paramount Pictures",
            Certification = "PG-13",
            Genres = ["Adventure", "Drama", "Science Fiction"],
            Keywords = ["space travel", "wormhole", "time dilation", "father daughter"],
            Poster = Poster(157_336),
            Fanart = Fanart(157_336),
            InCinemas = new DateOnly(2014, 11, 5),
            PhysicalRelease = new DateOnly(2015, 3, 31),
            DigitalRelease = new DateOnly(2015, 3, 31),
            Popularity = 92.5,
            Ratings = new MovieRatings
            {
                Tmdb = 8.4,
                TmdbVotes = 34_900,
                Imdb = 8.7,
                ImdbVotes = 2_130_000,
                RottenTomatoes = 73,
                Metacritic = 74,
                Trakt = 8.5
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 32,
            TmdbId = 603,
            ImdbId = "tt0133093",
            Title = "The Matrix",
            SortTitle = "matrix",
            Year = 1999,
            Status = MovieStatus.Released,
            Overview = "A programmer is offered the choice of finding out what the world actually is.",
            RuntimeMinutes = 136,
            Studio = "Warner Bros. Pictures",
            Certification = "R",
            Genres = ["Action", "Science Fiction"],
            Keywords = ["simulated reality", "hacker", "dystopia", "kung fu"],
            Poster = Poster(603),
            Fanart = Fanart(603),
            ClearLogo = ClearLogo(603),
            InCinemas = new DateOnly(1999, 3, 31),
            PhysicalRelease = new DateOnly(1999, 9, 21),
            DigitalRelease = new DateOnly(2008, 12, 9),
            Popularity = 79.8,
            Ratings = new MovieRatings
            {
                Tmdb = 8.2,
                TmdbVotes = 25_700,
                Imdb = 8.7,
                ImdbVotes = 2_060_000,
                RottenTomatoes = 83,
                Metacritic = 73,
                Trakt = 8.4
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 33,
            TmdbId = 13,
            ImdbId = "tt0109830",
            Title = "Forrest Gump",
            SortTitle = "forrest gump",
            Year = 1994,
            Status = MovieStatus.Released,
            Overview = "Thirty years of American history, from a bus stop bench.",
            RuntimeMinutes = 142,
            Studio = "Paramount Pictures",
            Certification = "PG-13",
            Genres = ["Comedy", "Drama", "Romance"],
            Keywords = ["based on novel", "vietnam war", "running"],
            Poster = Poster(13),
            Fanart = Fanart(13),
            InCinemas = new DateOnly(1994, 6, 23),
            PhysicalRelease = new DateOnly(2001, 8, 28),
            Popularity = 70.4,
            Ratings = new MovieRatings
            {
                Tmdb = 8.5,
                TmdbVotes = 27_100,
                Imdb = 8.8,
                ImdbVotes = 2_310_000,
                RottenTomatoes = 74,
                Metacritic = 82,
                Trakt = 8.5
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 34,
            TmdbId = 278,
            ImdbId = "tt0111161",
            Title = "The Shawshank Redemption",
            SortTitle = "shawshank redemption",
            Year = 1994,
            Status = MovieStatus.Released,
            Overview = "A banker convicted of a murder he says he did not commit spends two decades getting out.",
            RuntimeMinutes = 142,
            Studio = "Castle Rock Entertainment",
            Certification = "R",
            Genres = ["Drama", "Crime"],
            Keywords = ["prison", "friendship", "based on novella", "escape"],
            Poster = Poster(278),
            Fanart = Fanart(278),
            InCinemas = new DateOnly(1994, 9, 23),
            PhysicalRelease = new DateOnly(1999, 1, 26),
            Popularity = 84.9,
            Ratings = new MovieRatings
            {
                Tmdb = 8.7,
                TmdbVotes = 27_600,
                Imdb = 9.3,
                ImdbVotes = 2_930_000,
                RottenTomatoes = 91,
                Metacritic = 82,
                Trakt = 9.0
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 35,
            TmdbId = 424,
            ImdbId = "tt0108052",
            Title = "Schindler's List",
            SortTitle = "schindler's list",
            Translations =
            [
                new MovieTranslation(MovieLanguages.German, "Schindlers Liste", null),
                new MovieTranslation(MovieLanguages.Portuguese, "A Lista de Schindler", null)
            ],
            Year = 1993,
            Status = MovieStatus.Released,
            Overview = "A German industrialist spends a fortune buying the lives of the people working for him.",
            RuntimeMinutes = 195,
            Studio = "Universal Pictures",
            Certification = "R",
            Genres = ["Drama", "History", "War"],
            Keywords = ["holocaust", "based on novel", "world war ii", "black and white"],
            Poster = Poster(424),
            Fanart = Fanart(424),
            InCinemas = new DateOnly(1993, 12, 15),
            PhysicalRelease = new DateOnly(2004, 3, 9),
            Popularity = 55.8,
            Ratings = new MovieRatings
            {
                Tmdb = 8.6,
                TmdbVotes = 16_200,
                Imdb = 9.0,
                ImdbVotes = 1_500_000,
                RottenTomatoes = 98,
                Metacritic = 95,
                Trakt = 8.8
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 36,
            TmdbId = 769,
            ImdbId = "tt0099685",
            Title = "GoodFellas",
            SortTitle = "goodfellas",
            AlternativeTitles = ["Good Fellas", "Goodfellas"],
            Year = 1990,
            Status = MovieStatus.Released,
            Overview = "Thirty years inside a crew, narrated by someone who liked it until he did not.",
            RuntimeMinutes = 145,
            Studio = "Warner Bros. Pictures",
            Certification = "R",
            Genres = ["Drama", "Crime"],
            Keywords = ["mafia", "based on book", "narration", "new york"],
            Poster = Poster(769),
            Fanart = Fanart(769),
            InCinemas = new DateOnly(1990, 9, 12),
            PhysicalRelease = new DateOnly(1997, 8, 26),
            Popularity = 48.2,
            Ratings = new MovieRatings
            {
                Tmdb = 8.5,
                TmdbVotes = 12_900,
                Imdb = 8.7,
                ImdbVotes = 1_260_000,
                RottenTomatoes = 95,
                Metacritic = 92,
                Trakt = 8.6
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 37,
            TmdbId = 274,
            ImdbId = "tt0102926",
            Title = "The Silence of the Lambs",
            SortTitle = "silence of the lambs",
            Year = 1991,
            Status = MovieStatus.Released,
            Overview = "A trainee agent bargains with one killer to find another.",
            RuntimeMinutes = 118,
            Studio = "Orion Pictures",
            Certification = "R",
            Genres = ["Crime", "Drama", "Thriller", "Horror"],
            Keywords = ["fbi", "serial killer", "based on novel", "psychiatry"],
            Poster = Poster(274),
            Fanart = Fanart(274),
            InCinemas = new DateOnly(1991, 2, 14),
            PhysicalRelease = new DateOnly(1998, 10, 27),
            Popularity = 51.7,
            Ratings = new MovieRatings
            {
                Tmdb = 8.3,
                TmdbVotes = 15_800,
                Imdb = 8.6,
                ImdbVotes = 1_540_000,
                RottenTomatoes = 95,
                Metacritic = 86,
                Trakt = 8.5
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 38,
            TmdbId = 389,
            ImdbId = "tt0050083",
            Title = "12 Angry Men",
            SortTitle = "12 angry men",
            AlternativeTitles = ["Twelve Angry Men"],
            Year = 1957,
            Status = MovieStatus.Released,
            Overview = "One juror will not vote guilty, and the afternoon gets long.",
            RuntimeMinutes = 97,
            Studio = "United Artists",
            Certification = "Approved",
            Genres = ["Drama"],
            Keywords = ["jury", "single location", "black and white", "courtroom"],
            Poster = Poster(389),
            Fanart = Fanart(389),
            InCinemas = new DateOnly(1957, 4, 10),
            PhysicalRelease = new DateOnly(2001, 3, 6),
            Popularity = 40.3,
            Ratings = new MovieRatings
            {
                Tmdb = 8.5,
                TmdbVotes = 8_600,
                Imdb = 9.0,
                ImdbVotes = 890_000,
                RottenTomatoes = 100,
                Metacritic = 97,
                Trakt = 8.7
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 39,
            TmdbId = 496_243,
            ImdbId = "tt6751668",
            Title = "Parasite",
            SortTitle = "parasite",
            OriginalTitle = "Gisaengchung",
            OriginalLanguage = MovieLanguages.Korean,
            AlternativeTitles = ["Gisaengchung"],
            Translations =
            [
                new MovieTranslation(MovieLanguages.English, "Parasite", "One family talks its way into another."),
                new MovieTranslation(MovieLanguages.French, "Parasite", null)
            ],
            Year = 2019,
            Status = MovieStatus.Released,
            Overview = "One family talks its way into the household of another, and the basement has a tenant.",
            RuntimeMinutes = 132,
            Studio = "Barunson E&A",
            Certification = "R",
            Genres = ["Comedy", "Thriller", "Drama"],
            Keywords = ["class conflict", "seoul", "con artists", "basement"],
            Poster = Poster(496_243),
            Fanart = Fanart(496_243),
            InCinemas = new DateOnly(2019, 5, 30),
            PhysicalRelease = new DateOnly(2020, 1, 28),
            DigitalRelease = new DateOnly(2020, 1, 14),
            Popularity = 76.1,
            Ratings = new MovieRatings
            {
                Tmdb = 8.5,
                TmdbVotes = 18_400,
                Imdb = 8.5,
                ImdbVotes = 930_000,
                RottenTomatoes = 99,
                Metacritic = 97,
                Trakt = 8.5
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 40,
            TmdbId = 129,
            ImdbId = "tt0245429",
            Title = "Spirited Away",
            SortTitle = "spirited away",
            OriginalTitle = "Sen to Chihiro no kamikakushi",
            OriginalLanguage = MovieLanguages.Japanese,
            AlternativeTitles = ["Sen to Chihiro no Kamikakushi"],
            Translations =
            [
                new MovieTranslation(MovieLanguages.English, "Spirited Away", null),
                new MovieTranslation(MovieLanguages.German, "Chihiros Reise ins Zauberland", null)
            ],
            Year = 2001,
            Status = MovieStatus.Released,
            Overview = "A girl's parents are turned into pigs and she takes a job at a bathhouse to get them back.",
            RuntimeMinutes = 125,
            Studio = "Studio Ghibli",
            Certification = "PG",
            Genres = ["Animation", "Family", "Fantasy"],
            Keywords = ["bathhouse", "spirits", "coming of age", "hand drawn"],
            Poster = Poster(129),
            Fanart = Fanart(129),
            ClearLogo = ClearLogo(129),
            InCinemas = new DateOnly(2001, 7, 20),
            PhysicalRelease = new DateOnly(2003, 4, 15),
            DigitalRelease = new DateOnly(2019, 10, 15),
            Popularity = 85.7,
            Ratings = new MovieRatings
            {
                Tmdb = 8.5,
                TmdbVotes = 16_300,
                Imdb = 8.6,
                ImdbVotes = 863_000,
                RottenTomatoes = 97,
                Metacritic = 96,
                Trakt = 8.6
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 41,
            TmdbId = 372_058,
            ImdbId = "tt5311514",
            Title = "Your Name.",
            SortTitle = "your name",
            OriginalTitle = "Kimi no na wa.",
            OriginalLanguage = MovieLanguages.Japanese,
            AlternativeTitles = ["Kimi no Na wa", "Your Name"],
            Year = 2016,
            Status = MovieStatus.Released,
            Overview = "Two teenagers keep waking up in each other's lives, and then the dates stop lining up.",
            RuntimeMinutes = 106,
            Studio = "CoMix Wave Films",
            Certification = "PG",
            Genres = ["Animation", "Romance", "Drama"],
            Keywords = ["body swap", "comet", "time", "rural japan"],
            Poster = Poster(372_058),
            Fanart = Fanart(372_058),
            InCinemas = new DateOnly(2016, 8, 26),
            PhysicalRelease = new DateOnly(2017, 11, 7),
            DigitalRelease = new DateOnly(2017, 11, 7),
            Popularity = 67.3,
            Ratings = new MovieRatings
            {
                Tmdb = 8.5,
                TmdbVotes = 11_200,
                Imdb = 8.4,
                ImdbVotes = 298_000,
                RottenTomatoes = 98,
                Metacritic = 79,
                Trakt = 8.5
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 42,
            TmdbId = 194,
            ImdbId = "tt0211915",
            Title = "Amelie",
            SortTitle = "amelie",
            OriginalTitle = "Le Fabuleux Destin d'Amelie Poulain",
            OriginalLanguage = MovieLanguages.French,
            AlternativeTitles = ["Le Fabuleux Destin d'Amelie Poulain", "Amelie from Montmartre"],
            Translations =
            [
                new MovieTranslation(MovieLanguages.English, "Amelie", "A waitress decides to fix other people."),
                new MovieTranslation(MovieLanguages.German, "Die fabelhafte Welt der Amelie", null)
            ],
            Year = 2001,
            Status = MovieStatus.Released,
            Overview = "A waitress in Montmartre decides to arrange other people's happiness and keeps missing her own.",
            RuntimeMinutes = 122,
            Studio = "UGC",
            Certification = "R",
            Genres = ["Comedy", "Romance"],
            Keywords = ["paris", "montmartre", "whimsical", "photo booth"],
            Poster = Poster(194),
            Fanart = Fanart(194),
            InCinemas = new DateOnly(2001, 4, 25),
            PhysicalRelease = new DateOnly(2002, 7, 16),
            Popularity = 45.9,
            Ratings = new MovieRatings
            {
                Tmdb = 7.9,
                TmdbVotes = 10_400,
                Imdb = 8.3,
                ImdbVotes = 796_000,
                RottenTomatoes = 89,
                Metacritic = 69,
                Trakt = 8.1
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 43,
            TmdbId = 545_611,
            ImdbId = "tt6710474",
            Title = "Everything Everywhere All at Once",
            SortTitle = "everything everywhere all at once",
            AlternativeTitles = ["EEAAO"],
            Year = 2022,
            Status = MovieStatus.Released,
            Overview = "A laundromat owner is audited, and also has to save every version of herself.",
            RuntimeMinutes = 139,
            Studio = "A24",
            Certification = "R",
            Genres = ["Action", "Adventure", "Science Fiction", "Comedy"],
            Keywords = ["multiverse", "mother daughter", "absurdist", "martial arts"],
            Poster = Poster(545_611),
            Fanart = Fanart(545_611),
            InCinemas = new DateOnly(2022, 3, 24),
            PhysicalRelease = new DateOnly(2022, 7, 5),
            DigitalRelease = new DateOnly(2022, 6, 7),
            Popularity = 69.8,
            Ratings = new MovieRatings
            {
                Tmdb = 7.8,
                TmdbVotes = 6_100,
                Imdb = 7.8,
                ImdbVotes = 615_000,
                RottenTomatoes = 93,
                Metacritic = 81,
                Trakt = 7.9
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 44,
            TmdbId = 872_585,
            ImdbId = "tt15398776",
            Title = "Oppenheimer",
            SortTitle = "oppenheimer",
            Year = 2023,
            Status = MovieStatus.Released,
            Overview = "The physicist who ran the bomb project, and the hearing that followed it.",
            RuntimeMinutes = 181,
            Studio = "Universal Pictures",
            Certification = "R",
            Genres = ["Drama", "History"],
            Keywords = ["biography", "manhattan project", "imax", "cold war"],
            Poster = Poster(872_585),
            Fanart = Fanart(872_585),
            InCinemas = new DateOnly(2023, 7, 19),
            PhysicalRelease = new DateOnly(2023, 11, 21),
            DigitalRelease = new DateOnly(2023, 11, 21),
            Popularity = 94.6,
            Ratings = new MovieRatings
            {
                Tmdb = 8.1,
                TmdbVotes = 8_900,
                Imdb = 8.3,
                ImdbVotes = 856_000,
                RottenTomatoes = 93,
                Metacritic = 90,
                Trakt = 8.2
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 45,
            TmdbId = 19_995,
            ImdbId = "tt0499549",
            Title = "Avatar",
            SortTitle = "avatar",
            Year = 2009,
            Status = MovieStatus.Released,
            Overview = "A paralyzed marine is given a borrowed body on a moon the company wants to strip.",
            RuntimeMinutes = 162,
            SecondaryYear = 2022,
            Studio = "20th Century Fox",
            Certification = "PG-13",
            Genres = ["Action", "Adventure", "Fantasy", "Science Fiction"],
            Keywords = ["alien planet", "3d", "colonialism", "motion capture"],
            Poster = Poster(19_995),
            Fanart = Fanart(19_995),
            InCinemas = new DateOnly(2009, 12, 15),
            PhysicalRelease = new DateOnly(2010, 4, 22),
            DigitalRelease = new DateOnly(2010, 4, 22),
            Popularity = 88.9,
            Ratings = new MovieRatings
            {
                Tmdb = 7.6,
                TmdbVotes = 31_200,
                Imdb = 7.9,
                ImdbVotes = 1_400_000,
                RottenTomatoes = 81,
                Metacritic = 83,
                Trakt = 7.8
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 46,
            TmdbId = 475_557,
            ImdbId = "tt7286456",
            Title = "Joker",
            SortTitle = "joker",
            Year = 2019,
            Status = MovieStatus.Released,
            Overview = "A failed comedian in a city that has stopped pretending it cares.",
            RuntimeMinutes = 122,
            Studio = "Warner Bros. Pictures",
            Certification = "R",
            Genres = ["Crime", "Thriller", "Drama"],
            Keywords = ["gotham", "mental illness", "character study", "1980s"],
            Poster = Poster(475_557),
            Fanart = Fanart(475_557),
            InCinemas = new DateOnly(2019, 10, 2),
            PhysicalRelease = new DateOnly(2020, 1, 7),
            DigitalRelease = new DateOnly(2019, 12, 17),
            Popularity = 82.4,
            Ratings = new MovieRatings
            {
                Tmdb = 8.1,
                TmdbVotes = 25_600,
                Imdb = 8.4,
                ImdbVotes = 1_530_000,
                RottenTomatoes = 68,
                Metacritic = 59,
                Trakt = 8.2
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 47,
            TmdbId = 244_786,
            ImdbId = "tt2582802",
            Title = "Whiplash",
            SortTitle = "whiplash",
            Year = 2014,
            Status = MovieStatus.Released,
            Overview = "A drummer and a teacher who believes cruelty is a teaching method.",
            RuntimeMinutes = 106,
            Studio = "Blumhouse Productions",
            Certification = "R",
            Genres = ["Drama", "Music"],
            Keywords = ["jazz", "conservatory", "abuse", "drumming"],
            Poster = Poster(244_786),
            Fanart = Fanart(244_786),
            InCinemas = new DateOnly(2014, 10, 10),
            PhysicalRelease = new DateOnly(2015, 2, 24),
            DigitalRelease = new DateOnly(2015, 2, 24),
            Popularity = 61.2,
            Ratings = new MovieRatings
            {
                Tmdb = 8.4,
                TmdbVotes = 14_700,
                Imdb = 8.5,
                ImdbVotes = 985_000,
                RottenTomatoes = 94,
                Metacritic = 89,
                Trakt = 8.5
            },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 48,
            TmdbId = 1_124,
            ImdbId = "tt0482571",
            Title = "The Prestige",
            SortTitle = "prestige",
            Year = 2006,
            Status = MovieStatus.Released,
            Overview = "Two stage magicians spend their lives ruining each other over one trick.",
            RuntimeMinutes = 130,
            Studio = "Warner Bros. Pictures",
            Certification = "PG-13",
            Genres = ["Drama", "Mystery", "Science Fiction"],
            Keywords = ["magic", "rivalry", "based on novel", "victorian london"],
            Poster = Poster(1_124),
            Fanart = Fanart(1_124),
            InCinemas = new DateOnly(2006, 10, 19),
            PhysicalRelease = new DateOnly(2007, 2, 20),
            Popularity = 58.1,
            Ratings = new MovieRatings
            {
                Tmdb = 8.2,
                TmdbVotes = 15_400,
                Imdb = 8.5,
                ImdbVotes = 1_400_000,
                RottenTomatoes = 76,
                Metacritic = 66,
                Trakt = 8.3
            },
            LastInfoSync = Synced
        },

        // The remaining four exist so that every declared availability state is reachable in the projection
        // rather than only in the declaration. Their identifiers are outside the real catalog's range and
        // their synopses say what they are for: a state a front end can never render is a state nobody can
        // check the rendering of.
        new()
        {
            Id = 49,
            TmdbId = 9_000_001,
            Title = "A Film Still Only In Cinemas",
            SortTitle = "film still only in cinemas",
            Year = 2026,
            Status = MovieStatus.InCinemas,
            Overview = "Showing theatrically with no home release announced. Present so that the in-cinemas "
                + "state is reachable, and so that minimum availability has something to hold back.",
            RuntimeMinutes = 121,
            Studio = "Example Pictures",
            Certification = "PG-13",
            Genres = ["Drama"],
            Poster = Poster(9_000_001),
            InCinemas = new DateOnly(2026, 6, 12),
            Popularity = 22.4,
            Ratings = new MovieRatings { Tmdb = 7.1, TmdbVotes = 340 },
            LastInfoSync = Synced
        },
        new()
        {
            Id = 50,
            TmdbId = 9_000_002,
            Title = "An Announced Film",
            SortTitle = "announced film",
            Year = 2027,
            Status = MovieStatus.Announced,
            Overview = "Announced with a theatrical date and nothing else. Present so that the announced state "
                + "is reachable.",
            RuntimeMinutes = 0,
            Studio = "Example Pictures",
            Genres = ["Science Fiction"],
            Poster = Poster(9_000_002),
            InCinemas = new DateOnly(2027, 3, 5),
            Popularity = 8.7,
            Ratings = MovieRatings.None,
            LastInfoSync = Synced
        },
        new()
        {
            Id = 51,
            TmdbId = 9_000_003,
            Title = "An Untitled Project",
            SortTitle = "untitled project",
            Year = 2028,
            Status = MovieStatus.Tba,
            Overview = "Known to exist, with no date of any kind. Present so that the to-be-announced state is "
                + "reachable, and so that a null release date has a row to appear in.",
            RuntimeMinutes = 0,
            Genres = ["Drama"],
            Popularity = 2.1,
            Ratings = MovieRatings.None,
            LastInfoSync = Synced
        },
        new()
        {
            Id = 52,
            TmdbId = 9_000_004,
            Title = "A Withdrawn Film",
            SortTitle = "withdrawn film",
            Year = 2019,
            Status = MovieStatus.Deleted,
            Overview = "Removed from the upstream catalog after it was added. Present so that the deleted state "
                + "is reachable, which is the state a library entry falls into without anybody asking it to.",
            RuntimeMinutes = 94,
            Studio = "Example Pictures",
            Genres = ["Documentary"],
            Poster = Poster(9_000_004),
            InCinemas = new DateOnly(2019, 8, 2),
            Popularity = 0.4,
            Ratings = MovieRatings.None,
            LastInfoSync = Synced
        }
    ];
}
