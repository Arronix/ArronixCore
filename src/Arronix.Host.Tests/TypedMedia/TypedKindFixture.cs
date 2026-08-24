using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Releases;
using Arronix.Abstractions.Shape;


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
    public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

    [Searchable]
    public required string Title { get; init; }

    public Language? TitleLanguage { get; init; }

    [Searchable, Multiline]
    public string? Overview { get; init; }

    public ArtworkSet Artwork { get; init; } = ArtworkSet.Empty;

    [Count, Sortable, Prominence(Prominence.Secondary)]
    public int MemberCount { get; init; }

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
    public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

    public CatalogRecordState CatalogState { get; init; }

    [Searchable, Editable, Prominence(Prominence.Primary)]
    public required string Title { get; init; }

    public Language? TitleLanguage { get; init; }

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

    public ArtworkSet Artwork { get; init; } = ArtworkSet.Empty;

    [Sortable, Filterable]
    public IReadOnlyList<Score> Scores { get; init; } = [];

    [Groupable, Prominence(Prominence.Secondary)]
    public IReadOnlyList<WorkCollection> Collections { get; init; } = [];

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
internal sealed class WorkTarget : IReleaseTarget;

internal sealed record WorkRelease(
    string Title = "Work",
    int? Year = null,
    string? Edition = null) : IRelease;

internal sealed class WorkParser : IReleaseParser<WorkRelease>
{
    public static ReleaseParseResult<WorkRelease> Parse(ReleaseParseContext context) =>
        ReleaseParseResult<WorkRelease>.Accepted(new WorkRelease(context.Text));
}

internal sealed class WorkRepresentation : IRepresentation;

internal static class WorkFormat
{
    internal static FormatFamilyDefinition<WorkRepresentation> Definition { get; } = new()
    {
        Id = "work",
        Name = "Work",
        FileExtensions = [".mkv", ".mp4"]
    };
}

/// <summary>A malformed definition used to prove that required format composition fails at construction.</summary>
internal sealed partial class EmptyFormatWorks() : MediaType<Work, WorkTarget, WorkRelease, WorkParser>(
    MediaKindId.FromString("empty-format-works"),
    "Empty-format work",
    "Empty-format works",
    formats: [],
    availability: new OrderedSelectionDefinition<Work, WorkStage>(
        work => work.Stage,
        "Minimum availability",
        WorkStage.Published))
{
}

/// <summary>
/// A second, otherwise valid media kind closed over the same item type as <see cref="Works"/>.
/// </summary>
/// <remarks>
/// It exists to be refused. An item type is how the platform finds a kind — a paired cataloger resolves to
/// one that way, and so does identifier recognition — so two kinds owning one type is an ambiguity, and a
/// fixture that can produce it is what proves the refusal is real rather than assumed.
/// </remarks>
internal sealed partial class RivalWorks() : MediaType<Work, WorkTarget, WorkRelease, WorkParser>(
    Id,
    "Rival work",
    "Rival works",
    formats: [new FormatUse<WorkRepresentation>(WorkFormat.Definition)],
    availability: new OrderedSelectionDefinition<Work, WorkStage>(
        work => work.Stage,
        "Minimum availability",
        WorkStage.Published))
{
    internal static MediaKindId Id { get; } = MediaKindId.FromString("rival-works");

    public override IReadOnlyList<SearchDefinition> Searches { get; } =
    [
        new("work", "Work", [SearchTerm.WorkTitle], [SearchTerm.FreeText])
    ];

    // The smallest declarations the shape gate demands, so this kind is refused for owning another kind's
    // item type rather than for being incomplete.
    public override MatchingDefinition<Work> Matching { get; } = new()
    {
        Layers = [new("own-title", work => new[] { work.Title })],
        Ambiguity = AmbiguityPolicy.Reject
    };

    public override QueryDefinition<Work> Querying { get; } = new()
    {
        Tiers = [new("sweep", "work") { Origins = [SearchOrigin.Rss], HasNoTerms = true }]
    };
}

internal sealed partial class Works() : MediaType<Work, WorkTarget, WorkRelease, WorkParser>(
    Id,
    "Work",
    "Works",
    formats: [new FormatUse<WorkRepresentation>(WorkFormat.Definition)],
    availability: new OrderedSelectionDefinition<Work, WorkStage>(
        work => work.Stage,
        "Minimum availability",
        WorkStage.Published)
    {
        OfferedValues =
            [WorkStage.Rumored, WorkStage.Announced, WorkStage.Previewing, WorkStage.Published]
    })
{
    internal static MediaKindId Id { get; } = MediaKindId.FromString("works");

    public override IdentityDefinition Identity { get; } = new()
    {
        RequiredRoles = [IdentifierRole.PrimaryWork],
        AdmittedRoles = [IdentifierRole.SecondaryWork]
    };

    public override IReadOnlyList<IGroupDefinition<Work>> Groups { get; } =
    [
        new GroupDefinition<Work, WorkCollection>(work => work.Collections, "Collection", "Collections")
        {
            IsMonitorable = true,
            IsDiscoverySource = true,
            Lifetime = GroupLifetime.Independent
        }
    ];

    public override IReadOnlyList<ISelectionDefinition<Work>> AdditionalSelections { get; } =
    [
        new ThresholdSelectionDefinition<Work>(
            "availabilityDelay",
            "Availability delay",
            "days",
            ThresholdDirection.AtLeast,
            0)
    ];

    public override IReadOnlyList<SearchDefinition> Searches { get; } =
    [
        new("work", "Work", [SearchTerm.WorkTitle], [SearchTerm.Year, SearchTerm.FreeText]),
        new("work-id", "Work by identifier", [SearchTerm.ExternalIdentifier], [SearchTerm.WorkTitle])
    ];

    public override MatchingDefinition<Work> Matching { get; } = new()
    {
        Layers =
        [
            new("own-title", work => new[] { work.Title, work.OriginalTitle }),
            new("roman-rewrite", work => new[] { work.Title }, KeyExpansion.RomanNumerals)
        ],
        Agreements =
        [
            new MatchAgreement<Work, int>(
                ReadingFact.TitleYear,
                work => new int?[] { work.Year },
                Agreement.Accept,
                1800)
        ],
        ScopeReplacesSearch = true,
        Ambiguity = AmbiguityPolicy.Reject
    };

    public override ReleasePolicy<WorkRelease> ReleasePolicy { get; } =
        ReleasePolicy<WorkRelease>.Compile(policy =>
            policy.Require(static _ => true, "fixture requirement"));

    public override QueryDefinition<Work> Querying { get; } = new()
    {
        Tiers =
        [
            new("identifier", "work-id")
            {
                RequiredIdentityRoles = [IdentifierRole.PrimaryWork],
                Arguments =
                [
                    new QueryIdentityArgument<Work>(
                        SearchTerm.ExternalIdentifier,
                        IdentifierRole.PrimaryWork),
                    new QueryIdentityArgument<Work>(
                        SearchTerm.ExternalIdentifier,
                        IdentifierRole.SecondaryWork,
                        true)
                ],
                FreeText = work => work.Title,
                CarryAliases = true
            },
            new("text", "work")
            {
                Requirements = [new ItemPropertyDefinition<Work, int?>(work => work.Year)],
                FreeText = work => $"{work.Title} {work.Year}",
                FanOutPerAlias = true,
                CarryAliases = true
            },
            new("sweep", "work") { Origins = [SearchOrigin.Rss], HasNoTerms = true }
        ],
        Aliases =
        [
            new("display-title", work => new[] { work.Title }),
            new(
                "translated-titles",
                work => work.AlternateTitles
                    .Where(title => title.Role == AlternateTitleRole.Translation)
                    .Select(title => title.Title))
            {
                FilterByAcceptedLanguages = true,
                NeverOwnQuery = true
            }
        ]
    };

    public override NamingDefinition<Work> Naming { get; } = new()
    {
        FileTemplate = "{Work Title} ({Work Year})",
        FolderTemplate = "{Work TitleThe} ({Work Year}) <{{{Work Id}}}>",
        GroupFolders = [new GroupNamingDefinition<Work, WorkCollection>("{Collection TitleThe}")],
        FolderSpine = "{root}/[collection-folder/]{folder}",
        GroupSelections = [new GroupNamingSelection<Work, WorkCollection>("group-by-collection")],
        Requirements =
        [
            new(
                "names-the-work",
                "A file template must name either the title and the year, or the original title.",
                facts => (facts.Has(work => work.Title) && facts.Has(work => work.Year))
                    ^ facts.Has(work => work.OriginalTitle))
        ],
        Fallbacks =
        [
            new TokenFallbackDefinition<Work, string?>(
                work => work.OriginalTitle,
                [FileFact.SceneName, FileFact.OriginalFileName])
        ],
        EmptyResultFallback = FileFact.OriginalFileName
    };

    public override SummaryDefinition<Work> Summary { get; } = new()
    {
        Headline = work => $"{work.Title} ({work.Year})",
        HeadlineMaxLength = 200,
        Body = work => work.Overview,
        BodyMaxLength = 280,
        Fields = [new("Runtime", work => work.Runtime)],
        Groups =
        [
            new GroupSummaryDefinition<Work, WorkCollection>(
                collection => collection.Title,
                [new GroupSummaryFieldDefinition<WorkCollection, int>("Works", collection => collection.MemberCount)])
        ]
    };

    public override IntentDefinition<Work> Intent { get; } = new()
    {
        DefaultBrowseId = "all",
        DefaultBrowseName = "All works",
        Sorts = [new SortDefinition<Work, string>(work => work.Title, true)],
        HiddenBrowseFields =
            [new ItemPropertyDefinition<Work, IReadOnlyList<string>>(work => work.Keywords)],
        StateTones =
        [
            new StateToneDefinition<WorkStage>(WorkStage.Previewing, StateTone.Attention),
            new StateToneDefinition<WorkStage>(WorkStage.Published, StateTone.Positive),
            new StateToneDefinition<WorkStage>(WorkStage.Withdrawn, StateTone.Problem)
        ]
    };

    public override IReadOnlyList<IWorkbenchDefinition<Work>> Workbenches { get; } =
    [
        new WorkbenchDefinition<Work, ImportRow>("manual-import", "Manual import")
        {
            Subject = WorkbenchSubject.LooseFiles,
            Inputs = [new("files", "Files")],
            CommitLabel = "Import",
            CommitConsequence = Consequence.Destructive
        }
    ];

    public override IReadOnlyList<IDerivationDefinition<Work>> Derivations { get; } =
    [
        new DerivationDefinition<Work, DateOnly?>(work => work.ReleaseDate, ReleaseDateOf),
        new DerivationDefinition<Work, WorkStage>(work => work.Stage, StageOf)
    ];

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

}
