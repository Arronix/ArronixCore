using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

// Every contract the fixture is written against is experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0016
#pragma warning disable ARX0019
#pragma warning disable ARX0020

namespace Arronix.Host.Tests.TypedMedia;

/// <summary>How far through its release sequence a work has travelled.</summary>
/// <remarks>
/// The order is the enumeration's, which is the whole point: a threshold over it is an ordinary comparison
/// the compiler checks rather than a rank function carried where no consumer can read it.
/// </remarks>
internal enum WorkStage
{
    Withdrawn = -1,
    Rumored = 0,
    Announced = 1,
    Previewing = 2,
    Published = 3
}

/// <summary>Why a work carries an alternate title.</summary>
internal enum AlternateTitleRole
{
    Release = 0,
    Translation = 1
}

/// <summary>A title a work is also known by. A repeated tuple, so one composite field.</summary>
internal sealed record AlternateTitle(string Title, AlternateTitleRole Role, Language? Language = null);

/// <summary>One score from one source, from whichever cataloger supplied it.</summary>
internal sealed record Score(string Source, double Value, long? Votes = null);

/// <summary>A collection a work belongs to: monitorable, independently lived, with metadata of its own.</summary>
internal sealed class WorkCollection : IMediaGroup<Work>
{
    [Identity]
    public required MediaItemId Id { get; init; }

    [Title, Searchable]
    public required string Title { get; init; }

    [Searchable, Multiline]
    public string? Overview { get; init; }

    [Artwork]
    public ArtworkSet Images { get; init; } = ArtworkSet.Empty;

    [Count, Sortable, Prominence(Prominence.Secondary)]
    public int MemberCount { get; init; }

    public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;
}

/// <summary>
/// The entity the derivation tests are written against.
/// </summary>
/// <remarks>
/// Deliberately media-neutral and deliberately complete. It exercises every derivation rule the design
/// names — a title and its transforms, an ordered status enumeration, a composite list, an external-identity
/// set, an artwork set, a group reference, a derived-and-queryable property, and one property that is not a
/// field at all — without being any real media kind, so a test cannot quietly assert something about movies
/// instead of about the rule.
/// </remarks>
internal sealed class Work : IMediaItem
{
    [Identity]
    public required MediaItemId Id { get; init; }

    public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

    [Title, Searchable, Editable, Prominence(Prominence.Primary)]
    public required string Title { get; init; }

    [Searchable, Disambiguation]
    public string? OriginalTitle { get; init; }

    [Filterable, Groupable]
    public Language? OriginalLanguage { get; init; }

    [Searchable, Editable]
    public IReadOnlyList<AlternateTitle> AlternateTitles { get; init; } = [];

    [Searchable, Disambiguation, Prominence(Prominence.Primary)]
    public int? Year { get; init; }

    [Timestamp]
    public DateOnly? PreviewedOn { get; init; }

    [Timestamp]
    public DateOnly? PublishedOn { get; init; }

    [Derived, Timestamp, Prominence(Prominence.Secondary)]
    public DateOnly? ReleaseDate { get; init; }

    [Derived, Status, Prominence(Prominence.Secondary)]
    public WorkStage Stage { get; init; }

    [Searchable, Multiline]
    public string? Overview { get; init; }

    [Sortable, Filterable]
    public TimeSpan? Runtime { get; init; }

    [Filterable, Groupable, Prominence(Prominence.Secondary)]
    public IReadOnlyList<string> Genres { get; init; } = [];

    [Filterable, Prominence(Prominence.Diagnostic)]
    public IReadOnlyList<string> Keywords { get; init; } = [];

    [Size, Sortable]
    public long ShippedBytes { get; init; }

    [Artwork]
    public ArtworkSet Images { get; init; } = ArtworkSet.Empty;

    [Sortable, Filterable]
    public IReadOnlyList<Score> Scores { get; init; } = [];

    [Groupable, Prominence(Prominence.Secondary)]
    public WorkCollection? Collection { get; init; }

    /// <summary>A helper the entity happens to expose, which is not a field of it.</summary>
    /// <remarks>
    /// Written out in full because a test project also has an <c>Ignore</c> attribute, and the two collide.
    /// A media-kind plugin references no test framework, so it writes the short form.
    /// </remarks>
    [Arronix.Abstractions.Media.Ignore]
    public bool IsPublished => Stage == WorkStage.Published;
}

/// <summary>One loose file being assigned to a work. The row type is the column set.</summary>
internal sealed record ImportRow
{
    [Prominence(Prominence.Primary)]
    public required string Path { get; init; }

    [Editable, Prominence(Prominence.Primary)]
    public Work? Work { get; init; }

    [Size, Prominence(Prominence.Secondary)]
    public long Size { get; init; }
}

/// <summary>The media type the derivation tests replay.</summary>
internal sealed class Works : IMediaType<Work>
{
    /// <inheritdoc />
    public static MediaKindId Kind { get; } = MediaKindId.FromString("works");

    /// <inheritdoc />
    public static void Configure(IMediaTypeBuilder<Work> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.Named("Work", "Works");
        b.Files.OnePerItem();

        b.Format("video", "Video")
            .Extensions(".mkv", ".mp4")
            .Ladder(Ladder, Unknown)
            .Facet("edition", "Edition", TechnicalFacetCase.TitleCaseWithExceptions, ["IMAX"], true);

        b.Identity(w => w.ExternalIds)
            .Requires(IdentifierRole.PrimaryWork)
            .Admits(IdentifierRole.SecondaryWork)
            .SupportsRedirects();

        b.Group(w => w.Collection)
            .Named("Collection", "Collections")
            .Monitorable()
            .DiscoverySource()
            .Independent();

        b.Selection(w => w.Stage)
            .Named("Minimum availability")
            .AtLeast(WorkStage.Published)
            .Offering(WorkStage.Rumored, WorkStage.Announced, WorkStage.Previewing, WorkStage.Published);

        b.Selection("availabilityDelay", "Availability delay").Days().AtLeast(0);

        b.Search("work", "Work")
            .Requires(SearchTerm.WorkTitle)
            .Admits(SearchTerm.Year, SearchTerm.FreeText)
            .Categories(2000, 2010);

        b.Search("work-id", "Work by identifier")
            .Requires(SearchTerm.ExternalIdentifier)
            .Admits(SearchTerm.WorkTitle)
            .Categories(2000);

        b.Matching
            .Layer("own-title", w => new[] { w.Title, w.OriginalTitle })
            .Layer("roman-rewrite", w => new[] { w.Title }, KeyExpansion.RomanNumerals)
            .Agrees(ReadingFact.TitleYear, w => new int?[] { w.Year }, Agreement.Accept, 1800)
            .ScopeReplacesSearch()
            .RejectAmbiguity();

        b.Querying
            .Tier("identifier", "work-id")
                .RequiresIdentity(IdentifierRole.PrimaryWork)
                .Argument(SearchTerm.ExternalIdentifier, IdentifierRole.PrimaryWork)
                .Argument(SearchTerm.ExternalIdentifier, IdentifierRole.SecondaryWork, omitWhenAbsent: true)
                .FreeText(w => w.Title)
                .CarryAliases()
            .Tier("text", "work")
                .Requires(w => w.Year)
                .FreeText(w => $"{w.Title} {w.Year}")
                .FanOutPerAlias()
                .CarryAliases()
            .Tier("sweep", "work")
                .Origins(SearchOrigin.Rss)
                .NoTerms()
            .Alias("display-title", w => new[] { w.Title })
            .Alias(
                "translated-titles",
                w => w.AlternateTitles
                    .Where(t => t.Role == AlternateTitleRole.Translation)
                    .Select(t => t.Title),
                a => a.FilterByAcceptedLanguages().NeverOwnQuery());

        b.Naming
            .File("{Work Title} ({Work Year})")
            .Folder("{Work TitleThe} ({Work Year}) <{{{Work Id}}}>")
            .GroupFolder<WorkCollection>("{WorkCollection TitleThe}")
            .Spine("{root}/[workCollection-folder/]{folder}")
            .WhenGroupingBy<WorkCollection>("group-by-collection")
            .RequireInFileTemplate(
                "names-the-work",
                "A file template must name either the title and the year, or the original title.",
                t => (t.Has(w => w.Title) && t.Has(w => w.Year)) ^ t.Has(w => w.OriginalTitle))
            .Fallback(w => w.OriginalTitle, FileFact.SceneName, FileFact.OriginalFileName)
            .FallbackForEmptyResult(FileFact.OriginalFileName);

        b.Summary
            .Headline(w => $"{w.Title} ({w.Year})", maxLength: 200)
            .Body(w => w.Overview, maxLength: 280)
            .Field("Runtime", w => w.Runtime)
            .Group(w => w.Collection, g => g
                .Headline(c => c.Title)
                .Field("Works", c => c.MemberCount));

        b.Intent
            .DefaultBrowse("all", "All works")
            .Sort(w => w.Title, ascending: true)
            .Hide(w => w.Keywords)
            .StateTone(WorkStage.Previewing, StateTone.Attention)
            .StateTone(WorkStage.Published, StateTone.Positive)
            .StateTone(WorkStage.Withdrawn, StateTone.Problem);

        b.Actions
            .Add("search", "Search", Consequence.Costly, ActionScope.Selection).LongRunning()
            .Add("collection.monitor", "Set wanted for the collection", Consequence.Costly, ActionScope.Item)
                .EnabledWhen(w => w.Collection != null)
                .Acknowledge("Every work in the collection is taken on.")
                .Parameter("wanted", "Wanted", defaultValue: true)
            .Add("add", "Add a work", Consequence.Safe, ActionScope.Kind)
                .Parameter("identifier", "Catalog identifier", IdentifierRole.PrimaryWork, required: true)
                .Parameter(b.Selection(w => w.Stage))
            .AddForGroup<WorkCollection>("collection.refresh", "Refresh collections", Consequence.Costly);

        b.Workbench<ImportRow>("manual-import", "Manual import")
            .Subject(WorkbenchSubject.LooseFiles)
            .Input("files", "Files")
            .Commit("Import", Consequence.Destructive);

        b.Quality.IgnoreStatedResolutionFor("cam", "ts").FallbackRoundUp();

        b.Derives(w => w.ReleaseDate, ReleaseDateOf);
        b.Derives(w => w.Stage, StageOf);

        b.Parsing(Parsing).Respace(static dotted => dotted.Replace('.', ' '));
        b.Corpus([]);
    }

    /// <summary>Recomputes the stored, queryable release date.</summary>
    internal static DateOnly? ReleaseDateOf(Work work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return work.PublishedOn ?? work.PreviewedOn;
    }

    /// <summary>Recomputes the stored, queryable stage.</summary>
    internal static WorkStage StageOf(Work work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return work.PublishedOn is not null
            ? WorkStage.Published
            : work.PreviewedOn is not null ? WorkStage.Previewing : WorkStage.Rumored;
    }

    internal static IReadOnlyList<QualityTier> Ladder { get; } =
    [
        new QualityTier("SD", 1),
        new QualityTier("HD", 2)
    ];

    internal static QualityTier Unknown { get; } = new("Unknown", 0);

    private static ParseDeclaration Parsing { get; } = new()
    {
        TitlePatterns =
        [
            new TitlePattern
            {
                PatternId = "title-year",
                Regex = @"^(?<title>.+?)[. ](?<year>\d{4})",
                Captures = []
            }
        ],
        RungResolution = new RungResolutionTable { Rules = [], UnknownTierId = "Unknown" }
    };
}
