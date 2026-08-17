using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Media;

// Definition and shape contracts are experimental; this file is the host's gate over the definition area.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

#pragma warning disable ARX0020 // The typed media surface is experimental; this file consumes it.

namespace Arronix.Host.Media;

/// <summary>
/// Every cross-reference check a media kind's derived model must pass before engines are built from it.
/// </summary>
/// <remarks>
/// <para>
/// The rule is the design's §1.3, applied to the whole aggregate: every identifier a section uses must
/// resolve against the section that declares it, at load, or the extension is refused with a diagnostic
/// naming the row. A title-pattern capture naming an undeclared coordinate component, a rung rule naming a
/// tier absent from every ladder, a query tier naming an undeclared search, a strategy binding naming a
/// strategy this host does not carry — all are load failures here, never runtime surprises in an engine.
/// </para>
/// <para>
/// Deliberately not checked: the embedded template micro-languages (key templates, alias templates,
/// naming templates, folder spines, JSON paths). Their grammars belong to the engines that compile them,
/// and each engine reports its own defects when it binds; duplicating half-understood grammar checks here
/// would refuse definitions the engine would accept. Declared order is likewise untouched: nothing in this
/// file sorts, because rule order is the algorithm.
/// </para>
/// </remarks>
internal static class DefinitionValidationRules
{
    private static readonly TimeSpan PatternProbeTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Checks everything above the shape, which the caller has already validated.
    /// </summary>
    /// <param name="model">The kind's derived model.</param>
    /// <param name="shape">Its derived, already-validated structure.</param>
    /// <param name="defects">Where faults are recorded. Every fault, not the first.</param>
    internal static void Check(
        MediaKindModel model,
        ValidatedShape shape,
        List<ShapeDefect> defects)
    {
        var tierIds = CollectTierIds(shape);
        var schemeIds = CollectSchemeIds(shape);
        var guards = CheckGuards(model.Parsing, defects);
        var patternIds = CheckTitlePatterns(model.Parsing, shape, guards, schemeIds, defects);
        CheckTokenTables(model.Parsing, defects);
        CheckRungResolution(model.Parsing.RungResolution, tierIds, guards, defects);
        CheckEscapes(model.Parsing.EscapeIds, defects);
        var releaseKinds = CollectReleaseKinds(model.Parsing);
        CheckMatching(model.Matching, shape, releaseKinds, schemeIds, defects);
        CheckQuerying(model.Querying, shape, defects);
        CheckQuality(model.Quality, guards, defects);
        CheckNaming(model.Naming, guards, defects);
        CheckCatalog(model.Catalog, shape, schemeIds, defects);
        CheckNotifications(model.Notifications, shape, defects);
        CheckCorpus(model.Corpus, patternIds, tierIds, defects);
    }

    private static HashSet<string> CollectTierIds(ValidatedShape shape)
    {
        var tiers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var family in shape.Declaration.FormatFamilies)
        {
            tiers.Add(family.Unknown.Name);

            foreach (var tier in family.Ladder)
            {
                tiers.Add(tier.Name);
            }
        }

        return tiers;
    }

    private static HashSet<string> CollectSchemeIds(ValidatedShape shape)
    {
        var schemes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var level in shape.Declaration.Levels)
        {
            foreach (var scheme in level.Identity.ExternalIds)
            {
                schemes.Add(scheme.Scheme);
            }
        }

        foreach (var axis in shape.Declaration.GroupingAxes)
        {
            foreach (var scheme in axis.ExternalIds)
            {
                schemes.Add(scheme.Scheme);
            }
        }

        return schemes;
    }

    private static HashSet<string> CollectReleaseKinds(ParseDeclaration parsing)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pattern in parsing.TitlePatterns)
        {
            foreach (var capture in pattern.Captures)
            {
                if (capture.Target == CaptureTarget.ReleaseKind && capture.Key is { } key)
                {
                    kinds.Add(key);
                }
            }
        }

        return kinds;
    }

    private static HashSet<string> CheckGuards(ParseDeclaration parsing, List<ShapeDefect> defects)
    {
        var guards = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < parsing.Guards.Count; i++)
        {
            var guard = parsing.Guards[i];
            var path = Path("parsing.guards", i);

            if (string.IsNullOrWhiteSpace(guard.GuardId))
            {
                Report(defects, path, "A guard declares the identifier its references use.");
            }
            else if (!guards.Add(guard.GuardId))
            {
                Report(defects, path, $"Guard '{guard.GuardId}' is declared more than once.");
            }

            ProbePattern(guard.Regex, path, defects);
        }

        return guards;
    }

    private static HashSet<string> CheckTitlePatterns(
        ParseDeclaration parsing,
        ValidatedShape shape,
        HashSet<string> guards,
        HashSet<string> schemeIds,
        List<ShapeDefect> defects)
    {
        var patternIds = new HashSet<string>(StringComparer.Ordinal);

        if (parsing.TitlePatterns.Count == 0)
        {
            Report(defects, "parsing.titlePatterns", "A parse declaration carries at least one title pattern.");
        }

        for (var i = 0; i < parsing.TitlePatterns.Count; i++)
        {
            var pattern = parsing.TitlePatterns[i];
            var path = Path("parsing.titlePatterns", i);

            if (string.IsNullOrWhiteSpace(pattern.PatternId))
            {
                Report(defects, path, "A title pattern declares the identifier corpus coverage reports it under.");
            }
            else if (!patternIds.Add(pattern.PatternId))
            {
                Report(defects, path, $"Title pattern '{pattern.PatternId}' is declared more than once.");
            }

            var compiled = ProbePattern(pattern.Regex, path, defects);
            var groupNames = compiled?.GetGroupNames() ?? [];

            for (var c = 0; c < pattern.Captures.Count; c++)
            {
                var capture = pattern.Captures[c];
                var capturePath = $"{path}.captures[{c.ToString(CultureInfo.InvariantCulture)}]";

                if (compiled is not null && !groupNames.Contains(capture.GroupName, StringComparer.Ordinal))
                {
                    Report(
                        defects,
                        capturePath,
                        $"Capture group '{capture.GroupName}' does not appear in the pattern's expression.");
                }

                CheckCaptureTarget(capture, shape, schemeIds, capturePath, defects);
            }

            foreach (var guardRef in pattern.Guards)
            {
                if (!guards.Contains(guardRef.GuardId))
                {
                    Report(defects, path, $"Guard '{guardRef.GuardId}' is referenced but never declared.");
                }
            }

            if (pattern.Expansion is { } expansion)
            {
                if (expansion.MaxSpan <= 0)
                {
                    Report(defects, path, "A range expansion carries a positive span cap.");
                }

                if (compiled is not null
                    && (!groupNames.Contains(expansion.FromGroup, StringComparer.Ordinal)
                        || !groupNames.Contains(expansion.ToGroup, StringComparer.Ordinal)))
                {
                    Report(
                        defects,
                        path,
                        "A range expansion names capture groups that appear in the pattern's expression.");
                }
            }
        }

        return patternIds;
    }

    private static void CheckCaptureTarget(
        CaptureBinding capture,
        ValidatedShape shape,
        HashSet<string> schemeIds,
        string path,
        List<ShapeDefect> defects)
    {
        switch (capture.Target)
        {
            case CaptureTarget.CoordinateComponent:
                if (capture.SpaceId is null || capture.ComponentId is null)
                {
                    Report(defects, path, "A coordinate-component capture names both its space and its component.");
                    return;
                }

                if (shape.SpaceOf(capture.SpaceId) is not { } space)
                {
                    Report(defects, path, $"Coordinate space '{capture.SpaceId}' is not declared by the shape.");
                    return;
                }

                if (!space.Components.Any(component =>
                        string.Equals(component.ComponentId, capture.ComponentId, StringComparison.Ordinal)))
                {
                    Report(
                        defects,
                        path,
                        $"Component '{capture.ComponentId}' is not declared by coordinate space '{capture.SpaceId}'.");
                }

                return;

            case CaptureTarget.ExternalId:
                if (capture.Key is null)
                {
                    Report(defects, path, "An external-identifier capture names its scheme.");
                }
                else if (!schemeIds.Contains(capture.Key))
                {
                    Report(defects, path, $"External-identifier scheme '{capture.Key}' is not declared by the shape.");
                }

                return;

            case CaptureTarget.Tag:
            case CaptureTarget.ReleaseKind:
                if (capture.Key is null)
                {
                    Report(defects, path, "A tag or release-kind capture names the key it lands under.");
                }

                return;

            default:
                return;
        }
    }

    private static void CheckTokenTables(ParseDeclaration parsing, List<ShapeDefect> defects)
    {
        var tableIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < parsing.TokenTables.Count; i++)
        {
            var table = parsing.TokenTables[i];
            var path = Path("parsing.tokenTables", i);

            if (!tableIds.Add(table.TableId))
            {
                Report(defects, path, $"Token table '{table.TableId}' is declared more than once.");
            }

            for (var r = 0; r < table.Rows.Count; r++)
            {
                ProbePattern(table.Rows[r].Pattern, $"{path}.rows[{r.ToString(CultureInfo.InvariantCulture)}]", defects);
            }
        }

        for (var i = 0; i < parsing.PreRewrites.Count; i++)
        {
            ProbePattern(parsing.PreRewrites[i].Regex, Path("parsing.preRewrites", i), defects);
        }
    }

    private static void CheckRungResolution(
        RungResolutionTable table,
        HashSet<string> tierIds,
        HashSet<string> guards,
        List<ShapeDefect> defects)
    {
        if (table.Rules.Count == 0)
        {
            Report(defects, "parsing.rungResolution.rules", "A rung-resolution table carries at least one row.");
        }

        var ruleIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < table.Rules.Count; i++)
        {
            var rule = table.Rules[i];
            var path = Path("parsing.rungResolution.rules", i);

            if (!ruleIds.Add(rule.RuleId))
            {
                Report(defects, path, $"Rung rule '{rule.RuleId}' is declared more than once.");
            }

            if (!tierIds.Contains(rule.TierId))
            {
                Report(defects, path, $"Tier '{rule.TierId}' is absent from every declared ladder.");
            }

            CheckPredicate(rule.When, guards, path, defects);
        }

        if (!tierIds.Contains(table.UnknownTierId))
        {
            Report(
                defects,
                "parsing.rungResolution.unknownTierId",
                $"Tier '{table.UnknownTierId}' is absent from every declared ladder.");
        }

        for (var i = 0; i < table.ContainerFallbacks.Count; i++)
        {
            var fallback = table.ContainerFallbacks[i];

            if (!tierIds.Contains(fallback.TierId))
            {
                Report(
                    defects,
                    Path("parsing.rungResolution.containerFallbacks", i),
                    $"Tier '{fallback.TierId}' is absent from every declared ladder.");
            }
        }
    }

    private static void CheckEscapes(IReadOnlyList<string> escapeIds, List<ShapeDefect> defects)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < escapeIds.Count; i++)
        {
            var path = Path("parsing.escapeIds", i);

            if (string.IsNullOrWhiteSpace(escapeIds[i]))
            {
                Report(defects, path, "An escape identifier is not blank.");
            }
            else if (!seen.Add(escapeIds[i]))
            {
                Report(defects, path, $"Escape '{escapeIds[i]}' is declared more than once.");
            }
        }
    }

    private static void CheckMatching(
        MatchDeclaration matching,
        ValidatedShape shape,
        HashSet<string> releaseKinds,
        HashSet<string> schemeIds,
        List<ShapeDefect> defects)
    {
        if (matching.Entry.Layers.Count == 0)
        {
            Report(defects, "matching.entry.layers", "Entry resolution carries at least one key layer.");
        }

        var layerIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < matching.Entry.Layers.Count; i++)
        {
            var layer = matching.Entry.Layers[i];
            var path = Path("matching.entry.layers", i);

            if (!layerIds.Add(layer.LayerId))
            {
                Report(defects, path, $"Match layer '{layer.LayerId}' is declared more than once.");
            }

            if (string.IsNullOrWhiteSpace(layer.NormalizerId))
            {
                Report(defects, path, "A match layer names the host normalizer it keys with.");
            }

            if (layer.PreferSpaceId is { } preferred && shape.SpaceOf(preferred) is null)
            {
                Report(defects, path, $"Coordinate space '{preferred}' is not declared by the shape.");
            }
        }

        for (var i = 0; i < matching.Entry.IdentifierOrder.Count; i++)
        {
            var scheme = matching.Entry.IdentifierOrder[i];

            if (!schemeIds.Contains(scheme))
            {
                Report(
                    defects,
                    Path("matching.entry.identifierOrder", i),
                    $"External-identifier scheme '{scheme}' is not declared by the shape.");
            }
        }

        if (matching.Units.Count == 0)
        {
            Report(defects, "matching.units", "A match declaration carries at least one unit-resolution row.");
        }

        var sawDefaultRow = false;

        for (var i = 0; i < matching.Units.Count; i++)
        {
            var unit = matching.Units[i];
            var path = Path("matching.units", i);

            if (unit.ReleaseKind is null)
            {
                if (sawDefaultRow)
                {
                    Report(defects, path, "At most one unit-resolution row is the default row.");
                }

                sawDefaultRow = true;
            }
            else if (!releaseKinds.Contains(unit.ReleaseKind))
            {
                Report(
                    defects,
                    path,
                    $"Release kind '{unit.ReleaseKind}' is captured by no title pattern, so the row can never fire.");
            }

            for (var s = 0; s < unit.Spaces.Count; s++)
            {
                var attempt = unit.Spaces[s];
                var attemptPath = $"{path}.spaces[{s.ToString(CultureInfo.InvariantCulture)}]";

                if (shape.SpaceOf(attempt.SpaceId) is null)
                {
                    Report(defects, attemptPath, $"Coordinate space '{attempt.SpaceId}' is not declared by the shape.");
                }

                if (attempt.Kind == SpaceAttemptKind.TitleLookup && string.IsNullOrWhiteSpace(attempt.NormalizerId))
                {
                    Report(
                        defects,
                        attemptPath,
                        "A title-lookup attempt names the normalizer the unit titles are keyed with.");
                }
            }
        }

        // No requirement that a kind declare confidence rows. How far to trust a basis is the same
        // question for every kind, so MatchConfidencePolicy answers it and a kind that declares its own
        // table overrides it entire. Demanding a table here made every kind copy the host's answer.

        if (matching.Variant is { } variant)
        {
            if (shape.VariantLevel is null)
            {
                Report(
                    defects,
                    "matching.variant",
                    "Variant choice is declared but the shape has no variant level to choose between.");
            }

            if (variant.Features.Count == 0)
            {
                Report(defects, "matching.variant.features", "Variant choice enables at least one feature.");
            }

            if (string.IsNullOrWhiteSpace(variant.FeatureCatalogId))
            {
                Report(defects, "matching.variant", "Variant choice names the host feature catalog it draws from.");
            }
        }
    }

    private static void CheckQuerying(QueryDeclaration querying, ValidatedShape shape, List<ShapeDefect> defects)
    {
        if (querying.Tiers.Count == 0)
        {
            Report(defects, "querying.tiers", "A query declaration carries at least one tier.");
        }

        var tierIds = new HashSet<string>(StringComparer.Ordinal);
        var searchKindIds = shape.Declaration.SearchKinds
            .Select(kind => kind.SearchKindId)
            .ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < querying.Tiers.Count; i++)
        {
            var tier = querying.Tiers[i];
            var path = Path("querying.tiers", i);

            if (!tierIds.Add(tier.TierId))
            {
                Report(defects, path, $"Query tier '{tier.TierId}' is declared more than once.");
            }

            if (!searchKindIds.Contains(tier.SearchKindId))
            {
                Report(defects, path, $"Search kind '{tier.SearchKindId}' is not declared by the shape.");
            }
        }

        var aliasIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < querying.Aliases.Count; i++)
        {
            if (!aliasIds.Add(querying.Aliases[i].AliasId))
            {
                Report(
                    defects,
                    Path("querying.aliases", i),
                    $"Alias template '{querying.Aliases[i].AliasId}' is declared more than once.");
            }
        }

        var spellingIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < querying.Grammar.Spellings.Count; i++)
        {
            var spelling = querying.Grammar.Spellings[i];
            var path = Path("querying.grammar.spellings", i);

            if (!spellingIds.Add(spelling.SpellingId))
            {
                Report(defects, path, $"Coordinate spelling '{spelling.SpellingId}' is declared more than once.");
            }

            if (shape.SpaceOf(spelling.SpaceId) is null)
            {
                Report(defects, path, $"Coordinate space '{spelling.SpaceId}' is not declared by the shape.");
            }
        }

        for (var i = 0; i < querying.Limits.Count; i++)
        {
            if (querying.Limits[i].Limit <= 0)
            {
                Report(defects, Path("querying.limits", i), "A result limit is positive.");
            }
        }
    }

    private static void CheckQuality(QualityDeclaration quality, HashSet<string> guards, List<ShapeDefect> defects)
    {
        for (var i = 0; i < quality.Defaults.Count; i++)
        {
            CheckPredicate(quality.Defaults[i].When, guards, Path("quality.defaults", i), defects);
        }
    }

    private static void CheckNaming(NamingDeclaration naming, HashSet<string> guards, List<ShapeDefect> defects)
    {
        if (string.IsNullOrWhiteSpace(naming.FolderSpine))
        {
            Report(defects, "naming.folderSpine", "A naming declaration carries a folder spine.");
        }

        var ruleIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < naming.Selection.Count; i++)
        {
            var rule = naming.Selection[i];
            var path = Path("naming.selection", i);

            if (!ruleIds.Add(rule.RuleId))
            {
                Report(defects, path, $"Template-selection rule '{rule.RuleId}' is declared more than once.");
            }

            if (rule.TemplateId is { } template && !naming.DefaultTemplates.ContainsKey(template))
            {
                Report(defects, path, $"Template '{template}' is not declared among the default templates.");
            }

            if (rule.FallbackTemplateId is { } fallback && !naming.DefaultTemplates.ContainsKey(fallback))
            {
                Report(defects, path, $"Template '{fallback}' is not declared among the default templates.");
            }

            CheckPredicate(rule.When, guards, path, defects);
        }

        var styleIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < naming.MultiUnitStyles.Count; i++)
        {
            if (!styleIds.Add(naming.MultiUnitStyles[i].StyleId))
            {
                Report(
                    defects,
                    Path("naming.multiUnitStyles", i),
                    $"Multi-unit style '{naming.MultiUnitStyles[i].StyleId}' is declared more than once.");
            }
        }
    }

    private static void CheckCatalog(
        CatalogDeclaration? catalog,
        ValidatedShape shape,
        HashSet<string> schemeIds,
        List<ShapeDefect> defects)
    {
        if (catalog is null)
        {
            return;
        }

        if (catalog.Requests.Count == 0)
        {
            Report(defects, "catalog.requests", "A catalog declaration carries at least one request template.");
        }

        var requestIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < catalog.Requests.Count; i++)
        {
            if (!requestIds.Add(catalog.Requests[i].RequestId))
            {
                Report(
                    defects,
                    Path("catalog.requests", i),
                    $"Request template '{catalog.Requests[i].RequestId}' is declared more than once.");
            }
        }

        if (catalog.Responses.Count == 0)
        {
            Report(defects, "catalog.responses", "A catalog declaration carries at least one response map.");
        }

        var levelIds = shape.Declaration.Levels.Select(level => level.Id.Value).ToHashSet(StringComparer.Ordinal);
        var axisIds = shape.Declaration.GroupingAxes.Select(axis => axis.AxisId).ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < catalog.Responses.Count; i++)
        {
            var response = catalog.Responses[i];
            var path = Path("catalog.responses", i);

            if (response.LevelId is { } level && !levelIds.Contains(level))
            {
                Report(defects, path, $"Level '{level}' is not declared by the shape.");
            }

            if (response.AxisId is { } axis && !axisIds.Contains(axis))
            {
                Report(defects, path, $"Grouping axis '{axis}' is not declared by the shape.");
            }

            // A response map's scheme is deliberately NOT cross-checked against the shape. The shape no
            // longer enumerates schemes — which catalog issues which identifier is a fact about the
            // installed catalogers, not about what a movie is, and the level's identity states the roles it
            // needs instead. With nothing to check against, this check could only ever refuse every kind.
            // The check it replaces returns when a cataloger is its own plugin: at that point the scheme is
            // the cataloger's own and the pairing is checkable where both halves are in scope.
        }

        var derivationIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < catalog.Derivations.Count; i++)
        {
            if (!derivationIds.Add(catalog.Derivations[i].RuleId))
            {
                Report(
                    defects,
                    Path("catalog.derivations", i),
                    $"Derivation rule '{catalog.Derivations[i].RuleId}' is declared more than once.");
            }
        }

        if (catalog.Paging.MaxPages <= 0)
        {
            Report(defects, "catalog.paging", "A paging policy allows at least one page.");
        }
    }

    private static void CheckNotifications(
        NotificationDeclaration notifications,
        ValidatedShape shape,
        List<ShapeDefect> defects)
    {
        var axisIds = shape.Declaration.GroupingAxes.Select(axis => axis.AxisId).ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < notifications.GroupSummaries.Count; i++)
        {
            if (!axisIds.Contains(notifications.GroupSummaries[i].AxisId))
            {
                Report(
                    defects,
                    Path("notifications.groupSummaries", i),
                    $"Grouping axis '{notifications.GroupSummaries[i].AxisId}' is not declared by the shape.");
            }
        }
    }

    private static void CheckCorpus(
        IReadOnlyList<CorpusCase> corpus,
        HashSet<string> patternIds,
        HashSet<string> tierIds,
        List<ShapeDefect> defects)
    {
        var caseIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < corpus.Count; i++)
        {
            var corpusCase = corpus[i];
            var path = Path("corpus", i);

            if (!caseIds.Add(corpusCase.CaseId))
            {
                Report(defects, path, $"Corpus case '{corpusCase.CaseId}' is declared more than once.");
            }

            if (corpusCase.ExpectedPatternId is { } pattern && !patternIds.Contains(pattern))
            {
                Report(defects, path, $"Title pattern '{pattern}' is not declared by the parsing section.");
            }

            if (corpusCase.ExpectedTierId is { } tier && !tierIds.Contains(tier))
            {
                Report(defects, path, $"Tier '{tier}' is absent from every declared ladder.");
            }
        }
    }

    private static void CheckPredicate(
        TagPredicate predicate,
        HashSet<string> guards,
        string path,
        List<ShapeDefect> defects)
    {
        for (var i = 0; i < predicate.All.Count; i++)
        {
            var atom = predicate.All[i];
            var atomPath = $"{path}.when[{i.ToString(CultureInfo.InvariantCulture)}]";

            if (string.IsNullOrWhiteSpace(atom.Subject))
            {
                Report(defects, atomPath, "A predicate atom names its subject.");
                continue;
            }

            if (atom.Subject.StartsWith("guard:", StringComparison.Ordinal)
                && !guards.Contains(atom.Subject["guard:".Length..]))
            {
                Report(
                    defects,
                    atomPath,
                    $"Guard '{atom.Subject["guard:".Length..]}' is referenced but never declared.");
            }

            switch (atom.Op)
            {
                case PredicateOp.Equals:
                case PredicateOp.In:
                case PredicateOp.Contains:
                case PredicateOp.GreaterOrEqual:
                case PredicateOp.LessOrEqual:
                    if (atom.Values.Count == 0)
                    {
                        Report(defects, atomPath, $"A '{atom.Op}' atom carries at least one value.");
                    }

                    break;

                case PredicateOp.GuardMatches:
                    var guardId = atom.Subject.StartsWith("guard:", StringComparison.Ordinal)
                        ? atom.Subject["guard:".Length..]
                        : atom.Values.Count > 0 ? atom.Values[0] : null;

                    if (guardId is null || !guards.Contains(guardId))
                    {
                        Report(
                            defects,
                            atomPath,
                            $"Guard '{guardId ?? atom.Subject}' is referenced but never declared.");
                    }

                    break;

                case PredicateOp.Present:
                default:
                    break;
            }
        }
    }

    private static Regex? ProbePattern(string pattern, string path, List<ShapeDefect> defects)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            Report(defects, path, "A regular expression is not empty.");
            return null;
        }

        try
        {
            // Construction validates the expression; the timeout bounds any engine work a probe could do.
            return new Regex(pattern, RegexOptions.None, PatternProbeTimeout);
        }
        catch (ArgumentException failure)
        {
            Report(defects, path, $"The regular expression does not parse: {failure.Message}");
            return null;
        }
    }

    private static string Path(string prefix, int index)
        => string.Create(CultureInfo.InvariantCulture, $"{prefix}[{index}]");

    private static void Report(List<ShapeDefect> defects, string path, string message)
        => defects.Add(new ShapeDefect(path, message, CoreErrorCode.PluginShapeInvalid));
}
