
namespace Arronix.Abstractions.Media;

/// <summary>The common acquisition intent for one typed media item.</summary>
/// <typeparam name="TItem">The item the acquisition is intended to cover.</typeparam>
/// <param name="Item">The durable item being requested.</param>
/// <remarks>
/// Media types use this closed type directly when one request covers one item. A media type defines a
/// richer target only when one request can express genuinely different coverage, such as a television
/// season, episode span, or set of episode coordinates.
/// </remarks>
public record ReleaseTarget<TItem>(TItem Item) : IReleaseTarget
    where TItem : class, IMediaItem;
