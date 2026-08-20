
namespace Arronix.Abstractions.Definition;

/// <summary>
/// How the kind's items are summarized when the platform tells someone about them.
/// </summary>
/// <remarks>
/// Declared data for the host summary renderer. The default declaration makes the renderer assemble a
/// generic summary from the shape's prominent fields; a kind declares more only where a generic summary
/// would misname its subject. The renderer seam itself lands in a later phase — these rows are inert
/// until it does, which is stated here so nobody mistakes a declared row for a delivered notification.
/// </remarks>
public sealed record NotificationDeclaration
{
    /// <summary>
    /// Gets the declaration that renders a host-generic summary from prominent fields.
    /// </summary>
    public static NotificationDeclaration Default { get; } = new();

    /// <summary>
    /// Gets the headline template. Null renders the host-generic headline.
    /// </summary>
    public string? HeadlineTemplate { get; init; }

    /// <summary>
    /// Gets the greatest headline length a destination is assumed to accept.
    /// </summary>
    public int HeadlineMaxLength { get; init; } = 256;

    /// <summary>
    /// Gets the field rendered as the body text, by identifier.
    /// </summary>
    public string? BodyFieldId { get; init; }

    /// <summary>
    /// Gets the greatest body length a destination is assumed to accept.
    /// </summary>
    public int BodyMaxLength { get; init; } = 300;

    /// <summary>
    /// Gets the summary field rows, in render order.
    /// </summary>
    public IReadOnlyList<SummaryFieldRule> Fields { get; init; } = [];

    /// <summary>
    /// Gets the summary rows for groups on the kind's grouping axes.
    /// </summary>
    public IReadOnlyList<GroupSummaryRule> GroupSummaries { get; init; } = [];
}
