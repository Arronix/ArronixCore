#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.
#pragma warning disable ARX0020 // Media contracts are experimental; a media extension is their intended implementer.

using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Movies;

/// <summary>One score from one source.</summary>
/// <param name="Source">Who published the score. An open vocabulary the supplying cataloger fills.</param>
/// <param name="Value">The score, on <paramref name="Scale"/>.</param>
/// <param name="Scale">The interval the score is expressed on.</param>
/// <param name="Voice">Whether the score aggregates critics or an audience.</param>
/// <param name="Votes">How many votes the score rests on, when the source states one.</param>
/// <remarks>
/// Replaces seven vendor-named fields, their seven descriptors, four orderings and four filters. The kind
/// states only that a movie carries scores, that a score has a scale, and that a critic score and an
/// audience score are different facts — all three of which are movie domain rather than vendor domain, so
/// adding a source is a cataloger change and not a media-kind change.
/// </remarks>
public sealed record Rating(string Source, double Value, RatingScale Scale, RatingVoice Voice, long? Votes = null);

/// <summary>The interval a <see cref="Rating.Value"/> is expressed on.</summary>
public enum RatingScale
{
    /// <summary>Zero to ten.</summary>
    OutOfTen = 0,

    /// <summary>Zero to one hundred.</summary>
    Percent = 1,

    /// <summary>Zero to five.</summary>
    OutOfFive = 2
}

/// <summary>Who produced a score.</summary>
/// <remarks>
/// The distinction the surveyed application encoded only by which vendor field a value landed in. Ordering
/// a library "by rating" across critic aggregates and audience votes without separating them is
/// meaningless, which is why the old shape needed four separate orderings.
/// </remarks>
public enum RatingVoice
{
    /// <summary>An aggregate of the public's votes.</summary>
    Audience = 0,

    /// <summary>An aggregate of published criticism.</summary>
    Critic = 1
}

/// <summary>One localized rendering of a movie's text.</summary>
/// <param name="Language">The language the rendering is in.</param>
/// <param name="Title">The title in that language.</param>
/// <param name="Overview">The synopsis in that language, when the catalog carries one.</param>
/// <remarks>
/// Replaces three multivalued fields — translated titles, translation languages and translated overviews —
/// that were correlated by position with the correlation itself undeclarable. One list of composites; the
/// correlation is the record.
/// </remarks>
public sealed record Translation(Language Language, string Title, string? Overview);

/// <summary>An age rating, and the regulator that issued it.</summary>
/// <param name="Region">The regulator's region.</param>
/// <param name="Value">The rating as that regulator spells it.</param>
/// <remarks>
/// Carrying the region on the value makes the "no cross-region fallback" rule enforceable by a consumer
/// rather than only by the cataloger: a "PG-13" from a foreign regulator is now visibly foreign instead of
/// silently wrong.
/// </remarks>
public sealed record Certification(string Region, string Value);

/// <summary>How far through its release sequence a movie has traveled.</summary>
/// <remarks>
/// The order is the enumeration. Availability is <c>status &gt;= minimum</c>, which the compiler now checks,
/// where the string surface could only carry it as a rank function no consumer could read.
/// </remarks>
public enum MovieStatus
{
    /// <summary>Removed from the catalog upstream. Below every other stage.</summary>
    Deleted = -1,

    /// <summary>Rumored only: an entry exists but nothing about it is scheduled.</summary>
    Tba = 0,

    /// <summary>Announced but not yet shown anywhere.</summary>
    Announced = 1,

    /// <summary>In theatrical release.</summary>
    InCinemas = 2,

    /// <summary>Released on physical or digital media.</summary>
    Released = 3
}

/// <summary>What taking a movie on also takes on.</summary>
public enum MonitorChoice
{
    /// <summary>The movie alone.</summary>
    Movie = 0,

    /// <summary>The movie and every member of its collection.</summary>
    MovieAndCollection = 1,

    /// <summary>Nothing; the movie is added but not wanted.</summary>
    None = 2
}

/// <summary>A collection of movies: monitorable, independently lived, with metadata of its own.</summary>
/// <remarks>
/// A group is a type, so its fields derive exactly as an item's do. That closes the defect the descriptor
/// recorded against itself — an axis could say metadata existed and never say what it was, so a front end
/// could render a movie generically and could not render a collection at all.
/// </remarks>
public sealed class Collection : IMediaGroup<Movie>
{
    /// <summary>The host-minted identity.</summary>
    [Identity]
    public required MediaItemId Key { get; init; }

    /// <summary>The collection's title.</summary>
    [Title, Searchable, Display(Example = "The Lord of the Rings Collection")]
    public required string Title { get; init; }

    /// <summary>The collection's own synopsis, fetched separately from its members'.</summary>
    [Searchable, Multiline]
    public string? Overview { get; init; }

    /// <summary>Artwork for the collection.</summary>
    [Artwork]
    public ArtworkSet Images { get; init; } = ArtworkSet.Empty;

    /// <summary>How many movies the catalog says the collection contains.</summary>
    /// <remarks>Which is not how many the library holds; that is host state about every kind.</remarks>
    [Count, Sortable, Prominence(Prominence.Secondary)]
    public int MemberCount { get; init; }

    /// <summary>The catalogs that assign this collection an identifier.</summary>
    /// <remarks>
    /// A collection identifier is in a different key space from a movie identifier and must never be
    /// compared against one. Under the string surface that was said by inventing a scheme name; here the
    /// key spaces are separated by the type system and no scheme string is involved.
    /// </remarks>
    public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;
}

/// <summary>A movie.</summary>
/// <remarks>
/// Forty field-identifier constants and forty descriptor blocks become twenty-three properties. What a
/// movie does not have costs nothing to say, because it is simply not written.
/// </remarks>
public sealed class Movie : IMediaItem
{
    /// <summary>The host-minted identity, unique within the kind.</summary>
    /// <remarks>
    /// Named <c>Key</c> rather than <c>Id</c> on purpose: a property named <c>Id</c> derives the token
    /// <c>{Movie Id}</c>, which is the name the derivation already gives the primary catalog stamp, and the
    /// two would collide in one token vocabulary.
    /// </remarks>
    [Identity]
    public required MediaItemId Key { get; init; }

    /// <summary>The catalogs this movie is known to, as a namespaced set.</summary>
    /// <remarks>
    /// Replaces the two vendor-named identifier fields, their two descriptors, their two filters and their
    /// two naming tokens. The kind names no scheme: it declares in <see cref="Movies"/> that a movie has a
    /// primary external identity and may have a secondary, and which schemes fill those roles is a fact
    /// about the installed catalogers.
    /// </remarks>
    public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

    /// <summary>The movie's title in the host's configured language.</summary>
    [Title, Searchable, Filterable, Editable, Prominence(Prominence.Primary)]
    [Display(Description = "The movie's title.", Example = "Blade Runner 2049")]
    public required string Title { get; init; }

    /// <summary>The title in the language the movie was produced in.</summary>
    [Searchable, Filterable, Disambiguation]
    [Display(Description = "The title in the language the movie was produced in.",
             Example = "Le Fabuleux Destin d'Amelie Poulain")]
    public string? OriginalTitle { get; init; }

    /// <summary>The language the movie was produced in.</summary>
    [Filterable, Groupable]
    public Language? OriginalLanguage { get; init; }

    /// <summary>Spellings releases appear under. Widens title matching; never displayed.</summary>
    [Searchable, Editable]
    public IReadOnlyList<string> AlternateTitles { get; init; } = [];

    /// <summary>Localized titles and synopses.</summary>
    [Searchable]
    public IReadOnlyList<Translation> Translations { get; init; } = [];

    /// <summary>Year of first release.</summary>
    [Searchable, Sortable, Filterable, Groupable, Disambiguation, Prominence(Prominence.Primary)]
    [Display(Description = "The year the movie was first released anywhere.", Example = "2017")]
    public int? Year { get; init; }

    /// <summary>A second year the movie is also known by, for when the scene disagrees with the catalog.</summary>
    [Filterable, Disambiguation, Prominence(Prominence.Diagnostic)]
    [Display(Description = "A second year the movie is also released under.", Example = "2016")]
    public int? SecondaryYear { get; init; }

    /// <summary>Theatrical release date.</summary>
    [Timestamp, Sortable, Filterable]
    [Display(Name = "In cinemas", Description = "The date the movie opened in cinemas.", Example = "2017-10-04")]
    public DateOnly? InCinemas { get; init; }

    /// <summary>Physical media release date.</summary>
    [Timestamp, Sortable, Filterable]
    [Display(Name = "Physical release", Description = "The date the movie was released on disc.", Example = "2018-01-16")]
    public DateOnly? PhysicalRelease { get; init; }

    /// <summary>Digital release date.</summary>
    [Timestamp, Sortable, Filterable]
    [Display(Name = "Digital release", Description = "The date the movie was released digitally.", Example = "2017-12-26")]
    public DateOnly? DigitalRelease { get; init; }

    /// <summary>The date minimum availability is measured against.</summary>
    /// <remarks>
    /// Stored rather than computed, and that is load-bearing: the field is ordered and filtered on, and a
    /// computed property is invisible to query translation, so ordering a library by it would evaluate
    /// every row in memory. The host recomputes it on write; <see cref="Movies.ReleaseDateOf"/> is the
    /// recomputation, and it is ordinary code where the string surface carried a date-reduction grammar.
    /// </remarks>
    [Derived, Timestamp, Sortable, Filterable, Prominence(Prominence.Secondary)]
    [Display(Name = "Release date", Description = "The date minimum availability is measured against.", Example = "2017-12-26")]
    public DateOnly? ReleaseDate { get; init; }

    /// <summary>How far through its release sequence the movie has traveled.</summary>
    /// <remarks>
    /// Derived for the same reason as <see cref="ReleaseDate"/>. The five-parameter status-stages rule,
    /// including an entire boolean and temporal expression grammar carried in a string, is
    /// <see cref="Movies.StatusOf"/>.
    /// </remarks>
    [Derived, Status, Sortable, Filterable, Groupable, Prominence(Prominence.Secondary)]
    [Display(Name = "Availability", Description = "How far through its release sequence the movie has "
                                                 + "traveled.", Example = "released")]
    public MovieStatus Status { get; init; }

    /// <summary>Synopsis, in the host's configured language.</summary>
    [Searchable, Multiline]
    public string? Overview { get; init; }

    /// <summary>Duration.</summary>
    /// <remarks>
    /// An elapsed time, so the presentational unit the old descriptor declared is intrinsic. The
    /// acceptable-size rule still multiplies the ladder's megabytes-per-minute by it.
    /// </remarks>
    [Sortable, Filterable]
    [Display(Name = "Runtime", Description = "How long the movie runs for.", Example = "2h 44m")]
    public TimeSpan? Runtime { get; init; }

    /// <summary>Production company.</summary>
    [Sortable, Filterable, Groupable]
    [Display(Name = "Studio", Description = "The company that produced the movie.", Example = "Warner Bros.")]
    public string? Studio { get; init; }

    /// <summary>The age rating issued in the host's configured certification region.</summary>
    [Filterable, Groupable]
    public Certification? Certification { get; init; }

    /// <summary>Genres.</summary>
    [Filterable, Groupable, Prominence(Prominence.Secondary)]
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Catalog keywords. Far noisier than genres and not worth an axis.</summary>
    [Filterable, Prominence(Prominence.Diagnostic)]
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>The movie's official site.</summary>
    public Uri? Website { get; init; }

    /// <summary>The trailer.</summary>
    /// <remarks>
    /// A whole address rather than one video host's bare identifier. A movie has a trailer; which service
    /// hosts it is the cataloger's problem, and a cataloger supplying one from elsewhere needs no change
    /// here.
    /// </remarks>
    public Uri? Trailer { get; init; }

    /// <summary>Artwork.</summary>
    /// <remarks>
    /// Replaces four fields differing only by a role name. Roles are a host-owned open vocabulary, so the
    /// artwork-role order the notification declaration used to carry is host-owned too.
    /// </remarks>
    [Artwork]
    public ArtworkSet Images { get; init; } = ArtworkSet.Empty;

    /// <summary>Catalog popularity, used only for ordering discovery results.</summary>
    [Sortable, Prominence(Prominence.Diagnostic)]
    public double? Popularity { get; init; }

    /// <summary>Scores, from whichever sources the installed catalogers supply.</summary>
    [Sortable, Filterable]
    public IReadOnlyList<Rating> Ratings { get; init; } = [];

    /// <summary>The collection this movie belongs to, if any.</summary>
    /// <remarks>
    /// A real reference, replacing a text label and a join key the item carried as data with no foreign
    /// key. There is nothing left to keep in step.
    /// </remarks>
    [Filterable, Groupable, Prominence(Prominence.Secondary)]
    public Collection? Collection { get; init; }
}
