using System.Collections.Frozen;
using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;

namespace Arronix.Host.Media;

/// <summary>
/// Copies the derived runtime model into host-owned values before the host retains and runs it.
/// </summary>
/// <remarks>
/// <para>
/// The host's parser, matcher, query planner and namer read this model after admission and outside any
/// invocation lease, so an extension-supplied collection in it would execute extension code with no ticket
/// held and would keep its collectible context alive until the kind is withdrawn. Making the model
/// internal hides it; it does not change who owns the objects, so every collection is rebuilt instead.
/// </para>
/// <para>
/// Two members are delegates by contract and cannot be copied; both ultimately run author-supplied code.
/// <see cref="MediaKindModel.Respace"/> is the media kind's own and is captured by the declarative release
/// parser. Each <c>TemplateRequirement.IsSatisfied</c> is a host adapter that closes over and calls the
/// author's own predicate, and no production path invokes one today. The lists holding both are rebuilt;
/// the delegates are retained with the kind and released with it.
/// </para>
/// </remarks>
internal static class ModelBoundary
{
    /// <summary>Copies a derived model and everything under it.</summary>
    /// <param name="model">What was derived from the extension's declaration.</param>
    /// <returns>The model, built from host-owned collections.</returns>
    internal static MediaKindModel Snapshot(MediaKindModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return model with
        {
            Parsing = model.Parsing is null ? null : Snapshot(model.Parsing),
            Matching = Snapshot(model.Matching),
            Querying = Snapshot(model.Querying),
            Naming = Snapshot(model.Naming),
            Notifications = Snapshot(model.Notifications),
            TemplateRules = [.. model.TemplateRules],
        };
    }

    private static ParseDeclaration Snapshot(ParseDeclaration parsing)
        => parsing with
        {
            Normalization = Snapshot(parsing.Normalization),
            PreRewrites = [.. parsing.PreRewrites],
            TitlePatterns = [.. parsing.TitlePatterns.Select(Snapshot)],
            Guards = [.. parsing.Guards],
            TokenTables = [.. parsing.TokenTables.Select(Snapshot)],
            EscapeIds = [.. parsing.EscapeIds],
        };

    private static NormalizationOptions Snapshot(NormalizationOptions options)
        => options with
        {
            LeadingArticles = [.. options.LeadingArticles],
            StopWords = [.. options.StopWords],
            Transliterations = [.. options.Transliterations],
            QueryRewrites = [.. options.QueryRewrites],
        };

    private static TitlePattern Snapshot(TitlePattern pattern)
        => pattern with
        {
            Sources = [.. pattern.Sources],
            Captures = [.. pattern.Captures],
            Guards = [.. pattern.Guards],
        };

    private static TokenTable Snapshot(TokenTable table)
        => table with { Rows = [.. table.Rows] };

    private static MatchDeclaration Snapshot(MatchDeclaration matching)
        => matching with
        {
            Entry = Snapshot(matching.Entry),
            Units = [.. matching.Units.Select(Snapshot)],
            Confidence = [.. matching.Confidence.Select(Snapshot)],
            Variant = matching.Variant is null ? null : Snapshot(matching.Variant),
        };

    private static EntryResolution Snapshot(EntryResolution entry)
        => entry with
        {
            IdentifierOrder = [.. entry.IdentifierOrder],
            Layers = [.. entry.Layers.Select(Snapshot)],
            Agreements = [.. entry.Agreements.Select(Snapshot)],
        };

    private static MatchLayer Snapshot(MatchLayer layer)
        => layer with { ExpanderIds = [.. layer.ExpanderIds] };

    private static AgreementRule Snapshot(AgreementRule rule)
        => rule with { AgreesWith = [.. rule.AgreesWith] };

    private static UnitResolutionRule Snapshot(UnitResolutionRule rule)
        => rule with { Spaces = [.. rule.Spaces] };

    /// <remarks>A record struct still carries a reference to whatever collection was put in it.</remarks>
    private static ConfidenceRule Snapshot(ConfidenceRule rule)
        => rule.SourceIn is null ? rule : rule with { SourceIn = [.. rule.SourceIn] };

    private static VariantChoiceDeclaration Snapshot(VariantChoiceDeclaration variant)
        => variant with { Features = [.. variant.Features] };

    private static QueryDeclaration Snapshot(QueryDeclaration querying)
        => querying with
        {
            Tiers = [.. querying.Tiers.Select(Snapshot)],
            Aliases = [.. querying.Aliases],
            Grammar = Snapshot(querying.Grammar),
            Limits = [.. querying.Limits],
            Substitutions = [.. querying.Substitutions],
        };

    private static QueryTierTemplate Snapshot(QueryTierTemplate tier)
        => tier with
        {
            Origins = [.. tier.Origins],
            Arguments = [.. tier.Arguments],
            RequiredFields = [.. tier.RequiredFields],
        };

    private static CoordinateGrammar Snapshot(CoordinateGrammar grammar)
        => grammar with { Spellings = [.. grammar.Spellings] };

    private static NamingDeclaration Snapshot(NamingDeclaration naming)
        => naming with
        {
            DefaultTemplates = Frozen(naming.DefaultTemplates),
            Selection = [.. naming.Selection.Select(Snapshot)],
            MultiUnitStyles = [.. naming.MultiUnitStyles],
            Fallbacks = [.. naming.Fallbacks.Select(Snapshot)],
        };

    private static TemplateSelectionRule Snapshot(TemplateSelectionRule rule)
        => rule with { When = Snapshot(rule.When) };

    private static TagPredicate Snapshot(TagPredicate predicate)
        => predicate with { All = [.. predicate.All.Select(Snapshot)] };

    private static PredicateAtom Snapshot(PredicateAtom atom)
        => atom with { Values = [.. atom.Values] };

    private static TokenFallbackRule Snapshot(TokenFallbackRule rule)
        => rule with { Order = [.. rule.Order] };

    private static NotificationDeclaration Snapshot(NotificationDeclaration notifications)
        => notifications with
        {
            Fields = [.. notifications.Fields],
            GroupSummaries = [.. notifications.GroupSummaries.Select(Snapshot)],
        };

    private static GroupSummaryRule Snapshot(GroupSummaryRule rule)
        => rule with { Fields = [.. rule.Fields] };

    private static IReadOnlyDictionary<string, TValue> Frozen<TValue>(
        IReadOnlyDictionary<string, TValue>? values)
        => values is null or { Count: 0 }
            ? FrozenDictionary<string, TValue>.Empty
            : values.ToFrozenDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
