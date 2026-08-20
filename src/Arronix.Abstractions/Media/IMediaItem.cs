
namespace Arronix.Abstractions.Media;

/// <summary>
/// A durable catalog or library item owned by a media type.
/// </summary>
/// <remarks>
/// <para>
/// The common entity facts are a compiled interface rather than a base class, so a media extension keeps
/// its own inheritance choices while catalogers, curators and generic work surfaces can consume the facts
/// without field-name conventions or descriptor lookups.
/// </para>
/// <para>
/// Implementing it is the whole of the entity half of declaring a media kind. What the properties cannot
/// say — how files bind, what groups exist, how releases are matched — is said by the kind's
/// <see cref="MediaType{TItem,TTarget,TRelease,TParser}"/>.
/// </para>
/// </remarks>
public interface IMediaItem : IMediaEntity
{
    /// <summary>Gets whether an authoritative catalog currently presents the item.</summary>
    CatalogRecordState CatalogState { get; }
}

/// <summary>A media item with a media-owned, compile-time release-stage vocabulary.</summary>
/// <typeparam name="TReleaseStage">The media type's release-stage enumeration.</typeparam>
public interface IMediaItem<out TReleaseStage> : IMediaItem
    where TReleaseStage : struct, Enum
{
    /// <summary>Gets the item's current release stage.</summary>
    TReleaseStage Status { get; }
}

/// <summary>A media item whose common collection memberships retain its exact item type.</summary>
/// <typeparam name="TItem">The exact item type.</typeparam>
/// <typeparam name="TReleaseStage">The media type's release-stage enumeration.</typeparam>
public interface IMediaItem<TItem, out TReleaseStage> : IMediaItem<TReleaseStage>
    where TItem : class, IMediaItem<TItem, TReleaseStage>
    where TReleaseStage : struct, Enum
{
    /// <summary>Gets the ordinary media collections containing the item.</summary>
    IReadOnlyList<MediaCollection<TItem>> Collections { get; }
}
