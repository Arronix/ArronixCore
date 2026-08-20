using System.Linq;
using System.Linq.Expressions;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;

using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed.Compilation;


namespace Arronix.Host.Media.Typed;

/// <summary>Compiles one media definition object's typed values into the host runtime draft.</summary>
/// <remarks>
/// This is ordinary dispatch over values returned by
/// <see cref="MediaType{TItem,TTarget,TRelease,TParser}"/>.
/// Media definitions do not advertise configuration by implementing closed capability interfaces and the
/// host does not reflect over their inheritance lists.
/// </remarks>
internal static class MediaDefinitionCompiler
{
    private const string CleanTitleNormalizer = "clean-title";
    private const string RomanNumeralExpander = "roman-numeral-variants";

    internal static void Apply<TItem, TTarget, TRelease, TParser>(
        TypedDeclaration declaration,
        MediaType<TItem, TTarget, TRelease, TParser> definition)
        where TItem : class, IMediaItem
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
        where TParser : IReleaseParser<TRelease>
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(definition);

        declaration.CompiledShapes = definition.CompiledShapes;
        declaration.Singular = definition.SingularName;
        declaration.Plural = definition.PluralName;
        declaration.FilesBindOnePerItem = definition.Files == FileBindingDefinition.OnePerItem;

        var formatCompiler = new FormatCompiler(declaration);
        foreach (var format in definition.Formats)
        {
            format.Accept(formatCompiler);
        }

        foreach (var role in definition.Identity.RequiredRoles)
        {
            declaration.Identity.Required.Add(role);
        }

        foreach (var role in definition.Identity.AdmittedRoles)
        {
            declaration.Identity.Admitted.Add(role);
        }

        var groupCompiler = new GroupCompiler<TItem>(declaration);
        foreach (var group in definition.Groups)
        {
            group.Accept(groupCompiler);
        }

        var selectionCompiler = new SelectionCompiler<TItem>(declaration);
        definition.Availability.Accept(selectionCompiler);
        declaration.AvailabilitySelectionId = declaration.Selections[^1].FacetId;
        foreach (var selection in definition.AdditionalSelections)
        {
            selection.Accept(selectionCompiler);
        }

        foreach (var search in definition.Searches)
        {
            var draft = new SearchDraft(search.Id, search.Name);
            draft.Required.AddRange(search.RequiredTerms);
            draft.Optional.AddRange(search.OptionalTerms);
            declaration.Searches.Add(draft);
        }

        foreach (var layer in definition.Matching.Layers)
        {
            declaration.MatchLayers.Add(new MatchLayer
            {
                LayerId = layer.Id,
                KeyTemplate = ExpressionPaths.KeyTemplate(layer.Keys),
                NormalizerId = CleanTitleNormalizer,
                ExpanderIds = layer.Expansion == KeyExpansion.RomanNumerals
                    ? [RomanNumeralExpander]
                    : []
            });
        }

        var agreementCompiler = new AgreementCompiler<TItem>(declaration);
        foreach (var agreement in definition.Matching.Agreements)
        {
            agreement.Accept(agreementCompiler);
        }

        declaration.ScopeReplacesSearch = definition.Matching.ScopeReplacesSearch;
        declaration.Ambiguity = definition.Matching.Ambiguity;
        declaration.ReleasePolicy = definition.ReleasePolicy;

        CompileQuery<TItem>(declaration, definition.Querying);
        CompileNaming<TItem>(declaration, definition.Naming);
        CompileSummary<TItem>(declaration, definition.Summary);
        CompileIntent<TItem>(declaration, definition.Intent);
        CompileWorkbenches<TItem>(declaration, definition.Workbenches);

        var derivationCompiler = new DerivationCompiler<TItem>(declaration);
        foreach (var derivation in definition.Derivations)
        {
            derivation.Accept(derivationCompiler);
        }

    }

    private static void CompileQuery<TItem>(
        TypedDeclaration declaration,
        QueryDefinition<TItem> query)
        where TItem : class, IMediaItem
    {
        foreach (var tier in query.Tiers)
        {
            var draft = new QueryTierDraft(tier.Id, tier.SearchId, declaration.Tiers.Count + 1)
            {
                FreeTextTemplate = tier.FreeText is null ? string.Empty : ExpressionPaths.Template(tier.FreeText),
                FanOutPerAlias = tier.FanOutPerAlias,
                CarryAliases = tier.CarryAliases
            };
            draft.Origins.AddRange(tier.Origins);
            draft.RequiredRoles.AddRange(tier.RequiredIdentityRoles);
            draft.RequiredFields.AddRange(tier.Requirements.Select(requirement =>
                ExpressionPaths.FieldId(requirement.Property)));

            foreach (var argument in tier.Arguments)
            {
                var template = argument.IdentityRole is { } role
                    ? $"{{identity.{DerivedNames.Identifier(role.ToString())}}}"
                    : $"{{{ExpressionPaths.FieldId(argument.Property!)}}}";
                draft.Arguments.Add(new QueryArgument(argument.Term, template, null, argument.OmitWhenAbsent));
            }

            if (tier.HasNoTerms)
            {
                draft.FreeTextTemplate = string.Empty;
                draft.Arguments.Clear();
            }

            declaration.Tiers.Add(draft);
        }

        foreach (var alias in query.Aliases)
        {
            declaration.Aliases.Add(new AliasTemplate
            {
                AliasId = alias.Id,
                Template = ExpressionPaths.KeyTemplate(alias.Spellings),
                Order = declaration.Aliases.Count + 1,
                FilterByAcceptedLanguages = alias.FilterByAcceptedLanguages,
                NeverOwnQuery = alias.NeverOwnQuery
            });
        }
    }

    private static void CompileNaming<TItem>(
        TypedDeclaration declaration,
        NamingDefinition<TItem> naming)
        where TItem : class, IMediaItem
    {
        if (naming.FileTemplate is { } file)
        {
            declaration.Templates["file"] = file;
        }

        if (naming.FolderTemplate is { } folder)
        {
            declaration.Templates["folder"] = folder;
        }

        foreach (var groupFolder in naming.GroupFolders)
        {
            declaration.Templates[$"{GroupAxisId(declaration, groupFolder.GroupType)}-folder"] =
                groupFolder.Template;
        }

        declaration.FolderSpine = naming.FolderSpine;

        foreach (var selection in naming.GroupSelections)
        {
            var axisId = GroupAxisId(declaration, selection.GroupType);
            declaration.TemplateSelection.Add(new TemplateSelectionRule
            {
                RuleId = selection.RuleId,
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
        }

        foreach (var requirement in naming.Requirements)
        {
            declaration.TemplateRules.Add(new TemplateRequirement(
                requirement.Id,
                requirement.Requirement,
                facts => requirement.IsSatisfied(new NamingTemplateFactsAdapter<TItem>(facts))));
        }

        foreach (var fallback in naming.Fallbacks)
        {
            declaration.TokenFallbacks.Add(new TokenFallbackRule
            {
                Token = ExpressionPaths.FieldId(fallback.Property),
                Order = [.. fallback.Order.Select(fact => $"file.{DerivedNames.Identifier(fact.ToString())}")]
            });
        }

        if (naming.EmptyResultFallback is { } empty)
        {
            declaration.TokenFallbacks.Add(new TokenFallbackRule
            {
                Token = string.Empty,
                Order = [$"file.{DerivedNames.Identifier(empty.ToString())}"]
            });
        }
    }

    private static void CompileSummary<TItem>(
        TypedDeclaration declaration,
        SummaryDefinition<TItem> summary)
        where TItem : class, IMediaItem
    {
        if (summary.Headline is { } headline)
        {
            declaration.HeadlineTemplate = ExpressionPaths.Template(headline);
            declaration.HeadlineMaxLength = summary.HeadlineMaxLength;
        }

        if (summary.Body is { } body)
        {
            declaration.BodyFieldId = ExpressionPaths.FieldId(body);
            declaration.BodyMaxLength = summary.BodyMaxLength;
        }

        foreach (var field in summary.Fields)
        {
            var paths = ExpressionPaths.Paths(field.Value);
            declaration.SummaryFields.Add(new SummaryFieldRule(
                field.Label,
                string.Concat(paths.Select(path => $"{{{path}}}")),
                field.Weight));
        }

        foreach (var group in summary.Groups)
        {
            declaration.GroupSummaries.Add(new GroupSummaryRule
            {
                AxisId = GroupAxisId(declaration, group.GroupType),
                HeadlineTemplate = ExpressionPaths.Template(group.Headline),
                Fields =
                [
                    .. group.Fields.Select(field => new SummaryFieldRule(
                        field.Label,
                        string.Concat(ExpressionPaths.Paths(field.Value).Select(path => $"{{{path}}}")),
                        field.Weight))
                ]
            });
        }
    }

    private static void CompileIntent<TItem>(
        TypedDeclaration declaration,
        IntentDefinition<TItem> intent)
        where TItem : class, IMediaItem
    {
        declaration.DefaultBrowseAxisId = intent.DefaultBrowseId;
        declaration.DefaultBrowseName = intent.DefaultBrowseName;

        foreach (var sort in intent.Sorts)
        {
            declaration.SortOverrides[ExpressionPaths.FieldId(sort.Property)] =
                sort.Ascending ? SortDirection.Ascending : SortDirection.Descending;
        }

        foreach (var hidden in intent.HiddenBrowseFields)
        {
            declaration.HiddenAxisFieldIds.Add(ExpressionPaths.FieldId(hidden.Property));
        }

        foreach (var state in intent.StateTones)
        {
            declaration.StateTones[DerivedNames.Identifier(state.State.ToString())] = state.Tone;
        }
    }

    private static void CompileWorkbenches<TItem>(
        TypedDeclaration declaration,
        IReadOnlyList<IWorkbenchDefinition<TItem>> workbenches)
        where TItem : class, IMediaItem
    {
        foreach (var workbench in workbenches)
        {
            var draft = new WorkbenchDraft(workbench.Id, workbench.Name, workbench.RowType)
            {
                Subject = workbench.Subject,
                CommitLabel = workbench.CommitLabel,
                CommitConsequence = workbench.CommitConsequence
            };

            foreach (var input in workbench.Inputs)
            {
                draft.Inputs.Add(input.IdentityRole is { } role
                    ? new ActionParameter(
                        input.Id,
                        input.Name,
                        FieldValueKind.ExternalIdentifier,
                        false,
                        [],
                        null,
                        $"identity.{DerivedNames.Identifier(role.ToString())}")
                    : new ActionParameter(input.Id, input.Name, FieldValueKind.Text, true, []));
            }

            declaration.Workbenches.Add(draft);
        }
    }

    private static string GroupAxisId(TypedDeclaration declaration, Type groupType) =>
        declaration.Groups.SingleOrDefault(group => group.GroupType == groupType)?.AxisId
        ?? throw new InvalidOperationException(
            $"Group '{groupType.Name}' was used before it was declared.");

    private sealed class FormatCompiler(TypedDeclaration declaration) : IFormatUseVisitor
    {
        public void Visit<TRepresentation>(FormatUse<TRepresentation> use)
            where TRepresentation : class, IRepresentation
        {
            var draft = new FormatFamilyDraft(use.Family.Id, use.Family.Name)
            {
                RepresentationType = typeof(TRepresentation),
                SupportsEmbeddedMetadata = use.SupportsEmbeddedMetadata,
                CoexistsWithOtherFamilies = use.CoexistsWithOtherFamilies
            };
            draft.Extensions.AddRange(use.Family.FileExtensions);
            declaration.Formats.Add(draft);
        }
    }

    private sealed class GroupCompiler<TItem>(TypedDeclaration declaration) : IGroupDefinitionVisitor<TItem>
        where TItem : class, IMediaItem
    {
        public void Visit<TGroup>(GroupDefinition<TItem, TGroup> group)
            where TGroup : class, IMediaGroup<TItem>
        {
            var draft = new GroupDraft(
                typeof(TGroup),
                ExpressionPaths.DirectMemberName(group.Memberships),
                DerivedNames.Identifier(group.SingularName))
            {
                Singular = group.SingularName,
                Plural = group.PluralName,
                IsMonitorable = group.IsMonitorable,
                IsDiscoverySource = group.IsDiscoverySource,
                Lifetime = group.Lifetime,
                Arity = GroupingArity.ManyToMany
            };
            declaration.Groups.Add(draft);
        }
    }

    private sealed class SelectionCompiler<TItem>(TypedDeclaration declaration) : ISelectionDefinitionVisitor<TItem>
        where TItem : class, IMediaItem
    {
        public void Visit<TValue>(OrderedSelectionDefinition<TItem, TValue> selection)
            where TValue : struct, Enum
        {
            var offered = selection.OfferedValues.Count == 0
                ? Enum.GetValues<TValue>().Where(value => Convert.ToInt64(value) >= 0).ToArray()
                : [.. selection.OfferedValues];

            var draft = new SelectionDraft(
                ExpressionPaths.FieldId(selection.Property),
                selection.Name,
                SelectionFacetKind.Enumerated)
            {
                EnumType = typeof(TValue),
                Application = selection.Application
            };
            draft.DefaultAllowed.Add(DerivedNames.Identifier(selection.DefaultFloor.ToString()));
            draft.Values.AddRange(offered.Select(member => new FacetValue(
                DerivedNames.Identifier(member.ToString()),
                DerivedNames.Label(member.ToString()))));
            declaration.Selections.Add(draft);
        }

        public void Visit(ThresholdSelectionDefinition<TItem> selection)
        {
            var draft = new SelectionDraft(
                selection.FacetId,
                selection.Name,
                SelectionFacetKind.Threshold)
            {
                Unit = selection.Unit,
                ThresholdDirection = selection.Direction,
                DefaultNumber = selection.DefaultBound,
                Application = selection.Application
            };
            declaration.Selections.Add(draft);
        }
    }

    private sealed class AgreementCompiler<TItem>(TypedDeclaration declaration) : IMatchAgreementVisitor<TItem>
        where TItem : class, IMediaItem
    {
        public void Visit<TValue>(MatchAgreement<TItem, TValue> agreement)
            where TValue : struct => declaration.Agreements.Add(new AgreementRule
            {
                RuleId = DerivedNames.Identifier(agreement.Reading.ToString()),
                Subject = $"reading.{agreement.Reading}",
                AgreesWith = [.. ExpressionPaths.Paths(agreement.CandidateValues)],
                AbsentAgrees = agreement.WhenAbsent == Agreement.Accept,
                MinimumValue = agreement.Floor
            });
    }

    private sealed class DerivationCompiler<TItem>(TypedDeclaration declaration)
        : IDerivationDefinitionVisitor<TItem>
        where TItem : class, IMediaItem
    {
        public void Visit<TValue>(DerivationDefinition<TItem, TValue> derivation) =>
            declaration.Derivations.Add(new DerivationBinding(
                ExpressionPaths.DirectMemberName(derivation.Property),
                item => derivation.Recompute((TItem)item)));
    }

    private sealed class NamingTemplateFactsAdapter<TItem>(INamingTemplateFacts facts)
        : INamingTemplateFacts<TItem>
        where TItem : IMediaItem
    {
        public bool Has<TValue>(Expression<Func<TItem, TValue>> property) =>
            facts.HasField(ExpressionPaths.FieldId(property));

        public bool Has(FileFact fact) => facts.Has(fact);
    }
}
