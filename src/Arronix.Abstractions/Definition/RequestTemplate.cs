
namespace Arronix.Abstractions.Definition;

/// <summary>
/// One named catalog request, as a template.
/// </summary>
public sealed record RequestTemplate
{
    /// <summary>
    /// Gets the request's identifier.
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// Gets the request verb.
    /// </summary>
    public required string Verb { get; init; }

    /// <summary>
    /// Gets the route template, relative to the catalog's endpoint setting.
    /// </summary>
    public required string Route { get; init; }

    /// <summary>
    /// Gets the query parameters, as templates.
    /// </summary>
    public IReadOnlyList<RequestParameter> Query { get; init; } = [];

    /// <summary>
    /// Gets the body template, for verbs that carry one.
    /// </summary>
    public string? BodyTemplate { get; init; }
}
