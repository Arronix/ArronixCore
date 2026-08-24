
using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Releases;
using Arronix.Abstractions.Shape;
using Arronix.Format.Video;
using Arronix.Media.Movies;
using Arronix.Plugin.Movies.Definition;

namespace Arronix.Plugin.Movies;

/// <summary>Defines movie structure, acquisition, matching, naming, and user interactions.</summary>
public sealed partial class Movies() :
    MediaType<Movie, ReleaseTarget<Movie>, Release<Video>, MovieReleaseParser>(
        MediaKindId.FromString("movies"),
        "Movie",
        "Movies",
        formats: [new FormatUse<Video>(VideoFormat.Definition)],
        availability: new OrderedSelectionDefinition<Movie, MovieReleaseStage>(
            movie => movie.Status,
            "Minimum availability",
            MovieReleaseStage.Released))
{
    /// <summary>Words that should not be treated as dotted initials.</summary>
    private static readonly string[] ExceptionWords = ["a", "dr"];

    public override IdentityDefinition Identity { get; } = new()
    {
        RequiredRoles = [IdentifierRole.PrimaryWork],
        AdmittedRoles = [IdentifierRole.SecondaryWork]
    };

    public override IReadOnlyList<IGroupDefinition<Movie>> Groups { get; } =
    [
        new GroupDefinition<Movie, MediaCollection<Movie>>(
            movie => movie.Collections,
            "Collection",
            "Collections")
        {
            IsMonitorable = true,
            IsDiscoverySource = true,
            Lifetime = GroupLifetime.Independent
        }
    ];

    public override IReadOnlyList<ISelectionDefinition<Movie>> AdditionalSelections { get; } =
    [
        new ThresholdSelectionDefinition<Movie>(
            "availabilityDelay",
            "Availability delay",
            "days",
            ThresholdDirection.AtLeast,
            0)
    ];

    public override IReadOnlyList<SearchDefinition> Searches { get; } =
    [
        new(
            "movie-id",
            "Movie by identifier",
            [SearchTerm.ExternalIdentifier],
            [SearchTerm.WorkTitle, SearchTerm.Year]),
        new(
            "movie",
            "Movie",
            [SearchTerm.WorkTitle],
            [SearchTerm.Year, SearchTerm.FreeText])
    ];

    public override MatchingDefinition<Movie> Matching { get; } = new()
    {
        Layers =
        [
            new("own-title", movie => new[] { movie.Title, movie.OriginalTitle }),
            new("roman-rewrite", movie => new[] { movie.Title }, KeyExpansion.RomanNumerals),
            new("alternative-titles", movie => movie.AlternateTitles, KeyExpansion.RomanNumerals),
            new(
                "translated-titles",
                movie => movie.Translations.Select(text => text.Value.Title),
                KeyExpansion.RomanNumerals)
        ],
        Agreements =
        [
            new MatchAgreement<Movie, int>(
                ReadingFact.TitleYear,
                movie => new[] { movie.Year, movie.SecondaryYear },
                Agreement.Accept,
                1800)
        ],
        ScopeReplacesSearch = true,
        Ambiguity = AmbiguityPolicy.Reject
    };

    public override ReleasePolicy<Release<Video>> ReleasePolicy { get; } =
        ReleasePolicy<Release<Video>>.Compile(policy =>
            VideoReleasePolicyDefaults.Configure(policy, release => release.Representation));

    public override QueryDefinition<Movie> Querying { get; } = new()
    {
        Tiers =
        [
            new("identifier", "movie-id")
            {
                RequiredIdentityRoles = [IdentifierRole.PrimaryWork],
                Arguments =
                [
                    new QueryIdentityArgument<Movie>(SearchTerm.ExternalIdentifier, IdentifierRole.PrimaryWork),
                    new QueryIdentityArgument<Movie>(SearchTerm.ExternalIdentifier, IdentifierRole.SecondaryWork, true)
                ],
                FreeText = movie => movie.Title,
                CarryAliases = true
            },
            new("text", "movie")
            {
                Requirements = [new ItemPropertyDefinition<Movie, int?>(movie => movie.Year)],
                Arguments =
                [
                    new QueryPropertyArgument<Movie, string>(SearchTerm.WorkTitle, movie => movie.Title),
                    new QueryPropertyArgument<Movie, int?>(SearchTerm.Year, movie => movie.Year)
                ],
                FreeText = movie => $"{movie.Title} {movie.Year}",
                FanOutPerAlias = true,
                CarryAliases = true
            },
            new("sweep", "movie") { Origins = [SearchOrigin.Rss], HasNoTerms = true }
        ],
        Aliases =
        [
            new("display-title", movie => new[] { movie.Title }),
            new("original-title", movie => new[] { movie.OriginalTitle }),
            new("alternative-titles", movie => movie.AlternateTitles),
            new("translated-titles", movie => movie.Translations.Select(text => text.Value.Title))
            {
                FilterByAcceptedLanguages = true,
                NeverOwnQuery = true
            }
        ]
    };

    public override NamingDefinition<Movie> Naming { get; } = new()
    {
        FileTemplate = "{Movie Title} ({Movie Year})",
        FolderTemplate = "{Movie TitleThe} ({Movie Year}) <{{{Movie Id}}}>",
        GroupFolders =
            [new GroupNamingDefinition<Movie, MediaCollection<Movie>>("{Collection TitleThe}")],
        FolderSpine = "{root}/[collection-folder/]{folder}",
        GroupSelections =
            [new GroupNamingSelection<Movie, MediaCollection<Movie>>("group-by-collection")],
        Requirements =
        [
            new(
                "names-the-movie-or-the-original-file",
                "A file name states a title and a year, or else names the original file.",
                facts => ((facts.Has(movie => movie.Title) || facts.Has(movie => movie.OriginalTitle))
                          && facts.Has(movie => movie.Year))
                         ^ (facts.Has(FileFact.SceneName) || facts.Has(FileFact.OriginalFileName)))
        ],
        Fallbacks =
        [
            new TokenFallbackDefinition<Movie, string?>(
                movie => movie.OriginalTitle,
                [FileFact.SceneName, FileFact.OriginalFileName])
        ],
        EmptyResultFallback = FileFact.OriginalFileName
    };

    public override SummaryDefinition<Movie> Summary { get; } = new()
    {
        Headline = movie => $"{movie.Title} ({movie.Year})",
        Body = movie => movie.Overview,
        Fields =
        [
            new("Studio", movie => movie.Organization),
            new("Genres", movie => movie.Genres),
            new("Rated", movie => movie.Certification),
            new("Runtime", movie => movie.Runtime),
            new("Rating", movie => movie.Ratings)
        ],
        Groups =
        [
            new GroupSummaryDefinition<Movie, MediaCollection<Movie>>(
                collection => collection.Title,
                [
                    new GroupSummaryFieldDefinition<MediaCollection<Movie>, int>(
                        "Movies",
                        collection => collection.MemberCount,
                        SummaryFieldWeight.Primary)
                ])
        ]
    };

    public override IntentDefinition<Movie> Intent { get; } = new()
    {
        DefaultBrowseId = "all",
        DefaultBrowseName = "All movies",
        Sorts = [new SortDefinition<Movie, string>(movie => movie.Title, true)],
        HiddenBrowseFields = [new ItemPropertyDefinition<Movie, IReadOnlyList<string>>(movie => movie.Keywords)],
        StateTones =
        [
            new StateToneDefinition<MovieReleaseStage>(MovieReleaseStage.InCinemas, StateTone.Attention),
            new StateToneDefinition<MovieReleaseStage>(MovieReleaseStage.Released, StateTone.Positive)
        ]
    };

    public override IReadOnlyList<IWorkbenchDefinition<Movie>> Workbenches { get; } =
    [
        new WorkbenchDefinition<Movie, ManualImportRow<ReleaseTarget<Movie>, Release<Video>>>(
            "manual-import",
            "Manual import")
        {
            Subject = WorkbenchSubject.LooseFiles,
            Inputs = [new("files", "Files")],
            CommitLabel = "Import",
            CommitConsequence = Consequence.Destructive
        },
        new WorkbenchDefinition<Movie, ReleaseChoiceRow<ReleaseTarget<Movie>, Release<Video>>>(
            "interactive-search",
            "Interactive search")
        {
            Subject = WorkbenchSubject.ReleaseCandidates,
            Inputs = [new("releases", "Candidates")],
            CommitLabel = "Grab",
            CommitConsequence = Consequence.Costly
        },
        new WorkbenchDefinition<Movie, CatalogCandidateRow<Movie>>("add-from-catalog", "Add movies")
        {
            Subject = WorkbenchSubject.CatalogCandidates,
            Inputs =
            [
                new("query", "Search for"),
                new("identifier", "Or a catalog identifier", IdentifierRole.PrimaryWork)
            ],
            CommitLabel = "Add"
        },
        new WorkbenchDefinition<Movie, CatalogCandidateRow<Movie>>("complete-collection", "Complete a collection")
        {
            Subject = WorkbenchSubject.CatalogCandidates,
            Inputs = [new("collection", "Collection")],
            CommitLabel = "Add"
        }
    ];

    /// <summary>Returns a distinct premiere year when it differs from the catalog year.</summary>
    /// <param name="catalogYear">The year the catalog states.</param>
    /// <param name="premiere">The premiere date the catalog states.</param>
    /// <returns>The premiere year when it differs; otherwise <see langword="null"/>.</returns>
    public static int? SecondaryYearOf(int? catalogYear, DateOnly? premiere) =>
        premiere is { } moment && moment.Year != catalogYear ? moment.Year : null;

    /// <summary>Preserves dotted initials and converts dots between words to spaces.</summary>
    /// <param name="dottedRun">The dotted run read out of a release title.</param>
    /// <returns>The normalized text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dottedRun"/> is <see langword="null"/>.</exception>
    public static string RespaceDottedAcronym(string dottedRun)
    {
        ArgumentNullException.ThrowIfNull(dottedRun);

        var parts = dottedRun.Split('.');
        var result = new List<string>(parts.Length);

        for (var index = 0; index < parts.Length;)
        {
            var runEnd = index;
            while (runEnd < parts.Length && IsInitial(parts[runEnd]))
            {
                runEnd++;
            }

            var runLength = runEnd - index;
            var isAcronym = runLength >= 2
                && !parts[index..runEnd].All(
                    static part => ExceptionWords.Contains(part, StringComparer.OrdinalIgnoreCase));

            if (isAcronym)
            {
                var acronym = string.Join('.', parts[index..runEnd]);
                if (runEnd < parts.Length)
                {
                    acronym += ".";
                }

                result.Add(acronym);
                index = runEnd;
                continue;
            }

            if (parts[index].Length > 0)
            {
                result.Add(parts[index]);
            }

            index++;
        }

        return string.Join(' ', result);
    }

    private static bool IsInitial(string value) =>
        value.Length == 1 && char.IsLetter(value[0]);
}
