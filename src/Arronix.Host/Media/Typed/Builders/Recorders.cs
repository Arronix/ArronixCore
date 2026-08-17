using System.Linq;
using System.Linq.Expressions;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Quality;

// The derivation reads and produces experimental contracts throughout.
#pragma warning disable ARX0013
#pragma warning disable ARX0016
#pragma warning disable ARX0019
#pragma warning disable ARX0020
#pragma warning disable ARX0021

namespace Arronix.Host.Media.Typed.Builders;

/// <summary>The host's own match vocabulary, as the derivation names it.</summary>
/// <remarks>
/// A typed kind never spells these: it says which expansion it wants and the derivation resolves the host's
/// identifier for it, which is one fewer string that has to agree with something across an assembly
/// boundary.
/// </remarks>
internal static class HostMatchVocabulary
{
    /// <summary>The normalizer every title layer runs through.</summary>
    internal const string CleanTitle = "clean-title";

    /// <summary>The expander that treats roman numerals and their decimal spellings as one key.</summary>
    internal const string RomanNumeralVariants = "roman-numeral-variants";
}

/// <summary>Records one format family.</summary>
internal sealed class FormatFamilyRecorder<TItem>(FormatFamilyDraft draft)
    : IFormatFamilyBuilder<TItem>
    where TItem : IMediaItem
{
    /// <inheritdoc />
    public IFormatFamilyBuilder<TItem> Extensions(params string[] extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        draft.Extensions.AddRange(extensions);
        return this;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The model is compiled here rather than recorded as a pair of types: a family that mis-states a
    /// rendering rule or a default policy then fails where its author is standing, not on the first
    /// release that happens to exercise the rule.
    /// </remarks>
    public IFormatFamilyBuilder<TItem> Quality<TFacts, TType>()
        where TFacts : IQualityFacts
        where TType : IQualityType<TFacts>
    {
        draft.Quality = QualityTypeFactory.Create<TFacts, TType>();
        return this;
    }

    /// <inheritdoc />
    public IFormatFamilyBuilder<TItem> RefinedBy<TFacts, TRefinement>()
        where TFacts : IQualityFacts
        where TRefinement : IQualityRefinement<TFacts>
    {
        if (draft.Quality is null)
        {
            throw new InvalidOperationException(
                $"The format family '{draft.FamilyId}' declared a refinement before it declared a quality "
                + "model. A kind contributes to a family that is already declared; it does not bring one "
                + "into existence by refining it.");
        }

        if (draft.Quality is not HostQualityType<TFacts> host)
        {
            throw new InvalidOperationException(
                $"The format family '{draft.FamilyId}' reads '{draft.Quality.FactsType.Name}' and the "
                + $"refinement contributes '{typeof(TFacts).Name}'.");
        }

        host.RefinedBy(TRefinement.Refine);
        return this;
    }

    /// <inheritdoc />
    public IFormatFamilyBuilder<TItem> Facet(
        string facetId,
        string name,
        TechnicalFacetCase titleCase,
        IReadOnlyList<string>? exceptions = null,
        bool ordinalSuffixesLowerCase = false)
    {
        draft.Facets.Add(new TechnicalFacet
        {
            FacetId = facetId,
            Name = name,
            TitleCase = titleCase,
            CaseExceptions = exceptions ?? [],
            OrdinalSuffixesLowerCase = ordinalSuffixesLowerCase
        });

        return this;
    }

    /// <inheritdoc />
    public IFormatFamilyBuilder<TItem> SupportsEmbeddedMetadata()
    {
        draft.SupportsEmbeddedMetadata = true;
        return this;
    }

    /// <inheritdoc />
    public IFormatFamilyBuilder<TItem> CoexistsWithOtherFamilies()
    {
        draft.CoexistsWithOtherFamilies = true;
        return this;
    }
}

/// <summary>Records the external-identity roles a kind declares.</summary>
internal sealed class IdentityRecorder<TItem>(IdentityDraft draft) : IIdentityBuilder<TItem>
    where TItem : IMediaItem
{
    /// <inheritdoc />
    public IIdentityBuilder<TItem> Requires(IdentifierRole role)
    {
        if (!draft.Required.Contains(role))
        {
            draft.Required.Add(role);
        }

        return this;
    }

    /// <inheritdoc />
    public IIdentityBuilder<TItem> Admits(IdentifierRole role)
    {
        if (!draft.Admitted.Contains(role))
        {
            draft.Admitted.Add(role);
        }

        return this;
    }

    /// <inheritdoc />
    public IIdentityBuilder<TItem> SupportsRedirects()
    {
        draft.SupportsRedirects = true;
        return this;
    }
}

/// <summary>Records one grouping axis.</summary>
internal sealed class GroupRecorder<TItem, TGroup>(GroupDraft draft) : IGroupBuilder<TItem, TGroup>
    where TItem : IMediaItem
    where TGroup : class, IMediaGroup<TItem>
{
    /// <inheritdoc />
    public IGroupBuilder<TItem, TGroup> Named(string singular, string plural)
    {
        draft.Singular = singular;
        draft.Plural = plural;
        return this;
    }

    /// <inheritdoc />
    public IGroupBuilder<TItem, TGroup> Monitorable()
    {
        draft.IsMonitorable = true;
        return this;
    }

    /// <inheritdoc />
    public IGroupBuilder<TItem, TGroup> DiscoverySource()
    {
        draft.IsDiscoverySource = true;
        return this;
    }

    /// <inheritdoc />
    public IGroupBuilder<TItem, TGroup> Independent()
    {
        draft.Lifetime = GroupLifetime.Independent;
        return this;
    }
}

/// <summary>Holds one selection policy's draft so a second call for the same property finds it.</summary>
internal sealed class SelectionRecorder(SelectionDraft draft)
{
    /// <summary>Gets the draft.</summary>
    internal SelectionDraft Draft { get; } = draft;
}

/// <summary>Records a selection policy that is a threshold over an ordered enumeration.</summary>
internal sealed class OrderedSelectionRecorder<TItem, TEnum>(SelectionDraft draft)
    : IOrderedSelectionBuilder<TItem, TEnum>
    where TItem : IMediaItem
    where TEnum : struct, Enum
{
    /// <inheritdoc />
    public string FacetId => draft.FacetId;

    /// <inheritdoc />
    public IOrderedSelectionBuilder<TItem, TEnum> Named(string name)
    {
        draft.Name = name;
        return this;
    }

    /// <inheritdoc />
    public IOrderedSelectionBuilder<TItem, TEnum> AtLeast(TEnum defaultFloor)
    {
        draft.DefaultAllowed.Clear();
        draft.DefaultAllowed.Add(DerivedNames.Identifier(defaultFloor.ToString() ?? string.Empty));
        return this;
    }

    /// <inheritdoc />
    public IOrderedSelectionBuilder<TItem, TEnum> Offering(params TEnum[] choices)
    {
        ArgumentNullException.ThrowIfNull(choices);

        draft.Values.Clear();

        // Enumeration order, not call order: the order is the enumeration's, and letting a call site
        // reorder it would put the threshold's meaning back in a place no consumer can read.
        foreach (var member in EnumOrder.Values<TEnum>().Where(choices.Contains))
        {
            draft.Values.Add(new FacetValue(
                DerivedNames.Identifier(member.ToString() ?? string.Empty),
                DerivedNames.Label(member.ToString() ?? string.Empty)));
        }

        return this;
    }

    /// <inheritdoc />
    public IOrderedSelectionBuilder<TItem, TEnum> Gates(FacetApplication application)
    {
        draft.Application = application;
        return this;
    }
}

/// <summary>Records a selection policy that is a numeric bound with no backing property.</summary>
internal sealed class ThresholdSelectionRecorder(SelectionDraft draft) : IThresholdSelectionBuilder
{
    /// <inheritdoc />
    public string FacetId => draft.FacetId;

    /// <inheritdoc />
    public IThresholdSelectionBuilder Days() => InUnit("days");

    /// <inheritdoc />
    public IThresholdSelectionBuilder InUnit(string unit)
    {
        draft.Unit = unit;
        return this;
    }

    /// <inheritdoc />
    public IThresholdSelectionBuilder AtLeast(double defaultBound)
    {
        draft.ThresholdDirection = ThresholdDirection.AtLeast;
        draft.DefaultNumber = defaultBound;
        return this;
    }

    /// <inheritdoc />
    public IThresholdSelectionBuilder AtMost(double defaultBound)
    {
        draft.ThresholdDirection = ThresholdDirection.AtMost;
        draft.DefaultNumber = defaultBound;
        return this;
    }

    /// <inheritdoc />
    public IThresholdSelectionBuilder Gates(FacetApplication application)
    {
        draft.Application = application;
        return this;
    }
}

/// <summary>Records one search kind.</summary>
internal sealed class SearchRecorder<TItem>(SearchDraft draft) : ISearchBuilder<TItem>
    where TItem : IMediaItem
{
    /// <inheritdoc />
    public ISearchBuilder<TItem> Requires(params SearchTerm[] terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        draft.Required.AddRange(terms);
        return this;
    }

    /// <inheritdoc />
    public ISearchBuilder<TItem> Admits(params SearchTerm[] terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        draft.Optional.AddRange(terms);
        return this;
    }

    /// <inheritdoc />
    public ISearchBuilder<TItem> Categories(params int[] categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        draft.Categories.AddRange(categories.Select(CategoryId.FromInt));
        return this;
    }
}

/// <summary>Records one working surface.</summary>
internal sealed class WorkbenchRecorder<TItem, TRow>(IMediaTypeBuilder<TItem> root, WorkbenchDraft draft)
    : IWorkbenchBuilder<TItem, TRow>
    where TItem : IMediaItem
{
    /// <inheritdoc />
    public IWorkbenchBuilder<TItem, TRow> Subject(WorkbenchSubject subject)
    {
        draft.Subject = subject;
        return this;
    }

    /// <inheritdoc />
    public IWorkbenchBuilder<TItem, TRow> Input(string inputId, string name)
    {
        draft.Inputs.Add(new ActionParameter(inputId, name, FieldValueKind.Text, true, []));
        return this;
    }

    /// <inheritdoc />
    public IWorkbenchBuilder<TItem, TRow> Input(string inputId, string name, IdentifierRole role)
    {
        draft.Inputs.Add(new ActionParameter(
            inputId,
            name,
            FieldValueKind.ExternalIdentifier,
            false,
            [],
            null,
            $"identity.{DerivedNames.Identifier(role.ToString())}"));

        return this;
    }

    /// <inheritdoc />
    public IMediaTypeBuilder<TItem> Commit(string label, Consequence consequence)
    {
        draft.CommitLabel = label;
        draft.CommitConsequence = consequence;
        return root;
    }
}

/// <summary>Records the refinements of one row of alias spellings.</summary>
internal sealed class AliasRecorder : IAliasOptions
{
    /// <summary>Gets whether spellings are emitted only for accepted languages.</summary>
    internal bool FiltersByAcceptedLanguages { get; private set; }

    /// <summary>Gets whether spellings ride along without becoming a query of their own.</summary>
    internal bool NeverOwnsQuery { get; private set; }

    /// <inheritdoc />
    public IAliasOptions FilterByAcceptedLanguages()
    {
        FiltersByAcceptedLanguages = true;
        return this;
    }

    /// <inheritdoc />
    public IAliasOptions NeverOwnQuery()
    {
        NeverOwnsQuery = true;
        return this;
    }
}

/// <summary>Records how a group is summarized.</summary>
internal sealed class GroupSummaryRecorder<TItem, TGroup> : IGroupSummaryBuilder<TItem, TGroup>
    where TItem : IMediaItem
    where TGroup : class, IMediaGroup<TItem>
{
    private readonly List<SummaryFieldRule> _fields = [];

    /// <summary>Gets the group's headline template.</summary>
    internal string? HeadlineTemplate { get; private set; }

    /// <summary>Gets the group's summary rows.</summary>
    internal IReadOnlyList<SummaryFieldRule> Fields => _fields;

    /// <inheritdoc />
    public IGroupSummaryBuilder<TItem, TGroup> Headline(Expression<Func<TGroup, string?>> headline)
    {
        HeadlineTemplate = ExpressionPaths.Template(headline);
        return this;
    }

    /// <inheritdoc />
    public IGroupSummaryBuilder<TItem, TGroup> Field(
        string label,
        Expression<Func<TGroup, object?>> value,
        SummaryFieldWeight weight = SummaryFieldWeight.Secondary)
    {
        var paths = ExpressionPaths.Paths(value);
        _fields.Add(new SummaryFieldRule(
            label,
            string.Concat(paths.Select(static path => $"{{{path}}}")),
            weight));

        return this;
    }
}

/// <summary>
/// Presents the host's view of a user's template to a rule that was written against the entity.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// The whole of what turns an author's property expression into the field identifier the host holds. It is
/// the only reason the builder-side and model-side template facts differ by an arity.
/// </remarks>
internal sealed class NamingTemplateFactsAdapter<TItem>(INamingTemplateFacts facts)
    : INamingTemplateFacts<TItem>
    where TItem : IMediaItem
{
    /// <inheritdoc />
    public bool Has<TValue>(Expression<Func<TItem, TValue>> property) =>
        facts.HasField(ExpressionPaths.FieldId(property));

    /// <inheritdoc />
    public bool Has(FileFact fact) => facts.Has(fact);
}
