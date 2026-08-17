using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Definition;

namespace Arronix.Abstractions.Media;

/// <summary>
/// The per-kind inputs the host's media engines compile, for a kind whose structure is a type rather than
/// a declaration.
/// </summary>
/// <remarks>
/// <para>
/// What is <b>not</b> here is the point. There is no structure section and no intent section, because both
/// are derived from the item type and its attributes; there is no strategy section, because a strategy is
/// a method on the kind's own type; and there is no required-vocabulary section, because with a typed model
/// the compiler already knows which vocabulary the kind uses. A typed kind that could also hand over a
/// hand-written shape would have two sources of truth for its structure, which is the conflation the typed
/// surface exists to remove.
/// </para>
/// <para>
/// What remains is the residue a later iteration types: release parsing, which is regex and stays regex;
/// matching, querying, naming, quality and notification, whose <i>references into the item</i> the builder
/// already turns into property references; the catalog mapping, which leaves wholesale when catalogers
/// become plugins of their own; and the parity corpus, which is evidence and evidence is data.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record MediaKindModel
{
    /// <summary>
    /// Gets the release models: how a release title reads into coordinates and how token evidence resolves
    /// to a ladder rung.
    /// </summary>
    public required ParseDeclaration Parsing { get; init; }

    /// <summary>
    /// Gets how parsed readings resolve to catalog entries and units.
    /// </summary>
    public required MatchDeclaration Matching { get; init; }

    /// <summary>
    /// Gets the search templates: query tiers per search kind, alias templates, the coordinate grammar.
    /// </summary>
    public required QueryDeclaration Querying { get; init; }

    /// <summary>
    /// Gets quality evaluation beyond the ladder. Defaults to pure ladder derivation.
    /// </summary>
    public QualityDeclaration Quality { get; init; } = QualityDeclaration.LadderDerived;

    /// <summary>
    /// Gets the naming data derivation cannot know: default templates, selection rows, the folder spine,
    /// token fallbacks.
    /// </summary>
    public NamingDeclaration Naming { get; init; } = NamingDeclaration.Default;

    /// <summary>
    /// Gets the metadata mapping. Null when the kind has no catalog authority of its own.
    /// </summary>
    public CatalogDeclaration? Catalog { get; init; }

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
    /// <remarks>
    /// What used to be a host-owned named strategy with a role, a parameter dictionary, a requirement row,
    /// a host vocabulary entry and a load-time resolution rule. A strategy is a method.
    /// </remarks>
    public Func<string, string>? Respace { get; init; }

    /// <summary>
    /// Gets the parity cases the kind ships; the host keeps them green across engine upgrades.
    /// </summary>
    public IReadOnlyList<CorpusCase> Corpus { get; init; } = [];
}
