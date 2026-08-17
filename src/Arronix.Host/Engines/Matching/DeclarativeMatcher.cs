using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Shape;

// The declaration and shape contracts the engine executes are experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

namespace Arronix.Host.Engines.Matching;

/// <summary>
/// The declarative match engine: one instance per validated definition, implementing the existing
/// matcher seam so downstream consumers cannot tell a declared kind from a hand-written one.
/// </summary>
/// <remarks>
/// <para>
/// The engine owns the cascade ordering the audit verified against Radarr
/// (<c>_reference/Radarr/src/NzbDrone.Core/Movies/MovieService.cs:126-158</c> and its
/// <c>ParsingService</c> call order): external identifiers in declared precedence first, the scoped
/// entry when the caller supplied one, then the entry-resolution strategy over the declared key layers.
/// The declaration parameterizes each step; the order itself is engine behavior and no declaration may
/// reorder it.
/// </para>
/// <para>
/// Every cross-reference the declaration makes — normalizer, expander, strategy identifiers — is
/// resolved at construction, and an unknown identifier refuses the engine with the row named. After
/// construction there are no lookups left to fail.
/// </para>
/// </remarks>
internal sealed class DeclarativeMatcher : IReleaseMatcher
{
    /// <summary>The parsed-release metadata key carrying the release-kind discriminator.</summary>
    internal const string ReleaseKindMetadataKey = "release-kind";

    /// <summary>The parsed-release metadata key carrying leftover unit-title text.</summary>
    internal const string UnitTitleMetadataKey = "unit-title";

    private readonly MatchDeclaration _declaration;
    private readonly IReadOnlyList<ConfidenceRule> _confidence;
    private readonly IMatchEntryReader _reader;
    private readonly IEntryResolutionStrategy _entryResolution;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeclarativeMatcher"/> class.
    /// </summary>
    /// <param name="mediaKind">The kind the engine serves.</param>
    /// <param name="declaration">The kind's match declaration.</param>
    /// <param name="registry">The host's strategy registry.</param>
    /// <param name="reader">The library read window.</param>
    /// <exception cref="InvalidOperationException">A declared identifier resolves to nothing.</exception>
    /// <remarks>
    /// <para>
    /// Both strategies are <i>derived</i> rather than bound by name. Entry resolution has one implementation
    /// and choosing it by string was a choice with one option. Unit assignment is needed exactly when a
    /// release can cover more than one unit, and the match declaration already says whether it can: a unit
    /// rule that expands a span is the condition, and a kind whose every rule reads
    /// <see cref="SpanExpansion.None"/> has no assignment problem to pose.
    /// </para>
    /// <para>
    /// What this replaces was a per-kind string naming a host-owned strategy, checked against a host
    /// vocabulary at load. The fact was always derivable from the declaration next to it.
    /// </para>
    /// </remarks>
    internal DeclarativeMatcher(
        MediaKindId mediaKind,
        MatchDeclaration declaration,
        MatchStrategyRegistry registry,
        IMatchEntryReader reader)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(registry);

        MediaKind = mediaKind;
        _declaration = declaration;
        _confidence = MatchConfidencePolicy.For(declaration.Confidence);
        _reader = reader;

        ValidateLayers(declaration.Entry.Layers);
        ValidateUnits(declaration.Units);

        _entryResolution = registry.Resolve<IEntryResolutionStrategy>(
            MatchStrategyRoles.EntryResolution,
            "layered-key-lookup");

        UnitAssignment = declaration.Units.Any(static rule => rule.Expansion != SpanExpansion.None)
            ? registry.Resolve<IUnitAssignmentStrategy>(
                MatchStrategyRoles.UnitAssignment,
                "assignment-over-features")
            : null;
    }

    /// <inheritdoc />
    public MediaKindId MediaKind { get; }

    /// <summary>
    /// Gets the unit-assignment strategy, when the kind's units can span. Consumed by the import
    /// workbench's file-to-unit step; the single-text matcher seam has no assignment problem to pose.
    /// </summary>
    internal IUnitAssignmentStrategy? UnitAssignment { get; }

    /// <inheritdoc />
    public async Task<MatchOutcome> MatchAsync(MatchRequest request, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var input = BuildInput(request);

        var (entry, basis, preferSpaceId, rejection) =
            await ResolveEntryAsync(request, input, warnings, cancellationToken).ConfigureAwait(false);

        if (entry is null)
        {
            return Rejection(rejection ?? "Nothing in the library answers the text.", warnings);
        }

        var unitOutcome = await ResolveUnitsAsync(request, entry, preferSpaceId, cancellationToken)
            .ConfigureAwait(false);

        if (unitOutcome.Units.Count == 0)
        {
            return Rejection(
                unitOutcome.RejectionReason ?? $"No unit of {entry.Ref} answers the reading.",
                warnings);
        }

        // Coordinates addressing units within an already-resolved entry are the informative basis when
        // the entry itself was only scope.
        if (basis == MatchBasis.Scope && unitOutcome.UsedCoordinates)
        {
            basis = MatchBasis.Coordinate;
        }

        return new MatchOutcome
        {
            Units = unitOutcome.Units,
            Confidence = DecideConfidence(basis, unitOutcome.Confidence, request.Source, warnings),
            Basis = basis,
            Coordinates = unitOutcome.Coordinates,
            Warnings = warnings,
        };
    }

    private static void ValidateLayers(IReadOnlyList<MatchLayer> layers)
    {
        foreach (var layer in layers)
        {
            if (!MatchKeyNormalizers.Exists(layer.NormalizerId))
            {
                throw new InvalidOperationException(
                    $"Layer '{layer.LayerId}' names unknown normalizer '{layer.NormalizerId}'.");
            }

            foreach (var expanderId in layer.ExpanderIds)
            {
                if (!MatchKeyExpanders.Exists(expanderId))
                {
                    throw new InvalidOperationException(
                        $"Layer '{layer.LayerId}' names unknown expander '{expanderId}'.");
                }
            }
        }
    }

    private static void ValidateUnits(IReadOnlyList<UnitResolutionRule> units)
    {
        if (units.Count(rule => rule.ReleaseKind is null) > 1)
        {
            throw new InvalidOperationException("More than one unit-resolution row is the default row.");
        }

        foreach (var rule in units)
        {
            foreach (var attempt in rule.Spaces)
            {
                if (attempt.Kind != SpaceAttemptKind.TitleLookup)
                {
                    continue;
                }

                if (attempt.NormalizerId is null)
                {
                    throw new InvalidOperationException(
                        $"The title-lookup attempt for space '{attempt.SpaceId}' declares no normalizer.");
                }

                if (!MatchKeyNormalizers.Exists(attempt.NormalizerId))
                {
                    throw new InvalidOperationException(
                        $"The title-lookup attempt for space '{attempt.SpaceId}' names unknown normalizer "
                        + $"'{attempt.NormalizerId}'.");
                }
            }
        }
    }

    private static EntryResolutionInput BuildInput(MatchRequest request)
    {
        var title = request.Parsed?.Title;
        string[] titles = string.IsNullOrWhiteSpace(title) ? [request.Text] : [title];

        long? titleYear = null;
        if (request.Parsed?.Year is { } yearText
            && long.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
        {
            titleYear = year;
        }

        return new EntryResolutionInput
        {
            Titles = titles,
            ReadingValues = new Dictionary<string, long?>(StringComparer.Ordinal)
            {
                ["reading.TitleYear"] = titleYear,
            },
            Candidates = [],
        };
    }

    private async Task<(ItemView? Entry, MatchBasis Basis, string? PreferSpaceId, string? Rejection)>
        ResolveEntryAsync(
            MatchRequest request,
            EntryResolutionInput input,
            List<string> warnings,
            CancellationToken cancellationToken)
    {
        // 1. Identifier short-circuit, in declared scheme precedence.
        foreach (var externalId in OrderByScheme(request.ExternalIds))
        {
            var resolved = await _reader.ResolveExternalAsync(MediaKind, externalId, cancellationToken)
                .ConfigureAwait(false);
            if (resolved is not { } reference)
            {
                continue;
            }

            if (request.Scope is { } scoped && _declaration.Entry.ScopeReplacesSearch && reference != scoped)
            {
                return (null, MatchBasis.None, null,
                    $"Identifier {externalId} resolves to {reference}, not the scoped {scoped}.");
            }

            var entry = await _reader.GetAsync(reference, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                continue;
            }

            // The wrong-identifier defense: the agreement rules guard every identifier resolution, and a
            // disagreeing entry is skipped rather than trusted.
            if (!AgreementsHold(input, entry))
            {
                warnings.Add($"Identifier {externalId} resolved to {reference}, but the reading disagrees with it.");
                continue;
            }

            return (entry, MatchBasis.Identifier, null, null);
        }

        // 2. A scope replaces the catalog-wide search: text that disagrees is a rejection, never a match
        //    against something else.
        if (request.Scope is { } scope && _declaration.Entry.ScopeReplacesSearch)
        {
            var scopedEntry = await _reader.GetAsync(scope, cancellationToken).ConfigureAwait(false);
            if (scopedEntry is null)
            {
                return (null, MatchBasis.None, null, $"The scoped entry {scope} is unknown.");
            }

            var scopedOutcome = _entryResolution.Resolve(
                _declaration.Entry,
                input with { Candidates = [scopedEntry] });

            return scopedOutcome.Entry is null
                ? (null, MatchBasis.None, null,
                    $"The text disagrees with the scoped entry {scope}: "
                    + (scopedOutcome.RejectionReason ?? "no key layer accepted it."))
                : (scopedEntry, MatchBasis.Scope, scopedOutcome.PreferSpaceId, null);
        }

        // 3. The entry-resolution strategy over the whole library.
        var candidates = await _reader.GetEntriesAsync(MediaKind, cancellationToken).ConfigureAwait(false);
        var outcome = _entryResolution.Resolve(_declaration.Entry, input with { Candidates = candidates });

        return outcome.Entry is null
            ? (null, MatchBasis.None, null, outcome.RejectionReason)
            : (outcome.Entry, outcome.Basis, outcome.PreferSpaceId, null);
    }

    private IEnumerable<ExternalId> OrderByScheme(IReadOnlyList<ExternalId> externalIds)
    {
        var order = _declaration.Entry.IdentifierOrder;
        return externalIds
            .Select((id, index) => (Id: id, Index: index))
            .OrderBy(pair =>
            {
                var precedence = -1;
                for (var i = 0; i < order.Count; i++)
                {
                    if (string.Equals(order[i], pair.Id.Scheme, StringComparison.Ordinal))
                    {
                        precedence = i;
                        break;
                    }
                }

                return precedence == -1 ? order.Count : precedence;
            })
            .ThenBy(pair => pair.Index)
            .Select(pair => pair.Id);
    }

    private bool AgreementsHold(EntryResolutionInput input, ItemView entry)
    {
        var outcome = _entryResolution.Resolve(
            _declaration.Entry with
            {
                // Key layers are not consulted for an identifier resolution; only the agreements guard it.
                Layers =
                [
                    new MatchLayer
                    {
                        LayerId = "identifier-guard",
                        KeyTemplate = "{title}",
                        NormalizerId = MatchKeyNormalizers.StripNonAlphanumericUpper,
                    },
                ],
                Ambiguity = AmbiguityPolicy.Reject,
            },
            input with { Titles = [entry.Title], Candidates = [entry] });

        return outcome.Entry is not null;
    }

    private async Task<UnitResolution> ResolveUnitsAsync(
        MatchRequest request,
        ItemView entry,
        string? preferSpaceId,
        CancellationToken cancellationToken)
    {
        var rule = SelectUnitRule(request);
        if (rule is null)
        {
            return UnitResolution.Rejected("No unit-resolution row covers the reading's release kind.");
        }

        var units = await _reader.GetUnitsAsync(entry.Ref, cancellationToken).ConfigureAwait(false);
        if (units.Count == 0)
        {
            return UnitResolution.Rejected($"{entry.Ref} has no units.");
        }

        foreach (var attempt in OrderAttempts(rule.Spaces, preferSpaceId))
        {
            var resolution = attempt.Kind switch
            {
                SpaceAttemptKind.TitleLookup => TryTitleLookup(request, attempt, units),
                _ => TryCoordinates(request, rule, attempt, units),
            };

            if (resolution is not null)
            {
                return await ExpandAsync(rule, resolution, cancellationToken).ConfigureAwait(false);
            }
        }

        return UnitResolution.Rejected("No declared resolution attempt addressed a unit.");
    }

    private UnitResolutionRule? SelectUnitRule(MatchRequest request)
    {
        string? releaseKind = null;
        if (request.Parsed?.AdditionalMetadata is { } metadata
            && metadata.TryGetValue(ReleaseKindMetadataKey, out var value))
        {
            releaseKind = value;
        }

        return _declaration.Units.FirstOrDefault(rule =>
                releaseKind is not null && string.Equals(rule.ReleaseKind, releaseKind, StringComparison.Ordinal))
            ?? _declaration.Units.FirstOrDefault(rule => rule.ReleaseKind is null);
    }

    private static IEnumerable<SpaceAttempt> OrderAttempts(
        IReadOnlyList<SpaceAttempt> attempts,
        string? preferSpaceId)
    {
        if (preferSpaceId is null)
        {
            return attempts;
        }

        // The on-match space hint steers order without changing membership: an entry matched through a
        // community alias tries that community's numbering first.
        var preferred = attempts.Where(attempt =>
            string.Equals(attempt.SpaceId, preferSpaceId, StringComparison.Ordinal));
        var rest = attempts.Where(attempt =>
            !string.Equals(attempt.SpaceId, preferSpaceId, StringComparison.Ordinal));
        return preferred.Concat(rest);
    }

    private static UnitResolution? TryCoordinates(
        MatchRequest request,
        UnitResolutionRule rule,
        SpaceAttempt attempt,
        IReadOnlyList<ItemView> units)
    {
        if (request.Coordinates.TryGet(attempt.SpaceId, out var reading))
        {
            var exact = units
                .Where(unit => unit.Coordinates.TryGet(attempt.SpaceId, out var position)
                    && position.Value == reading.Value)
                .ToArray();

            if (exact.Length > 0)
            {
                return new UnitResolution
                {
                    Units = exact.Select(unit => unit.Ref).ToArray(),
                    Coordinates = CoordinateSet.Of(reading),
                    Confidence = reading.Confidence,
                    UsedCoordinates = true,
                };
            }

            // A span-scoped reading expands to every member along its sequence: units whose ordinal path
            // extends the reading's path.
            if (rule.Expansion == SpanExpansion.SequenceMembers && reading.Value.Kind == CoordinateKind.Ordinal)
            {
                var members = units
                    .Where(unit => unit.Coordinates.TryGet(attempt.SpaceId, out var position)
                        && position.Value.Kind == CoordinateKind.Ordinal
                        && IsPrefixOf(reading.Value.Ordinals, position.Value.Ordinals))
                    .ToArray();

                if (members.Length > 0)
                {
                    return new UnitResolution
                    {
                        Units = members.Select(unit => unit.Ref).ToArray(),
                        Coordinates = CoordinateSet.Of(reading),
                        Confidence = reading.Confidence,
                        UsedCoordinates = true,
                    };
                }
            }

            return null;
        }

        // No reading in the space: a unit that addresses itself — a singleton position — needs no value.
        var singletons = units
            .Where(unit => unit.Coordinates.TryGet(attempt.SpaceId, out var position)
                && position.Value.Kind == CoordinateKind.Singleton)
            .ToArray();

        return singletons.Length == 1
            ? new UnitResolution
            {
                Units = [singletons[0].Ref],
                Coordinates = CoordinateSet.Empty,
                Confidence = null,
                UsedCoordinates = false,
            }
            : null;
    }

    private static UnitResolution? TryTitleLookup(
        MatchRequest request,
        SpaceAttempt attempt,
        IReadOnlyList<ItemView> units)
    {
        if (request.Parsed?.AdditionalMetadata is not { } metadata
            || !metadata.TryGetValue(UnitTitleMetadataKey, out var unitTitle)
            || string.IsNullOrWhiteSpace(unitTitle))
        {
            return null;
        }

        var key = MatchKeyNormalizers.Normalize(attempt.NormalizerId!, unitTitle);
        var matched = units
            .Where(unit => string.Equals(
                MatchKeyNormalizers.Normalize(attempt.NormalizerId!, unit.Title),
                key,
                StringComparison.Ordinal))
            .ToArray();

        if (matched.Length != 1)
        {
            return null;
        }

        // The successful lookup is stamped with the attempt's space, carrying the unit's own position
        // there when the catalog states one.
        var coordinates = matched[0].Coordinates.TryGet(attempt.SpaceId, out var position)
            ? CoordinateSet.Of(position)
            : CoordinateSet.Empty;

        return new UnitResolution
        {
            Units = [matched[0].Ref],
            Coordinates = coordinates,
            Confidence = null,
            UsedCoordinates = false,
        };
    }

    private async Task<UnitResolution> ExpandAsync(
        UnitResolutionRule rule,
        UnitResolution resolution,
        CancellationToken cancellationToken)
    {
        if (rule.Expansion != SpanExpansion.BindingUnits)
        {
            return resolution;
        }

        // The match resolves at the binding anchor; the units are the anchor's children in running order.
        var expanded = new List<MediaItemRef>();
        foreach (var anchor in resolution.Units)
        {
            var children = await _reader.GetUnitsAsync(anchor, cancellationToken).ConfigureAwait(false);
            expanded.AddRange(children.Select(child => child.Ref));
        }

        return expanded.Count == 0 ? resolution : resolution with { Units = expanded };
    }

    private MatchConfidence DecideConfidence(
        MatchBasis basis,
        CoordinateConfidence? coordinateConfidence,
        MatchSource source,
        List<string> warnings)
    {
        foreach (var rule in _confidence)
        {
            if (rule.Basis != basis)
            {
                continue;
            }

            if (rule.CoordinateConfidence is { } required && required != coordinateConfidence)
            {
                continue;
            }

            if (rule.SourceIn is { } sources && !sources.Contains(source))
            {
                continue;
            }

            return rule.Result;
        }

        warnings.Add($"No confidence row covers basis {basis} from source {source}; reporting Low.");
        return MatchConfidence.Low;
    }

    private static bool IsPrefixOf(OrdinalPath prefix, OrdinalPath path)
    {
        if (prefix.Length == 0 || prefix.Length >= path.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (prefix[i] != path[i])
            {
                return false;
            }
        }

        return true;
    }

    private static MatchOutcome Rejection(string reason, List<string> warnings) => new()
    {
        Units = [],
        Confidence = MatchConfidence.None,
        Basis = MatchBasis.None,
        RejectionReason = reason,
        Warnings = warnings,
    };

    private sealed record UnitResolution
    {
        public IReadOnlyList<MediaItemRef> Units { get; init; } = [];

        public CoordinateSet Coordinates { get; init; } = CoordinateSet.Empty;

        public CoordinateConfidence? Confidence { get; init; }

        public bool UsedCoordinates { get; init; }

        public string? RejectionReason { get; init; }

        public static UnitResolution Rejected(string reason) => new() { RejectionReason = reason };
    }
}
