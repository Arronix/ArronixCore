using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Media.Typed.Compilation;

/// <summary>
/// The one-way host compilation of a typed media definition, before descriptors are derived.
/// </summary>
/// <remarks>
/// Kept as drafts rather than descriptors because several results depend on the complete definition: a
/// selection facet needs the level identifier and a naming token needs the whole field set. Compiling first
/// and deriving once keeps the public definition typed without making it carry host wire machinery.
/// </remarks>
internal sealed class TypedDeclaration
{
    internal CompiledShapeCatalog? CompiledShapes { get; set; }

    internal string? Singular { get; set; }

    internal string? Plural { get; set; }

    internal bool FilesBindOnePerItem { get; set; }

    internal List<FormatFamilyDraft> Formats { get; } = [];

    internal IdentityDraft Identity { get; } = new();

    internal List<GroupDraft> Groups { get; } = [];

    internal List<SelectionDraft> Selections { get; } = [];

    internal string? AvailabilitySelectionId { get; set; }

    internal List<SearchDraft> Searches { get; } = [];

    internal List<MatchLayer> MatchLayers { get; } = [];

    internal List<AgreementRule> Agreements { get; } = [];

    internal bool ScopeReplacesSearch { get; set; }

    internal AmbiguityPolicy Ambiguity { get; set; } = AmbiguityPolicy.Reject;

    internal List<QueryTierDraft> Tiers { get; } = [];

    internal List<AliasTemplate> Aliases { get; } = [];

    internal Dictionary<string, string> Templates { get; } = new(StringComparer.Ordinal);

    internal string FolderSpine { get; set; } = "{root}/{folder}";

    internal List<TemplateSelectionRule> TemplateSelection { get; } = [];

    internal List<TokenFallbackRule> TokenFallbacks { get; } = [];

    internal List<TemplateRequirement> TemplateRules { get; } = [];

    internal string? HeadlineTemplate { get; set; }

    internal int HeadlineMaxLength { get; set; } = 256;

    internal string? BodyFieldId { get; set; }

    internal int BodyMaxLength { get; set; } = 300;

    internal List<SummaryFieldRule> SummaryFields { get; } = [];

    internal List<GroupSummaryRule> GroupSummaries { get; } = [];

    internal string DefaultBrowseAxisId { get; set; } = "all";

    internal string DefaultBrowseName { get; set; } = "All";

    internal Dictionary<string, SortDirection> SortOverrides { get; } = new(StringComparer.Ordinal);

    internal HashSet<string> HiddenAxisFieldIds { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, StateTone> StateTones { get; } = new(StringComparer.Ordinal);

    internal List<WorkbenchDraft> Workbenches { get; } = [];

    internal ParseDeclaration? Parsing { get; set; }

    internal Func<string, string>? Respace { get; set; }

    internal object? ReleasePolicy { get; set; }

    internal List<DerivationBinding> Derivations { get; } = [];
}

/// <summary>One format family, as recorded.</summary>
internal sealed class FormatFamilyDraft(string familyId, string name)
{
    internal string FamilyId { get; } = familyId;

    internal string Name { get; } = name;

    internal List<string> Extensions { get; } = [];

    internal Type? RepresentationType { get; set; }

    internal bool SupportsEmbeddedMetadata { get; set; }

    internal bool CoexistsWithOtherFamilies { get; set; }
}

/// <summary>The external-identity roles a kind declared, as recorded.</summary>
internal sealed class IdentityDraft
{
    internal List<IdentifierRole> Required { get; } = [];

    internal List<IdentifierRole> Admitted { get; } = [];

}

/// <summary>One grouping axis, as recorded.</summary>
internal sealed class GroupDraft(Type groupType, string membershipPropertyName, string axisId)
{
    internal Type GroupType { get; } = groupType;

    internal string MembershipPropertyName { get; } = membershipPropertyName;

    internal string AxisId { get; } = axisId;

    internal GroupingArity Arity { get; set; } = GroupingArity.ManyToOne;

    internal string? Singular { get; set; }

    internal string? Plural { get; set; }

    internal bool IsMonitorable { get; set; }

    internal bool IsDiscoverySource { get; set; }

    internal GroupLifetime Lifetime { get; set; } = GroupLifetime.RefCounted;
}

/// <summary>One selection policy, as recorded.</summary>
internal sealed class SelectionDraft(string facetId, string name, SelectionFacetKind kind)
{
    internal string FacetId { get; } = facetId;

    internal string Name { get; set; } = name;

    internal SelectionFacetKind Kind { get; } = kind;

    internal Type? EnumType { get; set; }

    internal List<FacetValue> Values { get; } = [];

    internal List<string> DefaultAllowed { get; } = [];

    internal ThresholdDirection ThresholdDirection { get; set; }

    internal double? DefaultNumber { get; set; }

    internal string? Unit { get; set; }

    internal FacetApplication Application { get; set; } = FacetApplication.Acquisition;
}

/// <summary>One search kind, as recorded.</summary>
internal sealed class SearchDraft(string searchKindId, string name)
{
    internal string SearchKindId { get; } = searchKindId;

    internal string Name { get; } = name;

    internal List<SearchTerm> Required { get; } = [];

    internal List<SearchTerm> Optional { get; } = [];

    internal List<CategoryId> Categories { get; } = [];
}

/// <summary>One query tier, as recorded.</summary>
internal sealed class QueryTierDraft(string tierId, string searchKindId, int order)
{
    internal string TierId { get; } = tierId;

    internal string SearchKindId { get; } = searchKindId;

    internal int Order { get; } = order;

    internal List<SearchOrigin> Origins { get; } = [];

    internal List<QueryArgument> Arguments { get; } = [];

    internal string FreeTextTemplate { get; set; } = string.Empty;

    internal List<string> RequiredFields { get; } = [];

    internal List<IdentifierRole> RequiredRoles { get; } = [];

    internal bool FanOutPerAlias { get; set; }

    internal bool CarryAliases { get; set; }
}

/// <summary>One working surface, as recorded.</summary>
internal sealed class WorkbenchDraft(string workbenchId, string name, Type rowType)
{
    internal string WorkbenchId { get; } = workbenchId;

    internal string Name { get; } = name;

    internal Type RowType { get; } = rowType;

    internal WorkbenchSubject Subject { get; set; } = WorkbenchSubject.LibraryItems;

    internal List<ActionParameter> Inputs { get; } = [];

    internal string CommitLabel { get; set; } = "Commit";

    internal Consequence CommitConsequence { get; set; } = Consequence.Safe;
}

/// <summary>One bound recomputation of a stored, derived property.</summary>
/// <param name="PropertyName">The property recomputed.</param>
/// <param name="Recompute">The recomputation, taking the whole item and returning the new value.</param>
internal sealed record DerivationBinding(string PropertyName, Func<object, object?> Recompute);
