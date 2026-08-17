using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

// The declaration and shape contracts the engine executes are experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

namespace Arronix.Host.Engines.Search;

/// <summary>
/// The declarative query templater: one instance per validated definition, implementing the existing
/// planner seam so downstream dispatch cannot tell a declared kind from a hand-written one.
/// </summary>
/// <remarks>
/// <para>
/// Identifier tiers before text tiers, and a text query never made without its declared required fields —
/// the fallback chain every surveyed release source hand-rolls, expressed as ordered
/// <c>QueryTierTemplate</c> rows. Tiers sharing an order value land in one plan tier and are dispatched
/// together; the first tier that yields any release wins, and that stop rule is the dispatcher's, not
/// the planner's.
/// </para>
/// <para>
/// A tier participates in a request when it implements the requested search kind, or one that targets
/// the same level at the same scope — which is how an identifier tier declared against the
/// identifier-search kind joins the plan of the plain search that falls back to text. Origin-specific
/// tiers displace general ones for their origin, so a sweep tier declared for the periodic origin
/// replaces the identifier and text tiers there rather than joining them.
/// </para>
/// <para>
/// Alias spellings come from the declared alias rows, most canonical first, and are supplied by the
/// declaration, never invented by the host: a fan-out tier plans one query per spelling by substituting
/// the spelling for the title token; rows marked never-own-query ride along as aliases only. Rows
/// filtered by accepted languages emit a language-tagged spelling only when the acquisition accepts its
/// language — what makes translated-spelling fan-out affordable.
/// </para>
/// </remarks>
internal sealed class DeclarativeQueryPlanner : IReleaseQueryPlanner
{
    private readonly QueryDeclaration _declaration;
    private readonly IQueryItemReader _reader;
    private readonly Dictionary<string, SearchKind> _searchKinds;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeclarativeQueryPlanner"/> class.
    /// </summary>
    /// <param name="mediaKind">The kind the engine serves.</param>
    /// <param name="declaration">The kind's query declaration.</param>
    /// <param name="searchKinds">The search kinds the kind's shape declares.</param>
    /// <param name="reader">The item read window.</param>
    /// <exception cref="InvalidOperationException">A tier names an undeclared search kind.</exception>
    internal DeclarativeQueryPlanner(
        MediaKindId mediaKind,
        QueryDeclaration declaration,
        IReadOnlyList<SearchKind> searchKinds,
        IQueryItemReader reader)
    {
        MediaKind = mediaKind;
        _declaration = declaration;
        _reader = reader;
        _searchKinds = searchKinds.ToDictionary(kind => kind.SearchKindId, StringComparer.Ordinal);

        foreach (var tier in declaration.Tiers)
        {
            if (!_searchKinds.ContainsKey(tier.SearchKindId))
            {
                throw new InvalidOperationException(
                    $"Tier '{tier.TierId}' names undeclared search kind '{tier.SearchKindId}'.");
            }
        }
    }

    /// <inheritdoc />
    public MediaKindId MediaKind { get; }

    /// <inheritdoc />
    public async Task<ReleaseQueryPlan> PlanAsync(
        AcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_searchKinds.TryGetValue(request.SearchKindId, out var requested))
        {
            throw new InvalidOperationException(
                $"'{request.SearchKindId}' names no search kind this definition declares.");
        }

        var limit = ResolveLimit(request.Origin);
        var templates = SelectTemplates(requested, request.Origin);

        var tiers = new List<ReleaseQueryTier>();
        foreach (var group in templates.GroupBy(template => template.Order).OrderBy(group => group.Key))
        {
            var queries = new List<ReleaseQuery>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var template in group)
            {
                foreach (var unit in request.Units)
                {
                    var item = await _reader.GetAsync(unit, cancellationToken).ConfigureAwait(false);
                    if (item is null)
                    {
                        continue;
                    }

                    foreach (var query in PlanTemplate(template, item, request, limit))
                    {
                        var fingerprint = Fingerprint(query);
                        if (seen.Add(fingerprint))
                        {
                            queries.Add(query);
                        }
                    }
                }
            }

            if (queries.Count > 0)
            {
                tiers.Add(new ReleaseQueryTier(queries));
            }
        }

        return new ReleaseQueryPlan(tiers);
    }

    private IEnumerable<QueryTierTemplate> SelectTemplates(SearchKind requested, SearchOrigin origin)
    {
        var eligible = _declaration.Tiers
            .Where(tier => Implements(tier, requested))
            .ToArray();

        var originSpecific = eligible
            .Where(tier => tier.Origins.Count > 0 && tier.Origins.Contains(origin))
            .ToArray();

        return originSpecific.Length > 0
            ? originSpecific
            : eligible.Where(tier => tier.Origins.Count == 0);
    }

    private bool Implements(QueryTierTemplate tier, SearchKind requested)
    {
        if (string.Equals(tier.SearchKindId, requested.SearchKindId, StringComparison.Ordinal))
        {
            return true;
        }

        var declared = _searchKinds[tier.SearchKindId];
        return declared.TargetLevelId == requested.TargetLevelId && declared.Scope == requested.Scope;
    }

    private int? ResolveLimit(SearchOrigin origin)
    {
        foreach (var limit in _declaration.Limits)
        {
            if (limit.Origin == origin)
            {
                return limit.Limit;
            }
        }

        return null;
    }

    private IEnumerable<ReleaseQuery> PlanTemplate(
        QueryTierTemplate template,
        ItemView item,
        AcquisitionRequest request,
        int? limit)
    {
        var context = new QueryTemplateContext
        {
            Item = item,
            Grammar = _declaration.Grammar,
            Substitutions = _declaration.Substitutions,
        };

        foreach (var required in template.RequiredFields)
        {
            if (!item.Fields.TryGetValue(required, out var field) || field.IsAbsent)
            {
                yield break;
            }
        }

        if (!TryBuildArguments(template, item, context, out var arguments))
        {
            yield break;
        }

        var spellings = ResolveSpellings(context, request.AcceptedLanguages);
        var carried = template.CarryAliases
            ? spellings.Select(spelling => spelling.Text).Distinct(StringComparer.Ordinal).ToArray()
            : [];

        var categories = _searchKinds[template.SearchKindId].Categories;

        if (template.FanOutPerAlias)
        {
            var subjects = spellings
                .Where(spelling => !spelling.NeverOwnQuery)
                .Select(spelling => spelling.Text)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (var subject in subjects)
            {
                yield return BuildQuery(
                    template,
                    QueryTemplateRenderer.RenderLenient(
                        template.FreeTextTemplate,
                        context with { TitleOverride = subject }),
                    carried,
                    arguments,
                    categories,
                    request,
                    limit);
            }

            yield break;
        }

        yield return BuildQuery(
            template,
            QueryTemplateRenderer.RenderLenient(template.FreeTextTemplate, context),
            carried,
            arguments,
            categories,
            request,
            limit);
    }

    private static bool TryBuildArguments(
        QueryTierTemplate template,
        ItemView item,
        QueryTemplateContext context,
        out IReadOnlyList<SearchArgument> arguments)
    {
        var built = new List<SearchArgument>();

        foreach (var argument in template.Arguments)
        {
            if (argument.Term == SearchTerm.ExternalIdentifier && argument.Scheme is { } scheme)
            {
                var found = false;
                foreach (var externalId in item.ExternalIds)
                {
                    if (string.Equals(externalId.Scheme, scheme, StringComparison.Ordinal))
                    {
                        built.Add(new SearchArgument(argument.Term, FieldValue.OfExternalIdentifier(externalId)));
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    continue;
                }
            }
            else if (QueryTemplateRenderer.TryResolveBareField(argument.Template, context, out var fieldValue))
            {
                built.Add(new SearchArgument(argument.Term, fieldValue));
                continue;
            }
            else
            {
                var rendered = QueryTemplateRenderer.RenderLenient(argument.Template, context);
                if (rendered.Length > 0)
                {
                    built.Add(new SearchArgument(argument.Term, FieldValue.OfText(rendered)));
                    continue;
                }
            }

            if (!argument.OmitWhenAbsent)
            {
                arguments = [];
                return false;
            }
        }

        arguments = built;
        return true;
    }

    private IReadOnlyList<(string Text, bool NeverOwnQuery)> ResolveSpellings(
        QueryTemplateContext context,
        IReadOnlyList<Language> acceptedLanguages)
    {
        var spellings = new List<(string Text, bool NeverOwnQuery)>();

        foreach (var alias in _declaration.Aliases.OrderBy(alias => alias.Order))
        {
            foreach (var value in QueryTemplateRenderer.Render(alias.Template, context))
            {
                if (alias.FilterByAcceptedLanguages
                    && value.Language is { } language
                    && acceptedLanguages.Count > 0
                    && !acceptedLanguages.Any(accepted =>
                        string.Equals(accepted.Code, language.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                spellings.Add((value.Text, alias.NeverOwnQuery));
            }
        }

        return spellings;
    }

    private ReleaseQuery BuildQuery(
        QueryTierTemplate template,
        string freeText,
        IReadOnlyList<string> aliases,
        IReadOnlyList<SearchArgument> arguments,
        IReadOnlyList<CategoryId> categories,
        AcquisitionRequest request,
        int? limit)
    {
        var query = new ReleaseQuery
        {
            MediaKind = MediaKind,
            SearchKindId = template.SearchKindId,
            FreeText = freeText,
            Aliases = aliases,
            Arguments = arguments,
            Categories = categories,
            Origin = request.Origin,
        };

        return limit is { } bounded ? query with { Limit = bounded } : query;
    }

    private static string Fingerprint(ReleaseQuery query)
    {
        var arguments = string.Join(
            "|",
            query.Arguments.Select(argument =>
                $"{argument.Term}={argument.Value.Text ?? argument.Value.External?.ToString() ?? argument.Value.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));

        return $"{query.SearchKindId}\n{query.FreeText}\n{arguments}";
    }
}
