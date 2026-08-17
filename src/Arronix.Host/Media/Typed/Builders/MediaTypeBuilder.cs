using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

// The derivation reads and produces experimental contracts throughout.
#pragma warning disable ARX0013
#pragma warning disable ARX0016
#pragma warning disable ARX0019
#pragma warning disable ARX0020

namespace Arronix.Host.Media.Typed.Builders;

/// <summary>
/// Records one media type's configuration call.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// <para>
/// One class implementing the chained interfaces rather than one class each, because the chain is a single
/// conversation with a single subject: a tier's refinements and the next tier are the same recorder with a
/// different cursor, and splitting them would mean threading the same state through a dozen objects to buy
/// nothing an author can see.
/// </para>
/// <para>
/// It records and does not derive. Nothing here inspects the item type or produces a descriptor — that
/// happens once, afterwards, when every fact is in.
/// </para>
/// </remarks>
internal sealed class MediaTypeBuilder<TItem> :
    IMediaTypeBuilder<TItem>,
    IFileBindingBuilder<TItem>,
    IMatchBuilder<TItem>,
    IQueryTierBuilder<TItem>,
    INamingBuilder<TItem>,
    ISummaryBuilder<TItem>,
    IIntentBuilder<TItem>,
    IActionStepBuilder<TItem>,
    IQualityBuilder<TItem>,
    IParsingBuilder<TItem>
    where TItem : IMediaItem
{
    private readonly Dictionary<string, SelectionRecorder> _selectionsByProperty = new(StringComparer.Ordinal);
    private QueryTierDraft? _tier;
    private ActionDraft? _action;

    /// <summary>Gets everything the configuration call recorded.</summary>
    internal TypedDeclaration Declaration { get; } = new();

    // ---- the root ------------------------------------------------------------------------------------

    /// <inheritdoc />
    public IMediaTypeBuilder<TItem> Named(string singular, string plural)
    {
        Declaration.Singular = singular;
        Declaration.Plural = plural;
        return this;
    }

    /// <inheritdoc />
    public IFileBindingBuilder<TItem> Files => this;

    /// <inheritdoc />
    public IMatchBuilder<TItem> Matching => this;

    /// <inheritdoc />
    public IQueryBuilder<TItem> Querying => this;

    /// <inheritdoc />
    public INamingBuilder<TItem> Naming => this;

    /// <inheritdoc />
    public ISummaryBuilder<TItem> Summary => this;

    /// <inheritdoc />
    public IIntentBuilder<TItem> Intent => this;

    /// <inheritdoc />
    public IActionBuilder<TItem> Actions => this;

    /// <inheritdoc />
    public IQualityBuilder<TItem> Quality => this;

    /// <inheritdoc />
    public IMediaTypeBuilder<TItem> OnePerItem()
    {
        Declaration.FilesBindOnePerItem = true;
        return this;
    }

    /// <inheritdoc />
    public IFormatFamilyBuilder<TItem> Format(string familyId, string name)
    {
        var draft = new FormatFamilyDraft(familyId, name);
        Declaration.Formats.Add(draft);
        return new FormatFamilyRecorder<TItem>(draft);
    }

    /// <inheritdoc />
    public IIdentityBuilder<TItem> Identity(Expression<Func<TItem, ExternalIdSet>> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        // The property is named so the declaration cannot drift from the entity, and read for that reason
        // alone: which property carries the identifiers is already derivable from its type.
        _ = ExpressionPaths.Property(property);

        return new IdentityRecorder<TItem>(Declaration.Identity);
    }

    /// <inheritdoc />
    public IGroupBuilder<TItem, TGroup> Group<TGroup>(Expression<Func<TItem, TGroup?>> property)
        where TGroup : class, IMediaGroup<TItem>
    {
        ArgumentNullException.ThrowIfNull(property);

        var draft = new GroupDraft(typeof(TGroup), ExpressionPaths.Property(property));
        Declaration.Groups.Add(draft);

        return new GroupRecorder<TItem, TGroup>(draft);
    }

    /// <inheritdoc />
    public IOrderedSelectionBuilder<TItem, TEnum> Selection<TEnum>(Expression<Func<TItem, TEnum>> property)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(property);

        var facetId = ExpressionPaths.FieldId(property);

        // Idempotent by property, so an action offering the policy as a parameter simply names it rather
        // than declaring a second facet that would have to agree with the first by convention.
        if (!_selectionsByProperty.TryGetValue(facetId, out var recorder))
        {
            var draft = new SelectionDraft(facetId, DerivedNames.Label(facetId), SelectionFacetKind.Enumerated)
            {
                EnumType = typeof(TEnum)
            };

            Declaration.Selections.Add(draft);
            recorder = new SelectionRecorder(draft);
            _selectionsByProperty.Add(facetId, recorder);
        }

        return new OrderedSelectionRecorder<TItem, TEnum>(recorder.Draft);
    }

    /// <inheritdoc />
    public IThresholdSelectionBuilder Selection(string facetId, string name)
    {
        var existing = Declaration.Selections
            .FirstOrDefault(draft => string.Equals(draft.FacetId, facetId, StringComparison.Ordinal));

        if (existing is null)
        {
            existing = new SelectionDraft(facetId, name, SelectionFacetKind.Threshold);
            Declaration.Selections.Add(existing);
        }

        return new ThresholdSelectionRecorder(existing);
    }

    /// <inheritdoc />
    public ISearchBuilder<TItem> Search(string searchKindId, string name)
    {
        var draft = new SearchDraft(searchKindId, name);
        Declaration.Searches.Add(draft);
        return new SearchRecorder<TItem>(draft);
    }

    /// <inheritdoc />
    public IWorkbenchBuilder<TItem, TRow> Workbench<TRow>(string workbenchId, string name)
    {
        var draft = new WorkbenchDraft(workbenchId, name, typeof(TRow));
        Declaration.Workbenches.Add(draft);
        return new WorkbenchRecorder<TItem, TRow>(this, draft);
    }

    /// <inheritdoc />
    public IMediaTypeBuilder<TItem> Derives<TValue>(
        Expression<Func<TItem, TValue>> property,
        Func<TItem, TValue> recompute)
    {
        ArgumentNullException.ThrowIfNull(recompute);

        Declaration.Derivations.Add(
            new DerivationBinding(ExpressionPaths.Property(property), item => recompute((TItem)item)));

        return this;
    }

    /// <inheritdoc />
    public IParsingBuilder<TItem> Parsing(ParseDeclaration parsing)
    {
        Declaration.Parsing = parsing;
        return this;
    }

    /// <inheritdoc />
    public IMediaTypeBuilder<TItem> Respace(Func<string, string> respace)
    {
        Declaration.Respace = respace;
        return this;
    }

    /// <inheritdoc />
    public IMediaTypeBuilder<TItem> Catalog(CatalogDeclaration catalog)
    {
        Declaration.Catalog = catalog;
        return this;
    }

    /// <inheritdoc />
    public IMediaTypeBuilder<TItem> Corpus(IReadOnlyList<CorpusCase> cases)
    {
        Declaration.Corpus = cases;
        return this;
    }

    // ---- matching ------------------------------------------------------------------------------------

    /// <inheritdoc />
    public IMatchBuilder<TItem> Layer(
        string layerId,
        Expression<Func<TItem, IEnumerable<string?>>> keys,
        KeyExpansion expansion = KeyExpansion.None)
    {
        Declaration.MatchLayers.Add(new MatchLayer
        {
            LayerId = layerId,
            KeyTemplate = ExpressionPaths.KeyTemplate(keys),
            NormalizerId = HostMatchVocabulary.CleanTitle,
            ExpanderIds = expansion == KeyExpansion.RomanNumerals
                ? [HostMatchVocabulary.RomanNumeralVariants]
                : []
        });

        return this;
    }

    /// <inheritdoc />
    public IMatchBuilder<TItem> Agrees<TValue>(
        ReadingFact reading,
        Expression<Func<TItem, IEnumerable<TValue?>>> candidates,
        Agreement whenAbsent = Agreement.Reject,
        double? floor = null)
        where TValue : struct
    {
        Declaration.Agreements.Add(new AgreementRule
        {
            RuleId = DerivedNames.Identifier(reading.ToString()),
            Subject = $"reading.{reading}",
            AgreesWith = [.. ExpressionPaths.Paths(candidates)],
            AbsentAgrees = whenAbsent == Agreement.Accept,
            MinimumValue = floor
        });

        return this;
    }

    /// <inheritdoc />
    public IMatchBuilder<TItem> ScopeReplacesSearch()
    {
        Declaration.ScopeReplacesSearch = true;
        return this;
    }

    /// <inheritdoc />
    public IMatchBuilder<TItem> RejectAmbiguity()
    {
        Declaration.Ambiguity = AmbiguityPolicy.Reject;
        return this;
    }

    /// <inheritdoc />
    public IMatchBuilder<TItem> TiebreakAmbiguityByYear()
    {
        Declaration.Ambiguity = AmbiguityPolicy.TiebreakByYear;
        return this;
    }

    // ---- querying ------------------------------------------------------------------------------------

    /// <inheritdoc />
    public IQueryTierBuilder<TItem> Tier(string tierId, string searchKindId)
    {
        _tier = new QueryTierDraft(tierId, searchKindId, Declaration.Tiers.Count + 1);
        Declaration.Tiers.Add(_tier);
        return this;
    }

    /// <inheritdoc />
    public IQueryTierBuilder<TItem> RequiresIdentity(IdentifierRole role)
    {
        CurrentTier.RequiredRoles.Add(role);
        return this;
    }

    /// <inheritdoc />
    public IQueryTierBuilder<TItem> Requires<TValue>(Expression<Func<TItem, TValue>> property)
    {
        CurrentTier.RequiredFields.Add(ExpressionPaths.FieldId(property));
        return this;
    }

    /// <inheritdoc />
    public IQueryTierBuilder<TItem> Argument(SearchTerm term, IdentifierRole role, bool omitWhenAbsent = false)
    {
        CurrentTier.Arguments.Add(
            new QueryArgument(term, $"{{identity.{DerivedNames.Identifier(role.ToString())}}}", null, omitWhenAbsent));

        return this;
    }

    /// <inheritdoc />
    public IQueryTierBuilder<TItem> Argument<TValue>(
        SearchTerm term,
        Expression<Func<TItem, TValue>> property,
        bool omitWhenAbsent = false)
    {
        CurrentTier.Arguments.Add(
            new QueryArgument(term, $"{{{ExpressionPaths.FieldId(property)}}}", null, omitWhenAbsent));

        return this;
    }

    /// <inheritdoc />
    public IQueryTierBuilder<TItem> FreeText(Expression<Func<TItem, string?>> text)
    {
        CurrentTier.FreeTextTemplate = ExpressionPaths.Template(text);
        return this;
    }

    /// <inheritdoc />
    public IQueryTierBuilder<TItem> NoTerms()
    {
        CurrentTier.FreeTextTemplate = string.Empty;
        CurrentTier.Arguments.Clear();
        return this;
    }

    /// <inheritdoc />
    public IQueryTierBuilder<TItem> Origins(params SearchOrigin[] origins)
    {
        ArgumentNullException.ThrowIfNull(origins);
        CurrentTier.Origins.AddRange(origins);
        return this;
    }

    /// <inheritdoc />
    public IQueryTierBuilder<TItem> FanOutPerAlias()
    {
        CurrentTier.FanOutPerAlias = true;
        return this;
    }

    /// <inheritdoc />
    public IQueryTierBuilder<TItem> CarryAliases()
    {
        CurrentTier.CarryAliases = true;
        return this;
    }

    /// <inheritdoc />
    public IQueryBuilder<TItem> Alias(
        string aliasId,
        Expression<Func<TItem, IEnumerable<string?>>> spellings,
        Action<IAliasOptions>? configure = null)
    {
        var options = new AliasRecorder();
        configure?.Invoke(options);

        Declaration.Aliases.Add(new AliasTemplate
        {
            AliasId = aliasId,
            Template = ExpressionPaths.KeyTemplate(spellings),
            Order = Declaration.Aliases.Count + 1,
            FilterByAcceptedLanguages = options.FiltersByAcceptedLanguages,
            NeverOwnQuery = options.NeverOwnsQuery
        });

        return this;
    }

    // ---- naming --------------------------------------------------------------------------------------

    /// <inheritdoc />
    public INamingBuilder<TItem> File(string template)
    {
        Declaration.Templates["file"] = template;
        return this;
    }

    /// <inheritdoc />
    public INamingBuilder<TItem> Folder(string template)
    {
        Declaration.Templates["folder"] = template;
        return this;
    }

    /// <inheritdoc />
    public INamingBuilder<TItem> GroupFolder<TGroup>(string template)
        where TGroup : class, IMediaGroup<TItem>
    {
        Declaration.Templates[$"{DerivedNames.Identifier(typeof(TGroup).Name)}-folder"] = template;
        return this;
    }

    /// <inheritdoc />
    public INamingBuilder<TItem> Spine(string spine)
    {
        Declaration.FolderSpine = spine;
        return this;
    }

    /// <inheritdoc />
    public INamingBuilder<TItem> WhenGroupingBy<TGroup>(string ruleId)
        where TGroup : class, IMediaGroup<TItem>
    {
        var axisId = DerivedNames.Identifier(typeof(TGroup).Name);

        Declaration.TemplateSelection.Add(new TemplateSelectionRule
        {
            RuleId = ruleId,
            When = new TagPredicate(
            [
                new PredicateAtom
                {
                    Subject = $"options.groupBy.{axisId}",
                    Op = PredicateOp.Equals,
                    Values = ["true"]
                },
                new PredicateAtom
                {
                    Subject = $"fields.{axisId}",
                    Op = PredicateOp.Present
                }
            ]),
            InsertSpineSegment = $"{axisId}-folder"
        });

        return this;
    }

    /// <inheritdoc />
    public INamingBuilder<TItem> RequireInFileTemplate(
        string ruleId,
        string requirement,
        Func<INamingTemplateFacts<TItem>, bool> isSatisfied)
    {
        ArgumentNullException.ThrowIfNull(isSatisfied);

        Declaration.TemplateRules.Add(new TemplateRequirement(
            ruleId,
            requirement,
            facts => isSatisfied(new NamingTemplateFactsAdapter<TItem>(facts))));

        return this;
    }

    /// <inheritdoc />
    public INamingBuilder<TItem> Fallback<TValue>(
        Expression<Func<TItem, TValue>> property,
        params FileFact[] order)
    {
        ArgumentNullException.ThrowIfNull(order);

        Declaration.TokenFallbacks.Add(new TokenFallbackRule
        {
            Token = ExpressionPaths.FieldId(property),
            Order = [.. order.Select(static fact => $"file.{DerivedNames.Identifier(fact.ToString())}")]
        });

        return this;
    }

    /// <inheritdoc />
    public INamingBuilder<TItem> FallbackForEmptyResult(FileFact fact)
    {
        Declaration.TokenFallbacks.Add(new TokenFallbackRule
        {
            Token = string.Empty,
            Order = [$"file.{DerivedNames.Identifier(fact.ToString())}"]
        });

        return this;
    }

    // ---- summary -------------------------------------------------------------------------------------

    /// <inheritdoc />
    public ISummaryBuilder<TItem> Headline(Expression<Func<TItem, string?>> headline, int maxLength = 256)
    {
        Declaration.HeadlineTemplate = ExpressionPaths.Template(headline);
        Declaration.HeadlineMaxLength = maxLength;
        return this;
    }

    /// <inheritdoc />
    public ISummaryBuilder<TItem> Body(Expression<Func<TItem, string?>> body, int maxLength = 300)
    {
        Declaration.BodyFieldId = ExpressionPaths.FieldId(body);
        Declaration.BodyMaxLength = maxLength;
        return this;
    }

    /// <inheritdoc />
    public ISummaryBuilder<TItem> Field(
        string label,
        Expression<Func<TItem, object?>> value,
        SummaryFieldWeight weight = SummaryFieldWeight.Secondary)
    {
        var paths = ExpressionPaths.Paths(value);

        Declaration.SummaryFields.Add(
            new SummaryFieldRule(label, string.Concat(paths.Select(static path => $"{{{path}}}")), weight));

        return this;
    }

    /// <inheritdoc />
    public ISummaryBuilder<TItem> Group<TGroup>(
        Expression<Func<TItem, TGroup?>> property,
        Action<IGroupSummaryBuilder<TItem, TGroup>> configure)
        where TGroup : class, IMediaGroup<TItem>
    {
        ArgumentNullException.ThrowIfNull(configure);

        var recorder = new GroupSummaryRecorder<TItem, TGroup>();
        configure(recorder);

        Declaration.GroupSummaries.Add(new GroupSummaryRule
        {
            AxisId = DerivedNames.Identifier(typeof(TGroup).Name),
            HeadlineTemplate = recorder.HeadlineTemplate ?? string.Empty,
            Fields = recorder.Fields
        });

        return this;
    }

    // ---- intent --------------------------------------------------------------------------------------

    /// <inheritdoc />
    public IIntentBuilder<TItem> DefaultBrowse(string axisId, string name)
    {
        Declaration.DefaultBrowseAxisId = axisId;
        Declaration.DefaultBrowseName = name;
        return this;
    }

    /// <inheritdoc />
    public IIntentBuilder<TItem> Sort<TValue>(Expression<Func<TItem, TValue>> property, bool ascending)
    {
        Declaration.SortOverrides[ExpressionPaths.FieldId(property)] =
            ascending ? SortDirection.Ascending : SortDirection.Descending;

        return this;
    }

    /// <inheritdoc />
    public IIntentBuilder<TItem> Hide<TValue>(Expression<Func<TItem, TValue>> property)
    {
        Declaration.HiddenAxisFieldIds.Add(ExpressionPaths.FieldId(property));
        return this;
    }

    /// <inheritdoc />
    public IIntentBuilder<TItem> StateTone<TEnum>(TEnum member, StateTone tone)
        where TEnum : struct, Enum
    {
        Declaration.StateTones[DerivedNames.Identifier(member.ToString() ?? string.Empty)] = tone;
        return this;
    }

    // ---- actions -------------------------------------------------------------------------------------

    /// <inheritdoc />
    public IActionStepBuilder<TItem> Add(
        string actionId,
        string name,
        Consequence consequence,
        ActionScope scope)
    {
        _action = new ActionDraft(actionId, name, consequence, scope);
        Declaration.Actions.Add(_action);
        return this;
    }

    /// <inheritdoc />
    public IActionStepBuilder<TItem> AddForGroup<TGroup>(
        string actionId,
        string name,
        Consequence consequence)
        where TGroup : class, IMediaGroup<TItem>
    {
        _action = new ActionDraft(actionId, name, consequence, ActionScope.Group)
        {
            GroupAxisId = DerivedNames.Identifier(typeof(TGroup).Name)
        };

        Declaration.Actions.Add(_action);
        return this;
    }

    /// <inheritdoc />
    public IActionStepBuilder<TItem> LongRunning()
    {
        CurrentAction.LongRunning = true;
        return this;
    }

    /// <inheritdoc />
    public IActionStepBuilder<TItem> Acknowledge(string consequenceStatement)
    {
        CurrentAction.Confirmation = ConfirmationRequirement.Acknowledge;
        CurrentAction.ConsequenceStatement = consequenceStatement;
        return this;
    }

    /// <inheritdoc />
    public IActionStepBuilder<TItem> TypeToConfirm(string consequenceStatement)
    {
        CurrentAction.Confirmation = ConfirmationRequirement.TypeToConfirm;
        CurrentAction.ConsequenceStatement = consequenceStatement;
        return this;
    }

    /// <inheritdoc />
    public IActionStepBuilder<TItem> EnabledWhen(Expression<Func<TItem, bool>> predicate)
    {
        CurrentAction.EnabledWhenFieldId = ExpressionPaths.FieldId(predicate);
        return this;
    }

    /// <inheritdoc />
    public IActionStepBuilder<TItem> Parameter(
        string parameterId,
        string name,
        bool defaultValue = false,
        bool required = false)
    {
        CurrentAction.Parameters.Add(new ActionParameter(
            parameterId,
            name,
            FieldValueKind.Boolean,
            required,
            [],
            defaultValue.ToString(CultureInfo.InvariantCulture)));

        return this;
    }

    /// <inheritdoc />
    public IActionStepBuilder<TItem> Parameter(
        string parameterId,
        string name,
        IdentifierRole role,
        bool required = false)
    {
        CurrentAction.Parameters.Add(new ActionParameter(
            parameterId,
            name,
            FieldValueKind.ExternalIdentifier,
            required,
            [],
            null,
            $"identity.{DerivedNames.Identifier(role.ToString())}"));

        return this;
    }

    /// <inheritdoc />
    public IActionStepBuilder<TItem> Parameter<TEnum>(string parameterId, string name, TEnum defaultValue)
        where TEnum : struct, Enum
    {
        CurrentAction.Parameters.Add(new ActionParameter(
            parameterId,
            name,
            FieldValueKind.Enumerated,
            false,
            [.. EnumOrder.Names(typeof(TEnum))
                .Select(static member => new FacetValue(
                    DerivedNames.Identifier(member),
                    DerivedNames.Label(member)))],
            DerivedNames.Identifier(defaultValue.ToString() ?? string.Empty)));

        return this;
    }

    /// <inheritdoc />
    public IActionStepBuilder<TItem> Parameter(IDeclaredSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var facet = Declaration.Selections
            .Single(draft => string.Equals(draft.FacetId, selection.FacetId, StringComparison.Ordinal));

        CurrentAction.Parameters.Add(new ActionParameter(
            facet.FacetId,
            facet.Name,
            facet.Kind == SelectionFacetKind.Enumerated ? FieldValueKind.Enumerated : FieldValueKind.Decimal,
            false,
            facet.Values,
            facet.DefaultAllowed.Count > 0 ? facet.DefaultAllowed[0] : null,
            $"selection.{facet.FacetId}"));

        return this;
    }

    // ---- quality -------------------------------------------------------------------------------------

    /// <inheritdoc />
    public IQualityBuilder<TItem> IgnoreStatedResolutionFor(params string[] sourceGroups)
    {
        ArgumentNullException.ThrowIfNull(sourceGroups);

        foreach (var sourceGroup in sourceGroups)
        {
            Declaration.QualityDefaults.Add(new TierDefault
            {
                When = new TagPredicate(
                [
                    new PredicateAtom
                    {
                        Subject = "tags.sourceGroup",
                        Op = PredicateOp.Equals,
                        Values = [sourceGroup]
                    }
                ]),
                IgnoreStatedResolution = true
            });
        }

        return this;
    }

    /// <inheritdoc />
    public IQualityBuilder<TItem> FallbackRoundUp()
    {
        Declaration.QualityFallback = RungFallback.RoundUp;
        return this;
    }

    /// <inheritdoc />
    public IQualityBuilder<TItem> FallbackNearest()
    {
        Declaration.QualityFallback = RungFallback.Nearest;
        return this;
    }

    private QueryTierDraft CurrentTier =>
        _tier ?? throw new InvalidOperationException(
            "A query tier's refinement was written before any tier was declared.");

    private ActionDraft CurrentAction =>
        _action ?? throw new InvalidOperationException(
            "An action's refinement was written before any action was declared.");
}
