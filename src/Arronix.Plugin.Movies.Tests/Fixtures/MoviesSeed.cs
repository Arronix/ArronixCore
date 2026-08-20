
using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Movies.Tests.Fixtures;

/// <summary>
/// One localized rendering of a film's text: the language, the title in it, and the synopsis in it.
/// </summary>
/// <param name="Language">The language the rendering is in.</param>
/// <param name="Title">The title in that language.</param>
/// <param name="Overview">The synopsis in that language, when the catalog carries one.</param>
/// <remarks>
/// A repeated composite, and the shape has no way to declare one: <see cref="FieldDescriptor"/> has no
/// sub-fields and <see cref="FieldValue.Items"/> is a homogeneous list. The projection therefore splits
/// this triple across three parallel multivalued fields correlated by index, and the correlation itself is
/// undeclarable. Keeping the composite here rather than as three loose lists is what makes the split a
/// projection concern instead of a catalog concern.
/// </remarks>
public sealed record MovieTranslation(Language Language, string Title, string? Overview);

/// <summary>
/// What the rating aggregators say about a film.
/// </summary>
/// <remarks>
/// Each source contributes a value and, for two of them, a vote count. That is a tuple per source, and
/// there is no <c>FieldValueKind.Rating</c> and no composite field, so the projection publishes seven
/// scalar fields where the catalog holds five tuples.
/// </remarks>
public sealed record MovieRatings
{
    /// <summary>An unrated film.</summary>
    public static MovieRatings None { get; } = new();

    /// <summary>The Movie Database score, out of ten.</summary>
    public double? Tmdb { get; init; }

    /// <summary>How many votes the Movie Database score rests on.</summary>
    public long? TmdbVotes { get; init; }

    /// <summary>The IMDb score, out of ten.</summary>
    public double? Imdb { get; init; }

    /// <summary>How many votes the IMDb score rests on.</summary>
    public long? ImdbVotes { get; init; }

    /// <summary>The Rotten Tomatoes critics' score, as a percentage.</summary>
    public double? RottenTomatoes { get; init; }

    /// <summary>The Metacritic score, as a percentage.</summary>
    public double? Metacritic { get; init; }

    /// <summary>The Trakt score, out of ten.</summary>
    public double? Trakt { get; init; }
}

/// <summary>
/// One film as this extension's catalog holds it.
/// </summary>
/// <remarks>
/// <para>This is the extension's own type and it never crosses a boundary — the host sees only
/// <see cref="ItemView"/>. That separation is the whole point of <see cref="IMediaItemSource"/>: the
/// extension projects its catalog, the host stores library state, and the two are merged on read.</para>
/// <para>Note what is <b>not</b> here, and could not be: a path, a root folder, a quality profile, a
/// monitored flag, a tag, a date added, a file. Every one of them is the user's answer about a film rather
/// than a fact about it, and the contract is shaped so that a catalog cannot claim to know them.</para>
/// </remarks>
public sealed record MovieRecord
{
    /// <summary>Catalog-local surrogate identifier. Stable for the lifetime of the process.</summary>
    public required int Id { get; init; }

    /// <summary>The Movie Database identifier.</summary>
    public required int TmdbId { get; init; }

    /// <summary>IMDb identifier, including its <c>tt</c> prefix.</summary>
    public string? ImdbId { get; init; }

    /// <summary>Display title.</summary>
    public required string Title { get; init; }

    /// <summary>Collation key, with a leading article moved to the end.</summary>
    public required string SortTitle { get; init; }

    /// <summary>Title in the original language, when it differs from <see cref="Title"/>.</summary>
    public string? OriginalTitle { get; init; }

    /// <summary>The language the film was produced in.</summary>
    public Language OriginalLanguage { get; init; } = Language.English;

    /// <summary>Titles a release may legitimately use instead of <see cref="Title"/>.</summary>
    public IReadOnlyList<string> AlternativeTitles { get; init; } = [];

    /// <summary>Localized renderings of the title and the synopsis.</summary>
    public IReadOnlyList<MovieTranslation> Translations { get; init; } = [];

    /// <summary>Year of first release.</summary>
    public required int Year { get; init; }

    /// <summary>
    /// The other year a release may legitimately carry, for a film whose production year and release year
    /// disagree.
    /// </summary>
    public int? SecondaryYear { get; init; }

    /// <summary>How far through its release sequence the film has travelled.</summary>
    public required MovieReleaseStage Status { get; init; }

    /// <summary>Whether the upstream catalog still presents the record.</summary>
    public CatalogRecordState CatalogState { get; init; }

    /// <summary>Synopsis.</summary>
    public string? Overview { get; init; }

    /// <summary>Duration in minutes.</summary>
    public int RuntimeMinutes { get; init; }

    /// <summary>Production company.</summary>
    public string? Studio { get; init; }

    /// <summary>Age rating, as issued in the host's configured certification country.</summary>
    public string? Certification { get; init; }

    /// <summary>Genres.</summary>
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Catalog keywords, which are noisier and more numerous than genres.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>The film's own promotional site.</summary>
    public Uri? Website { get; init; }

    /// <summary>The YouTube identifier of the trailer, without any surrounding address.</summary>
    public string? YouTubeTrailerId { get; init; }

    /// <summary>Cover artwork address.</summary>
    public Uri? Poster { get; init; }

    /// <summary>Backdrop artwork address.</summary>
    public Uri? Fanart { get; init; }

    /// <summary>Banner artwork address.</summary>
    public Uri? Banner { get; init; }

    /// <summary>Transparent title-treatment artwork address.</summary>
    public Uri? ClearLogo { get; init; }

    /// <summary>Theatrical release date.</summary>
    public DateOnly? InCinemas { get; init; }

    /// <summary>Physical media release date.</summary>
    public DateOnly? PhysicalRelease { get; init; }

    /// <summary>Digital release date.</summary>
    public DateOnly? DigitalRelease { get; init; }

    /// <summary>Catalog popularity.</summary>
    public double Popularity { get; init; }

    /// <summary>What the rating aggregators say.</summary>
    public MovieRatings Ratings { get; init; } = MovieRatings.None;

    /// <summary>When the catalog record was last read from its source.</summary>
    public DateTimeOffset? LastInfoSync { get; init; }

    /// <summary>The collection this film belongs to, if any.</summary>
    public int? CollectionTmdbId { get; init; }

    /// <summary>
    /// The single date the film "came out", collapsed from the three dates a film actually has.
    /// </summary>
    /// <remarks>
    /// Radarr's <c>Movie.GetReleaseDate()</c> answers this differently depending on the library entry's
    /// minimum-availability setting — announced films collapse to the earliest of the three dates, films
    /// wanted from cinemas collapse to the theatrical date, and everything else collapses to the earliest
    /// home-release date. Minimum availability is a <see cref="SelectionFacet"/>, which is host state, and
    /// neither <see cref="ItemQuery"/> nor <see cref="IMediaItemSource.GetAsync"/> carries it. The catalog
    /// therefore answers the facet-independent form: earliest home release, else theatrical plus the
    /// customary ninety days.
    /// </remarks>
    public DateOnly? ReleaseDate
    {
        get
        {
            if (DigitalRelease is { } digital && PhysicalRelease is { } physical)
            {
                return digital <= physical ? digital : physical;
            }

            return DigitalRelease ?? PhysicalRelease ?? InCinemas?.AddDays(90);
        }
    }
}

/// <summary>
/// A cross-cutting collection: the aggregate a film belongs to without being contained by.
/// </summary>
/// <remarks>
/// A collection has a title, a synopsis, artwork, its own identifier and its own refresh cycle, and the
/// grouping axis can now say all of it, because <c>MediaCollection&lt;TItem&gt;</c> is a type and its fields derive
/// exactly as an item's do. This record is the catalog-side shape the seed was written from; the axis
/// descriptor is derived from the entity rather than from a naming convention nothing validated.
/// </remarks>
public sealed record MovieCollectionRecord
{
    /// <summary>The Movie Database collection identifier.</summary>
    public required int TmdbId { get; init; }

    /// <summary>Display title.</summary>
    public required string Title { get; init; }

    /// <summary>Collation key, with a leading article moved to the end.</summary>
    public required string SortTitle { get; init; }

    /// <summary>Synopsis.</summary>
    public string? Overview { get; init; }

    /// <summary>Cover artwork address.</summary>
    public Uri? Poster { get; init; }

    /// <summary>Backdrop artwork address.</summary>
    public Uri? Fanart { get; init; }

    /// <summary>When the collection record was last read from its source.</summary>
    public DateTimeOffset? LastInfoSync { get; init; }
}

/// <summary>
/// The extension's in-memory catalog and every lookup the other seams need over it.
/// </summary>
/// <remarks>
/// <para>Held by the extension rather than by the host deliberately. In this milestone there is no metadata
/// pipeline and no persistence, so an extension that only declared a shape would ship a surface that can
/// render nothing. Projecting a small real catalog makes every downstream seam — browse, match, plan,
/// name — exercisable end to end without inventing a store.</para>
/// <para>The lookups are the ones the seams ask for and no others. There is no general query method here:
/// paging, sorting and filtering are the projection's business and live in
/// the host item store, because those are the operations the host drives and the ones whose
/// vocabulary comes from the shape rather than from this type.</para>
/// </remarks>
public sealed class MoviesCatalog
{
    /// <summary>The catalog the seed's movie identifiers are issued by.</summary>
    private const string TmdbScheme = "tmdb";

    /// <summary>The catalog the seed cross-references its movies to.</summary>
    private const string ImdbScheme = "imdb";

    /// <summary>The item level, as the typed model derives its identifier from the entity's name.</summary>
    private static readonly MediaLevelId MovieLevel = MediaLevelId.FromString("movie");

    /// <summary>The media kind. Named through its namespace: the class shares its name with a property here.</summary>
    private static readonly MediaKindId MovieKind = new Arronix.Plugin.Movies.Movies().Kind;

    private readonly IReadOnlyList<MovieRecord> _movies;
    private readonly Dictionary<long, MovieRecord> _byId;
    private readonly Dictionary<int, MovieRecord> _byTmdb;
    private readonly Dictionary<string, MovieRecord> _byImdb;
    private readonly Dictionary<int, MovieCollectionRecord> _collections;
    private readonly Dictionary<int, List<MovieRecord>> _byCollection;
    private readonly Dictionary<string, List<MovieRecord>> _byNormalizedTitle;

    private MoviesCatalog(
        IReadOnlyList<MovieRecord> movies,
        IReadOnlyList<MovieCollectionRecord> collections)
    {
        _movies = movies;
        _byId = movies.ToDictionary(movie => (long)movie.Id);
        _byTmdb = movies.ToDictionary(movie => movie.TmdbId);
        _byImdb = movies
            .Where(movie => !string.IsNullOrEmpty(movie.ImdbId))
            .ToDictionary(movie => movie.ImdbId!, StringComparer.OrdinalIgnoreCase);
        _collections = collections.ToDictionary(collection => collection.TmdbId);
        _byCollection = [];
        _byNormalizedTitle = [];

        foreach (var movie in movies)
        {
            Index(movie.Title, movie);
            Index(movie.OriginalTitle, movie);

            foreach (var alternative in movie.AlternativeTitles)
            {
                Index(alternative, movie);
            }

            foreach (var translation in movie.Translations)
            {
                Index(translation.Title, movie);
            }

            if (movie.CollectionTmdbId is not { } collectionId)
            {
                continue;
            }

            if (!_byCollection.TryGetValue(collectionId, out var members))
            {
                members = [];
                _byCollection[collectionId] = members;
            }

            members.Add(movie);
        }

        foreach (var members in _byCollection.Values)
        {
            members.Sort(static (left, right) => left.Year.CompareTo(right.Year));
        }
    }

    /// <summary>Every film, in catalog order.</summary>
    public IReadOnlyList<MovieRecord> Movies => _movies;

    /// <summary>Every collection.</summary>
    public IReadOnlyCollection<MovieCollectionRecord> Collections => _collections.Values;

    /// <summary>Builds the seeded catalog.</summary>
    /// <returns>A catalog holding a small, real, deliberately varied set of films.</returns>
    public static MoviesCatalog CreateSeeded()
        => new(MoviesSeedData.Movies, MoviesSeedData.Collections);

    /// <summary>Looks a film up by its catalog identifier.</summary>
    /// <param name="id">The catalog identifier.</param>
    /// <param name="movie">The film, when one exists.</param>
    /// <returns><see langword="true"/> when the film exists.</returns>
    public bool TryGetById(long id, out MovieRecord? movie) => _byId.TryGetValue(id, out movie);

    /// <summary>Looks a film up by an external identifier.</summary>
    /// <param name="externalId">A <c>tmdb</c> or <c>imdb</c> identifier.</param>
    /// <param name="movie">The film, when one matches.</param>
    /// <returns><see langword="true"/> when the identifier resolves.</returns>
    public bool TryGetByExternalId(ExternalId externalId, out MovieRecord? movie)
    {
        movie = null;

        if (string.Equals(externalId.Scheme, TmdbScheme, StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(externalId.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId)
                && _byTmdb.TryGetValue(tmdbId, out movie);
        }

        return string.Equals(externalId.Scheme, ImdbScheme, StringComparison.OrdinalIgnoreCase)
            && _byImdb.TryGetValue(externalId.Value, out movie);
    }

    /// <summary>Looks a collection up by its identifier.</summary>
    /// <param name="tmdbId">The collection identifier.</param>
    /// <param name="collection">The collection, when one exists.</param>
    /// <returns><see langword="true"/> when the collection exists.</returns>
    public bool TryGetCollection(int tmdbId, out MovieCollectionRecord? collection)
        => _collections.TryGetValue(tmdbId, out collection);

    /// <summary>Every film in one collection, earliest first.</summary>
    /// <param name="tmdbId">The collection identifier.</param>
    /// <returns>The members, possibly empty.</returns>
    public IReadOnlyList<MovieRecord> MembersOf(int tmdbId)
        => _byCollection.TryGetValue(tmdbId, out var members) ? members : [];

    /// <summary>Every film whose title, original title, alternative or translated titles normalize to the given text.</summary>
    /// <param name="title">A raw title, typically taken from a release name.</param>
    /// <returns>The matching films, possibly empty.</returns>
    public IReadOnlyList<MovieRecord> FindByTitle(string title)
    {
        var normalized = DeclaredTitleKey.Of(title);

        return normalized.Length != 0 && _byNormalizedTitle.TryGetValue(normalized, out var matches)
            ? matches
            : [];
    }

    /// <summary>Builds a reference the host can carry for a film.</summary>
    /// <param name="movie">The film.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="movie"/> is <see langword="null"/>.</exception>
    public static MediaItemRef ReferenceTo(MovieRecord movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return new MediaItemRef(MovieKind, MovieLevel, MediaItemId.FromInt64(movie.Id));
    }

    private void Index(string? title, MovieRecord movie)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        var normalized = DeclaredTitleKey.Of(title);

        if (normalized.Length == 0)
        {
            return;
        }

        if (!_byNormalizedTitle.TryGetValue(normalized, out var bucket))
        {
            bucket = [];
            _byNormalizedTitle[normalized] = bucket;
        }

        if (!bucket.Contains(movie))
        {
            bucket.Add(movie);
        }
    }
}
