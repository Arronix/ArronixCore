using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>The common, directly usable shape of a catalogued media item.</summary>
/// <typeparam name="TReleaseTimeline">The media-owned release timeline.</typeparam>
/// <typeparam name="TReleaseStage">The media-owned release-stage enumeration.</typeparam>
/// <remarks>
/// This two-argument form is the complete item type when a media kind adds no further item facts. A kind
/// which genuinely extends the item uses the three-argument form so collection membership retains the
/// derived item type.
/// </remarks>
public class MediaItem<TReleaseTimeline, TReleaseStage>
    : MediaItem<MediaItem<TReleaseTimeline, TReleaseStage>, TReleaseTimeline, TReleaseStage>
    where TReleaseTimeline : IReleaseTimeline<TReleaseStage>
    where TReleaseStage : struct, Enum;

/// <summary>The common shape of a catalogued media item which may be extended with media-owned facts.</summary>
/// <typeparam name="TItem">The exact item type.</typeparam>
/// <typeparam name="TReleaseTimeline">The media-owned release timeline.</typeparam>
/// <typeparam name="TReleaseStage">The media-owned release-stage enumeration.</typeparam>
public class MediaItem<TItem, TReleaseTimeline, TReleaseStage>
    : IMediaItem<TItem, TReleaseStage>, IHasRatings, IHasCertification
    where TItem : class, IMediaItem<TItem, TReleaseStage>
    where TReleaseTimeline : IReleaseTimeline<TReleaseStage>
    where TReleaseStage : struct, Enum
{
    /// <inheritdoc />
    public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

    /// <inheritdoc />
    [Searchable, Filterable, Editable, Prominence(Prominence.Primary)]
    [Display(Description = "The item's configured display title.", Example = "Example Title")]
    public required string Title { get; init; }

    /// <inheritdoc />
    [Filterable, Groupable]
    public Language? TitleLanguage { get; init; }

    /// <summary>Gets the title in the language in which the item was originally published.</summary>
    [Searchable, Filterable, Disambiguation]
    [Display(Description = "The title in the language in which the item was originally published.",
             Example = "Original Title")]
    public string? OriginalTitle { get; init; }

    /// <summary>Gets the language in which the item was originally published.</summary>
    [Filterable, Groupable]
    public Language? OriginalLanguage { get; init; }

    /// <summary>Gets alternate titles under which releases may appear.</summary>
    [Searchable, Editable]
    public IReadOnlyList<string> AlternateTitles { get; init; } = [];

    /// <summary>Gets localized titles and descriptions.</summary>
    [Searchable]
    public IReadOnlyList<Localized<ItemInfo>> Translations { get; init; } = [];

    /// <summary>Gets the year in which the item was first released.</summary>
    [Searchable, Sortable, Filterable, Groupable, Disambiguation, Prominence(Prominence.Primary)]
    [Display(Description = "The year in which the item was first released.", Example = "2017")]
    public int? Year { get; init; }

    /// <summary>Gets another year under which the item may legitimately be identified.</summary>
    [Filterable, Disambiguation, Prominence(Prominence.Diagnostic)]
    [Display(Description = "Another year under which the item may legitimately be identified.",
             Example = "2016")]
    public int? SecondaryYear { get; init; }

    /// <summary>Gets the media-owned release milestones and lifecycle behaviour.</summary>
    /// <remarks>
    /// The complete concrete type is retained. A movie cataloger sees movie dates and movie stage logic;
    /// a book cataloger sees publication states. The common item does not flatten either into an enum-keyed
    /// dictionary or pretend that every medium shares one status vocabulary.
    /// </remarks>
    public required TReleaseTimeline Lifecycle { get; init; }

    /// <inheritdoc />
    public TReleaseStage Status => Lifecycle.Stage;

    /// <inheritdoc />
    public CatalogRecordState CatalogState { get; init; }

    /// <inheritdoc />
    public IReadOnlyList<MediaCollection<TItem>> Collections { get; init; } = [];

    /// <inheritdoc />
    [Searchable, Multiline]
    public string? Overview { get; init; }

    /// <summary>Gets the item's duration, when duration is meaningful and known.</summary>
    [Sortable, Filterable]
    [Display(Description = "The item's duration.", Example = "2h 44m")]
    public TimeSpan? Runtime { get; init; }

    /// <summary>Gets the principal organization associated with publishing or producing the item.</summary>
    /// <remarks>
    /// A media type may add a richer typed organization model when one name is insufficient. The common
    /// property represents the single display value already shared by movie studios, television networks,
    /// music labels, publishers, and equivalent catalog sources.
    /// </remarks>
    [Sortable, Filterable, Groupable]
    [Display(Description = "The principal organization associated with the item.",
             Example = "Example Publisher")]
    public string? Organization { get; init; }

    /// <inheritdoc />
    [Filterable, Groupable]
    public ContentCertification? Certification { get; init; }

    /// <summary>Gets the item's genres.</summary>
    [Filterable, Groupable, Prominence(Prominence.Secondary)]
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Gets free-form catalog keywords.</summary>
    [Filterable, Prominence(Prominence.Diagnostic)]
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>Gets the item's official website.</summary>
    public Uri? Website { get; init; }

    /// <summary>Gets a representative preview, such as a trailer, teaser, clip, excerpt, or sample.</summary>
    public Uri? Preview { get; init; }

    /// <inheritdoc />
    public ArtworkSet Artwork { get; init; } = ArtworkSet.Empty;

    /// <summary>Gets catalog popularity, used for discovery ordering.</summary>
    [Sortable, Prominence(Prominence.Diagnostic)]
    public double? Popularity { get; init; }

    /// <inheritdoc />
    [Sortable, Filterable]
    public IReadOnlyList<Rating> Ratings { get; init; } = [];

}
