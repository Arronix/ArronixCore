using Arronix.Abstractions.Definition;

namespace Arronix.Abstractions.Media;

/// <summary>Host engine inputs compiled from a typed media definition.</summary>
public sealed record MediaKindModel
{
    /// <summary>
    /// Gets the media-owned release-title grammar.
    /// </summary>
    public ParseDeclaration? Parsing { get; init; }

    /// <summary>
    /// Gets how parsed readings resolve to catalog entries and units.
    /// </summary>
    public required MatchDeclaration Matching { get; init; }

    /// <summary>
    /// Gets the search templates: query tiers per search kind, alias templates, the coordinate grammar.
    /// </summary>
    public required QueryDeclaration Querying { get; init; }

    /// <summary>
    /// Gets the naming data derivation cannot know: default templates, selection rows, the folder spine,
    /// token fallbacks.
    /// </summary>
    public NamingDeclaration Naming { get; init; } = NamingDeclaration.Default;

    /// <summary>
    /// Gets how the kind's items are summarized.
    /// </summary>
    public NotificationDeclaration Notifications { get; init; } = NotificationDeclaration.Default;

    /// <summary>
    /// Gets the rules a user's own file template must satisfy before it is saved.
    /// </summary>
    /// <remarks>
    /// Predicates rather than a per-token "is required" flag, because the rules that occur in practice are
    /// not conjunctions. This is the one part of the model that is code rather than data, and it is code
    /// because the shape it replaces provably could not carry the rule.
    /// </remarks>
    public IReadOnlyList<TemplateRequirement> TemplateRules { get; init; } = [];

    /// <summary>
    /// Gets the kind's rewrite of a dotted run in a release title, when it has one.
    /// </summary>
    public Func<string, string>? Respace { get; init; }

}
