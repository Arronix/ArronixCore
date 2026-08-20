using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>A named collection of media items with catalog identity and metadata of its own.</summary>
/// <typeparam name="TItem">The kind of item contained by the collection.</typeparam>
/// <remarks>
/// This is a complete usable group type, not a descriptor or an opaque host wrapper. A media type whose
/// collections carry additional domain facts may derive a more specific class from it.
/// </remarks>
public class MediaCollection<TItem> : IMediaGroup<TItem>
    where TItem : IMediaItem
{
    /// <inheritdoc />
    public required MediaItemId Key { get; init; }

    /// <inheritdoc />
    public ExternalIdSet ExternalIds { get; init; } = ExternalIdSet.Empty;

    /// <inheritdoc />
    [Searchable, Display(Example = "The Lord of the Rings Collection")]
    public required string Title { get; init; }

    /// <inheritdoc />
    [Filterable, Groupable]
    public Language? TitleLanguage { get; init; }

    /// <inheritdoc />
    [Searchable, Multiline]
    public string? Overview { get; init; }

    /// <inheritdoc />
    public ArtworkSet Artwork { get; init; } = ArtworkSet.Empty;

    /// <summary>Gets the number of items the catalog says belong to the collection.</summary>
    /// <remarks>This is catalog metadata, not the number currently held by the library.</remarks>
    [Count, Sortable, Prominence(Prominence.Secondary)]
    public int MemberCount { get; init; }
}
