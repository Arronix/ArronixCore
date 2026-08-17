using System.Globalization;
using System.Linq;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

// The declaration and shape contracts this strategy executes are experimental.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

namespace Arronix.Host.Engines.Matching;

/// <summary>
/// Entry resolution as ordered key layers over declared derivations: the <c>layered-key-lookup</c>
/// member of the match strategy family.
/// </summary>
/// <remarks>
/// <para>
/// Ports the fallback layering of Radarr's <c>MovieService.FindByTitle</c>
/// (<c>_reference/Radarr/src/NzbDrone.Core/Movies/MovieService.cs:126-158</c>): the entry's own clean
/// titles first, then numeral rewrites of the reading key, then alternative titles, then translations —
/// each layer filtered by year agreement, with the next layer consulted only when the previous produced
/// nothing. The layering is what stops an alternative spelling of one entry from outranking the actual
/// title of another, and it is why no engine may reorder the declared rows.
/// </para>
/// <para>
/// Expanders apply to the reading-side key only, as in the source: Radarr rewrites the search title into
/// its arabic and roman spellings and looks all of them up, and never rewrites the stored titles.
/// </para>
/// </remarks>
internal sealed class LayeredKeyLookupStrategy : IEntryResolutionStrategy
{
    /// <inheritdoc />
    public string Role => MatchStrategyRoles.EntryResolution;

    /// <inheritdoc />
    public string StrategyId => "layered-key-lookup";

    /// <inheritdoc />
    public EntryResolutionOutcome Resolve(EntryResolution declaration, EntryResolutionInput input)
    {
        foreach (var layer in declaration.Layers)
        {
            var readingKeys = ReadingKeys(layer, input);
            var survivors = input.Candidates
                .Where(candidate => EntryKeys(layer, candidate).Overlaps(readingKeys))
                .ToList();

            if (survivors.Count == 0)
            {
                continue;
            }

            var (agreeing, corroborated) = ApplyAgreements(declaration.Agreements, input, survivors);
            if (agreeing.Count == 0)
            {
                continue;
            }

            if (agreeing.Count > 1 && declaration.Ambiguity == AmbiguityPolicy.TiebreakByYear)
            {
                agreeing = TiebreakByYear(declaration.Agreements, input, agreeing);
            }

            if (agreeing.Count > 1)
            {
                var contenders = agreeing.Select(entry => entry.Ref.ToString()).ToArray();
                return new EntryResolutionOutcome
                {
                    RejectionReason =
                        $"Layer '{layer.LayerId}' left {agreeing.Count} contenders: {string.Join(", ", contenders)}.",
                    Contenders = contenders,
                };
            }

            return new EntryResolutionOutcome
            {
                Entry = agreeing[0],
                Basis = corroborated ? MatchBasis.TitleWithYear : MatchBasis.TitleOnly,
                LayerId = layer.LayerId,
                PreferSpaceId = layer.PreferSpaceId,
            };
        }

        return new EntryResolutionOutcome { RejectionReason = "No key layer produced a candidate." };
    }

    private static HashSet<string> ReadingKeys(MatchLayer layer, EntryResolutionInput input)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var title in input.Titles)
        {
            var key = MatchKeyNormalizers.Normalize(layer.NormalizerId, title);
            if (key.Length == 0)
            {
                continue;
            }

            keys.Add(key);

            foreach (var expanderId in layer.ExpanderIds)
            {
                foreach (var variant in MatchKeyExpanders.Expand(expanderId, key))
                {
                    keys.Add(variant);
                }
            }
        }

        return keys;
    }

    private static HashSet<string> EntryKeys(MatchLayer layer, ItemView candidate)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in layer.KeyTemplate.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var text in ResolveTemplatePart(part.Trim(), candidate))
            {
                var key = MatchKeyNormalizers.Normalize(layer.NormalizerId, text);
                if (key.Length > 0)
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private static IEnumerable<string> ResolveTemplatePart(string part, ItemView candidate)
    {
        if (part.Length < 3 || part[0] != '{' || part[^1] != '}')
        {
            throw new InvalidOperationException(
                $"Key template part '{part}' is not a single '{{field}}' token. "
                + "A key layer derives its key from one field reference per '|'-separated alternative.");
        }

        var fieldId = part[1..^1];
        if (string.Equals(fieldId, "title", StringComparison.Ordinal))
        {
            return [candidate.Title];
        }

        if (!candidate.Fields.TryGetValue(fieldId, out var value) || value.IsAbsent)
        {
            return [];
        }

        return FieldTexts(value);
    }

    private static IEnumerable<string> FieldTexts(FieldValue value)
    {
        if (value.Items is { } items)
        {
            return items.SelectMany(FieldTexts);
        }

        return value.Text is { Length: > 0 } text ? [text] : [];
    }

    private static (List<ItemView> Agreeing, bool Corroborated) ApplyAgreements(
        IReadOnlyList<AgreementRule> agreements,
        EntryResolutionInput input,
        List<ItemView> survivors)
    {
        var agreeing = survivors;
        var corroborated = false;

        foreach (var rule in agreements)
        {
            var subject = SubjectValue(rule, input);
            if (subject is null)
            {
                if (!rule.AbsentAgrees)
                {
                    return ([], false);
                }

                continue;
            }

            agreeing = agreeing.Where(candidate => Agrees(rule, subject.Value, candidate)).ToList();
            if (agreeing.Count > 0)
            {
                corroborated = true;
            }
        }

        return (agreeing, corroborated);
    }

    private static List<ItemView> TiebreakByYear(
        IReadOnlyList<AgreementRule> agreements,
        EntryResolutionInput input,
        List<ItemView> contenders)
    {
        // The tiebreak prefers entries agreeing with the subject on the FIRST declared entry-side path —
        // the primary statement — over ones that only agree through a secondary path.
        foreach (var rule in agreements)
        {
            var subject = SubjectValue(rule, input);
            if (subject is null || rule.AgreesWith.Count == 0)
            {
                continue;
            }

            var primary = contenders
                .Where(candidate => EntryNumber(candidate, rule.AgreesWith[0]) == subject.Value)
                .ToList();

            if (primary.Count > 0 && primary.Count < contenders.Count)
            {
                contenders = primary;
            }
        }

        return contenders;
    }

    private static long? SubjectValue(AgreementRule rule, EntryResolutionInput input)
    {
        if (!input.ReadingValues.TryGetValue(rule.Subject, out var stated) || stated is null)
        {
            return null;
        }

        // Below the declared minimum, the statement is noise rather than evidence and is treated as absent.
        if (rule.MinimumValue is { } minimum && stated < minimum)
        {
            return null;
        }

        return stated;
    }

    private static bool Agrees(AgreementRule rule, long subject, ItemView candidate) =>
        rule.AgreesWith.Any(path => EntryNumber(candidate, path) == subject);

    private static long? EntryNumber(ItemView candidate, string path)
    {
        const string Prefix = "entry.";
        var fieldId = path.StartsWith(Prefix, StringComparison.Ordinal) ? path[Prefix.Length..] : path;

        if (!candidate.Fields.TryGetValue(fieldId, out var value) || value.IsAbsent)
        {
            return null;
        }

        if (value.Number is { } number)
        {
            return number;
        }

        if (value.Date is { } date)
        {
            return date.Year;
        }

        return value.Text is { } text
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}

/// <summary>
/// The strategy surface the <see cref="MatchStrategyRoles.EntryResolution"/> role requires.
/// </summary>
internal interface IEntryResolutionStrategy : IMatchStrategy
{
    /// <summary>
    /// Resolves a reading to at most one catalog entry.
    /// </summary>
    /// <param name="declaration">The kind's declared cascade parameters.</param>
    /// <param name="input">The reading-side evidence and the candidate entries.</param>
    /// <returns>The outcome.</returns>
    EntryResolutionOutcome Resolve(EntryResolution declaration, EntryResolutionInput input);
}

/// <summary>
/// The reading-side evidence entry resolution runs on, with the candidates it may choose from.
/// </summary>
internal sealed record EntryResolutionInput
{
    /// <summary>
    /// Gets the title spellings the reading asserted, the title guess first.
    /// </summary>
    public required IReadOnlyList<string> Titles { get; init; }

    /// <summary>
    /// Gets the reading-side values agreement rules subject-path into, such as
    /// <c>"reading.TitleYear"</c>. A missing key and a null value both mean the reading stated nothing.
    /// </summary>
    public IReadOnlyDictionary<string, long?> ReadingValues { get; init; }
        = new Dictionary<string, long?>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the candidate entries.
    /// </summary>
    public required IReadOnlyList<ItemView> Candidates { get; init; }
}

/// <summary>
/// What entry resolution decided.
/// </summary>
internal sealed record EntryResolutionOutcome
{
    /// <summary>
    /// Gets the resolved entry, or <see langword="null"/> when nothing resolved.
    /// </summary>
    public ItemView? Entry { get; init; }

    /// <summary>
    /// Gets what the resolution was made on: <see cref="MatchBasis.TitleWithYear"/> when an agreement
    /// rule corroborated it, <see cref="MatchBasis.TitleOnly"/> otherwise, <see cref="MatchBasis.None"/>
    /// when nothing resolved.
    /// </summary>
    public MatchBasis Basis { get; init; } = MatchBasis.None;

    /// <summary>
    /// Gets the layer that produced the match, for diagnostics and corpus coverage.
    /// </summary>
    public string? LayerId { get; init; }

    /// <summary>
    /// Gets the coordinate space the matched layer prefers for unit resolution, when it declares one.
    /// </summary>
    public string? PreferSpaceId { get; init; }

    /// <summary>
    /// Gets why nothing resolved, when nothing did.
    /// </summary>
    public string? RejectionReason { get; init; }

    /// <summary>
    /// Gets the contenders an ambiguous resolution named.
    /// </summary>
    public IReadOnlyList<string> Contenders { get; init; } = [];
}
