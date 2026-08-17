#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.
#pragma warning disable ARX0016 // Intent contracts are experimental; a media extension is their intended implementer.
#pragma warning disable ARX0019 // Definition contracts are experimental; a media extension is their intended declarer.
#pragma warning disable ARX0020 // Media contracts are experimental; a media extension is their intended implementer.
#pragma warning disable ARX0021 // Quality contracts are experimental; a media extension is their intended declarer.

using System.Linq;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Quality.Families;
using Arronix.Abstractions.Shape;
using Arronix.Plugin.Movies.Definition;

namespace Arronix.Plugin.Movies;

/// <summary>The movie media type.</summary>
/// <remarks>
/// <para>
/// The rule dividing this file from the attributes on <see cref="Movie"/>: an attribute states a fact about
/// one property in isolation; this states a fact that relates two or more things, or that is about the type
/// as a whole. "This property is the title" is intrinsic. "Files bind one to one to items" relates the item
/// to the file model. "Availability is a threshold over the status" relates a property to a policy.
/// </para>
/// <para>
/// Everything a movie does not have costs nothing to say, because it is simply not called: no coordinate
/// spaces, no sequence axes, no variant axis, no span constraints, no multi-unit naming styles, and none of
/// the rows that used to restate a default.
/// </para>
/// </remarks>
public sealed class Movies : IMediaType<Movie>
{
    /// <summary>The number of days after a cinema date at which a film with no home date counts as out.</summary>
    private const int TheatricalWindowDays = 90;

    /// <summary>
    /// The single-character words that are ordinary words rather than parts of an abbreviation.
    /// </summary>
    /// <remarks>
    /// The two values that used to be a strategy binding's parameter dictionary, resolved at load against a
    /// host vocabulary. They are a private array beside the method that reads them.
    /// </remarks>
    private static readonly string[] ExceptionWords = ["a", "dr"];

    /// <inheritdoc />
    public static MediaKindId Kind { get; } = MediaKindId.FromString("movies");

    /// <inheritdoc />
    public static void Configure(IMediaTypeBuilder<Movie> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.Named("Movie", "Movies");

        // -- files -------------------------------------------------------------------------------------
        // The degenerate corner of the (unit, file, ordinal?) join: anchor and unit are the same entity,
        // both uniqueness constraints hold, the ordinal is meaningless.
        b.Files.OnePerItem();

        // -- format family -----------------------------------------------------------------------------
        // Movies are video and nothing else, so neither coexistence nor embedded metadata is stated. The
        // quality model belongs to the family rather than to this kind: a stream download at 1080 lines is
        // the same thing whether the work is a film or an episode, so television shares this exact model
        // and the two cannot drift. What a movie release says that no other kind's does is one refinement
        // class, and it is the second call rather than a paragraph of this one.
        b.Format("video", "Video")
         .Extensions(
             ".mkv", ".mp4", ".avi", ".m4v", ".mpg", ".mpeg", ".mov", ".wmv", ".ts", ".m2ts", ".webm",
             ".divx", ".flv", ".vob", ".ogv", ".ogm", ".rmvb", ".m4p", ".mk3d", ".iso", ".img")
         .Quality<VideoQuality, VideoQualityType>()
         .RefinedBy<VideoQuality, MoviesVideoRefinement>()

         // An edition is a property of a FILE that only a movie has: not a property of a movie, because a
         // file is not an item, and not host-general, because an edition is a movie noun. It rides as an
         // identifier-keyed facet — the one thing here that is still untyped, awaiting the per-kind
         // file-facts entity a kind with a real file-unit split will force the shape of.
         .Facet("edition", "Edition", TechnicalFacetCase.TitleCaseWithExceptions,
                exceptions: ["IMAX", "3D", "SDR", "HDR", "DV"], ordinalSuffixesLowerCase: true);

        // -- external identity -------------------------------------------------------------------------
        // Roles, never schemes. Which catalog fills a role is a fact about the installed catalogers.
        b.Identity(m => m.ExternalIds)
         .Requires(IdentifierRole.PrimaryWork)
         .Admits(IdentifierRole.SecondaryWork)

         // Kept, and recorded as misplaced: that the assigning catalog merges duplicates and answers a
         // retired identifier with a redirect is a fact about a CATALOG, not about movies. It moves to the
         // cataloger's own declaration once catalogers are plugins of their own.
         .SupportsRedirects();

        // -- grouping ----------------------------------------------------------------------------------
        // Arity, member position and the primary-member flag all derive from the property being a single
        // optional reference. The four remaining facts are behaviour the type cannot imply.
        b.Group(m => m.Collection)
         .Named("Collection", "Collections")
         .Monitorable()
         .DiscoverySource()
         .Independent();

        // -- selection policy --------------------------------------------------------------------------
        // A threshold over an ordered enumeration, not set membership. The enumeration is the order.
        b.Selection(m => m.Status)
         .Named("Minimum availability")
         .AtLeast(MovieStatus.Released)
         .Offering(MovieStatus.Tba, MovieStatus.Announced, MovieStatus.InCinemas, MovieStatus.Released);

        // The one policy with no backing property: days added to the availability date before a movie
        // counts as available. It is per-profile policy rather than a movie fact, so there is no property
        // to point an expression at and it is declared by identifier.
        b.Selection("availabilityDelay", "Availability delay")
         .Days()
         .AtLeast(0);

        // -- search kinds ------------------------------------------------------------------------------
        // Two request paths, so host-side eligibility is a set intersection rather than a silent
        // degradation: a source that cannot be asked by identifier is not eligible for the second kind
        // instead of being asked and quietly matching on title.
        b.Search("movie", "Movie")
         .Requires(SearchTerm.WorkTitle)
         .Admits(SearchTerm.Year, SearchTerm.FreeText)
         .Categories(2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060);

        b.Search("movie-id", "Movie by identifier")
         .Requires(SearchTerm.ExternalIdentifier)
         .Admits(SearchTerm.WorkTitle, SearchTerm.Year)
         .Categories(2000, 2010, 2020, 2030, 2040, 2045, 2050, 2060);

        // -- matching ----------------------------------------------------------------------------------
        // Four ordered key layers. The layering is the algorithm and declared order is semantic: a flat
        // lookup lets an alternative spelling of one film outrank the actual title of another.
        b.Matching
         .Layer("own-title", m => new[] { m.Title, m.OriginalTitle })
         .Layer("roman-rewrite", m => new[] { m.Title }, KeyExpansion.RomanNumerals)
         .Layer("alternative-titles", m => m.AlternateTitles, KeyExpansion.RomanNumerals)
         .Layer("translated-titles", m => m.Translations.Select(t => t.Title), KeyExpansion.RomanNumerals)

         // A missing year is common and harmless; a contradicting one is neither.
         .Agrees(ReadingFact.TitleYear, m => new[] { m.Year, m.SecondaryYear }, Agreement.Accept, 1800)

         .ScopeReplacesSearch()
         .RejectAmbiguity();

        // -- querying ----------------------------------------------------------------------------------
        b.Querying
         .Tier("identifier", "movie-id")
             .RequiresIdentity(IdentifierRole.PrimaryWork)
             .Argument(SearchTerm.ExternalIdentifier, IdentifierRole.PrimaryWork)
             .Argument(SearchTerm.ExternalIdentifier, IdentifierRole.SecondaryWork, omitWhenAbsent: true)
             .FreeText(m => m.Title)
             .CarryAliases()
         .Tier("text", "movie")
             // "Dune" alone is the worst query a movie search can make, and a film with no year has
             // nothing to search for yet.
             .Requires(m => m.Year)
             .Argument(SearchTerm.WorkTitle, m => m.Title)
             .Argument(SearchTerm.Year, m => m.Year)
             .FreeText(m => $"{m.Title} {m.Year}")
             .FanOutPerAlias()
             .CarryAliases()
         .Tier("sweep", "movie")
             .Origins(SearchOrigin.Rss)
             .NoTerms()

         .Alias("display-title", m => new[] { m.Title })
         .Alias("original-title", m => new[] { m.OriginalTitle })
         .Alias("alternative-titles", m => m.AlternateTitles)
         .Alias(
             "translated-titles",
             m => m.Translations.Select(t => t.Title),
             a => a.FilterByAcceptedLanguages().NeverOwnQuery());

        // -- naming ------------------------------------------------------------------------------------
        // Templates still carry a token grammar, and that is correct: a user types these. What changed is
        // that every token in one is derived from a property and checkable against the derived set.
        b.Naming
         .File("{Movie Title} ({Movie Year}) {Quality Full}")

         // The identity stamp Plex and Jellyfin read. {Movie Id} renders the PRIMARY identifier as
         // scheme-value, so a library catalogued anywhere gets that catalog's stamp and the media kind
         // spells no vendor's name. The braces are escaped so they survive as literal output and the
         // optional group elides the stamp for a movie with no identifier.
         .Folder("{Movie TitleThe} ({Movie Year}) <{{{Movie Id}}}>")
         .GroupFolder<Collection>("{Collection TitleThe}")

         .Spine("{root}/[collection-folder/]{folder}")

         // The one selection rule a movie layout has. Both halves of the old two-atom predicate are now
         // implied: whether the user asked is a host-owned option about the axis, and whether the item has
         // a group follows from the axis being a single optional reference.
         .WhenGroupingBy<Collection>("group-by-collection")

         // A disjunction with an exclusivity between its branches, which a per-token "is required" flag
         // could not express at all and which is one line of ordinary code here.
         .RequireInFileTemplate(
             "names-the-movie-or-the-original-file",
             "A file name states a title and a year, or else names the original file.",
             t => ((t.Has(m => m.Title) || t.Has(m => m.OriginalTitle)) && t.Has(m => m.Year))
                  ^ (t.Has(FileFact.SceneName) || t.Has(FileFact.OriginalFileName)))

         .Fallback(m => m.OriginalTitle, FileFact.SceneName, FileFact.OriginalFileName)
         .FallbackForEmptyResult(FileFact.OriginalFileName);

        // -- summaries ---------------------------------------------------------------------------------
        // A title alone names two different films in a remake year, so the headline carries the year.
        b.Summary
         .Headline(m => $"{m.Title} ({m.Year})", maxLength: 256)
         .Body(m => m.Overview, maxLength: 300)
         .Field("Studio", m => m.Studio)
         .Field("Genres", m => m.Genres)
         .Field("Rated", m => m.Certification)
         .Field("Runtime", m => m.Runtime)
         .Field("Rating", m => m.Ratings)
         .Group(m => m.Collection, g => g
             .Headline(c => c.Title)
             .Field("Movies", c => c.MemberCount, SummaryFieldWeight.Primary));

        // -- intent ------------------------------------------------------------------------------------
        // Eleven traversals, seventeen orderings, twenty-two filters and five states derive from the
        // attributes and the CLR types. Only the exceptions are written, and a movie has three.
        b.Intent
         .DefaultBrowse("all", "All movies")
         .Sort(m => m.Title, ascending: true)
         .Hide(m => m.Keywords)
         .StateTone(MovieStatus.InCinemas, StateTone.Attention)
         .StateTone(MovieStatus.Released, StateTone.Positive)
         .StateTone(MovieStatus.Deleted, StateTone.Problem);

        b.Actions
         .Add("search", "Search", Consequence.Costly, ActionScope.Selection).LongRunning()
         .Add("search.missing", "Search for missing movies", Consequence.Costly, ActionScope.Level)
             .LongRunning()
             .Acknowledge("Every wanted movie without a file is searched for. This can be a very large "
                        + "number of requests to every release source at once.")
             .Parameter("unavailableToo", "Include movies that are not available yet")
         .Add("search.cutoffUnmet", "Search for upgrades", Consequence.Costly, ActionScope.Level)
             .LongRunning()
             .Acknowledge("Every movie below its quality cutoff is searched for.")
         .Add("refresh", "Refresh", Consequence.Costly, ActionScope.Selection).LongRunning()
         .Add("rescan", "Rescan folder", Consequence.Safe, ActionScope.Selection).LongRunning()
         .Add("monitor.set", "Set wanted", Consequence.Safe, ActionScope.Selection)
             .Parameter("wanted", "Wanted", defaultValue: true, required: true)
         .Add("collection.monitor", "Set wanted for the whole collection", Consequence.Costly, ActionScope.Item)
             .LongRunning()

             // The closed defect: the old condition had to name a two-state field, and "has a value" about a
             // field that is not two-state was the condition actually wanted and could not be declared.
             .EnabledWhen(m => m.Collection != null)
             .Acknowledge("Every movie in the collection is taken on and marked wanted.")
             .Parameter("wanted", "Wanted", defaultValue: true, required: true)
             .Parameter("addMissing", "Also take on members the library does not hold")
         .Add("availability.set", "Set minimum availability", Consequence.Safe, ActionScope.Selection)

             // The choices, their order and the "at least this far along" comparison all come from the
             // policy, instead of a flat list of alternatives where the domain has a threshold.
             .Parameter(b.Selection(m => m.Status))
         .Add("rename", "Rename files", Consequence.Destructive, ActionScope.Selection)
             .LongRunning()
             .Acknowledge("Files on disk will be moved to new paths. Anything referring to the old paths "
                        + "will need to be updated.")
         .Add("add", "Add a movie", Consequence.Safe, ActionScope.Kind)
             .Parameter("identifier", "Catalog identifier", IdentifierRole.PrimaryWork, required: true)
             .Parameter("monitor", "What to take on", MonitorChoice.Movie)
             .Parameter(b.Selection(m => m.Status))
             .Parameter("searchOnAdd", "Search for it immediately", defaultValue: true)
         .Add("remove", "Remove from library", Consequence.Irreversible, ActionScope.Selection)
             .TypeToConfirm("The movie and its library state are removed. Files on disk are removed only "
                          + "if you ask for that as well, and that part cannot be undone.")
             .Parameter("deleteFiles", "Also delete files from disk")
             .Parameter("exclude", "Refuse to take it on again")
         .Add("exclude", "Refuse this movie", Consequence.Safe, ActionScope.Selection)
             .Acknowledge("The movie will be skipped by every curated list until the refusal is cleared.")
         .Add("exclude.clear", "Stop refusing this movie", Consequence.Safe, ActionScope.Selection)

         // A collection is a grouping axis rather than a level, and the old vocabulary had no member that
         // named one, so the action had to be filed under the whole kind. A group is a type now.
         .AddForGroup<Collection>("collection.refresh", "Refresh collections", Consequence.Costly)
             .LongRunning();

        // -- working surfaces --------------------------------------------------------------------------
        // The row type is the column set, so a column and the proposal that fills it cannot disagree.
        b.Workbench<ManualImportRow>("manual-import", "Manual import")
         .Subject(WorkbenchSubject.LooseFiles)
         .Input("files", "Files")
         .Commit("Import", Consequence.Destructive);

        b.Workbench<ReleaseChoiceRow>("interactive-search", "Interactive search")
         .Subject(WorkbenchSubject.ReleaseCandidates)
         .Input("releases", "Candidates")
         .Commit("Grab", Consequence.Costly);

        // The subject vocabulary has no member for "catalog entries the user may take on", which is the
        // single most common thing anybody does with a movie manager. Library items is the closest of
        // three wrong answers, and typing the rows does not add an enumeration member.
        b.Workbench<CatalogCandidateRow>("add-from-catalog", "Add movies")
         .Subject(WorkbenchSubject.LibraryItems)
         .Input("query", "Search for")
         .Input("identifier", "Or a catalog identifier", IdentifierRole.PrimaryWork)
         .Commit("Add", Consequence.Safe);

        b.Workbench<CatalogCandidateRow>("complete-collection", "Complete a collection")
         .Subject(WorkbenchSubject.LibraryItems)
         .Input("collection", "Collection")
         .Commit("Add", Consequence.Safe);

        // -- recomputations ----------------------------------------------------------------------------
        // Two miniature expression grammars, their parsers and their validation rules cease to exist.
        b.Derives(m => m.ReleaseDate, ReleaseDateOf);
        b.Derives(m => m.Status, m => StatusOf(m, DateOnly.FromDateTime(DateTime.UtcNow)));

        // -- release models, carried as data -----------------------------------------------------------
        // Release-title parsing is regular expressions and typing the model around it does not help. What
        // does improve: there is no code-escape identifier to declare, and the dotted-acronym rewrite is a
        // method rather than a named strategy with a role, a parameter dictionary and a load-time lookup.
        b.Parsing(MoviesParsing.Build())
         .Respace(RespaceDottedAcronym);

        // Evidence is data.
        b.Corpus(MoviesCorpus.Build());

        // Carried, and marked as leaving: the catalog mapping is the whole of what still names a vendor,
        // and it names one because it IS one. It moves to a cataloger plugin of its own, at which point
        // its response map stops being paths into field identifiers and becomes ordinary code.
        b.Catalog(MoviesCatalogDeclaration.Build());
    }

    /// <summary>Recomputes <see cref="Movie.ReleaseDate"/>: the earliest home release, else the cinema date.</summary>
    /// <param name="movie">The movie.</param>
    /// <returns>The date minimum availability is measured against.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="movie"/> is <see langword="null"/>.</exception>
    public static DateOnly? ReleaseDateOf(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);

        return new[] { movie.PhysicalRelease, movie.DigitalRelease }
            .Where(static date => date.HasValue)
            .Min() ?? movie.InCinemas;
    }

    /// <summary>Recomputes <see cref="Movie.Status"/> from the three release dates.</summary>
    /// <param name="movie">The movie.</param>
    /// <param name="today">The day the question is asked on.</param>
    /// <returns>How far through its release sequence the movie has traveled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="movie"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The ninety-day clause is not a defect: a movie with a cinema date and no home date at all must
    /// eventually count as released, or it is never searched for again.
    /// </remarks>
    public static MovieStatus StatusOf(Movie movie, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(movie);

        if (movie.PhysicalRelease <= today || movie.DigitalRelease <= today)
        {
            return MovieStatus.Released;
        }

        if (movie.InCinemas is { } cinema)
        {
            var noHomeRelease = movie.PhysicalRelease is null && movie.DigitalRelease is null;

            return noHomeRelease && today > cinema.AddDays(TheatricalWindowDays)
                ? MovieStatus.Released
                : today > cinema ? MovieStatus.InCinemas : MovieStatus.Announced;
        }

        return movie.PhysicalRelease is null && movie.DigitalRelease is null
            ? MovieStatus.Tba
            : MovieStatus.Announced;
    }

    /// <summary>Recomputes <see cref="Movie.SecondaryYear"/> from a catalog premiere date.</summary>
    /// <param name="catalogYear">The year the catalog states.</param>
    /// <param name="premiere">The premiere date the catalog states.</param>
    /// <returns>The second year, or <see langword="null"/> when the two agree.</returns>
    /// <remarks>
    /// The row the review singled out as failing the design's own acceptance test — "the moment a
    /// declaration needs an <c>if</c>, the design has failed at that point". It needed one because it is
    /// one.
    /// </remarks>
    public static int? SecondaryYearOf(int? catalogYear, DateOnly? premiere) =>
        premiere is { } moment && moment.Year != catalogYear ? moment.Year : null;

    /// <summary>Rejoins a dotted run: an acronym keeps its dots and an ordinary run loses them.</summary>
    /// <param name="dottedRun">The dotted run read out of a release title.</param>
    /// <returns>The run, respaced when its dots are separators rather than an abbreviation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dottedRun"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// What used to be a named strategy with a role identifier, a parameter dictionary, a requirement row,
    /// a host vocabulary entry and a load-time resolution rule — for a rewrite that is three lines. The
    /// cost is real and is stated rather than buried: a typed media kind has code, so the assembly is no
    /// longer eligible for unload the moment its declaration is captured, and the network privilege is no
    /// longer structurally ungrantable. The capability gate goes back to being enforced by the manifest and
    /// the loader rather than by the absence of an instruction pointer.
    /// </remarks>
    public static string RespaceDottedAcronym(string dottedRun)
    {
        ArgumentNullException.ThrowIfNull(dottedRun);

        var parts = dottedRun.Split('.');

        // An abbreviation is a run of single characters and keeps its dots: "S.W.A.T", "U.S.A". Anything
        // with a whole word in it is dot-separated text whose dots are spaces — "Mission.Impossible.3",
        // and also "A.Team" and "Dr.No", whose leading part is an ordinary word rather than an initial. A
        // run made of nothing but those ordinary words is text too.
        var isAbbreviation = parts.Length > 1
            && parts.All(static part => part.Length == 1)
            && !parts.All(static part => ExceptionWords.Contains(part, StringComparer.OrdinalIgnoreCase));

        return isAbbreviation ? dottedRun : dottedRun.Replace('.', ' ');
    }
}
