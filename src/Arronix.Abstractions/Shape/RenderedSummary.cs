
namespace Arronix.Abstractions.Shape;

/// <summary>
/// What an extension says about one of its own items, in a form anything can present.
/// </summary>
/// <remarks>
/// <para>
/// Rendered by the extension that owns the item, carried by the host, and consumed by whoever is
/// reporting: an outbound notification, an activity feed, a push payload. All three want the same
/// structure — a title, a qualifier, an image and a way back to the item — and a single message string
/// forces every consumer to re-derive it from text.
/// </para>
/// <para>
/// It lives with the shape rather than with the presentation vocabulary because it is a media
/// extension's rendering of its own item, and because the provider contracts consume it too; putting it
/// elsewhere would couple two otherwise independent contract areas for no gain.
/// </para>
/// </remarks>
public sealed record RenderedSummary
{
    /// <summary>
    /// Gets the headline. Plain text.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the qualifier that disambiguates the headline. Plain text.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// Gets the longer description. Plain text; never markup.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// Gets the labeled values worth carrying alongside the headline.
    /// </summary>
    public IReadOnlyList<SummaryField> Fields { get; init; } = [];

    /// <summary>
    /// Gets the images representing the item.
    /// </summary>
    public IReadOnlyList<ArtworkRef> Artwork { get; init; } = [];

    /// <summary>
    /// Gets the platform-relative location of the item, for example <c>"/kinds/x/items/42"</c>. Relative
    /// and platform-internal: an absolute address would bake one deployment's origin into a stored
    /// message.
    /// </summary>
    public string? DeepLink { get; init; }
}

/// <summary>
/// One labeled value carried alongside a summary's headline.
/// </summary>
/// <param name="Label">What the value is.</param>
/// <param name="Value">The value, already formatted by the extension that owns it.</param>
/// <param name="Weight">How important the value is relative to the summary's other fields.</param>
public readonly record struct SummaryField(string Label, string Value, SummaryFieldWeight Weight);

/// <summary>
/// How important a summary field is.
/// </summary>
/// <remarks>
/// An importance rank rather than a placement instruction: "weight" is meaningful to a spoken summary and
/// a one-line notification alike, where a placement word would be meaningful to neither.
/// </remarks>
public enum SummaryFieldWeight
{
    /// <summary>Carried wherever the summary is carried.</summary>
    Primary = 0,

    /// <summary>Carried where there is room.</summary>
    Secondary = 1
}

/// <summary>
/// One image representing an item.
/// </summary>
/// <param name="Role">What the image is for: <c>"poster"</c>, <c>"banner"</c>, <c>"thumbnail"</c>. Open.</param>
/// <param name="Url">Where the image can be fetched from.</param>
/// <param name="Width">The image's width in pixels, when known.</param>
/// <param name="Height">The image's height in pixels, when known.</param>
public sealed record ArtworkRef(string Role, Uri Url, int? Width, int? Height);
