// Consumes the experimental definition (ARX0019), shape (ARX0013) and quality (ARX0021) contracts.
#pragma warning disable ARX0019
#pragma warning disable ARX0013
#pragma warning disable ARX0021
#pragma warning disable ARX0020 // The typed media surface is experimental; this file consumes it.

using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;
using Arronix.Host.Engines.Parsing.Evidence;

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// The host's parse engine: executes one kind's declared release models behind the stable
/// <see cref="IReleaseParser"/> seam, so downstream consumers cannot tell a declared kind from a
/// hand-written one and both registration paths flow through one pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The shape of the algorithm is the surveyed one: the host's kind-agnostic layer runs first — separator
/// normalization, the shared source/resolution/codec/revision/remux scans, group and language reading —
/// then the kind's ordered pattern list, where the first pattern whose expression matches and whose
/// guards pass claims the release, then the kind's declared rung-resolution table over the combined tag
/// evidence. Declared order is semantic throughout and is executed byte-for-byte as declared.
/// </para>
/// <para>
/// One instance serves one definition; everything declared is compiled at construction and construction
/// fails loudly on any dangling cross-reference. Host patterns are source-generated; declared patterns
/// compile once here, every expression under a match timeout — a pattern that cannot finish over one
/// hostile title declines that title, it does not take the process down.
/// </para>
/// </remarks>
internal sealed class DeclarativeReleaseParser : IReleaseParser
{
    private readonly CompiledParseDeclaration _declaration;
    private readonly RungResolver? _rungResolver;
    private readonly IQualityType? _quality;

    /// <summary>Initializes a new instance of the <see cref="DeclarativeReleaseParser"/> class.</summary>
    /// <param name="shape">The kind's derived structure; the ladders the rung rows resolve against live on it.</param>
    /// <param name="model">The kind's release models. Refused, with a message naming the offending
    /// row, when any declared cross-reference dangles.</param>
    internal DeclarativeReleaseParser(MediaShape shape, MediaKindModel model)
    {
        ArgumentNullException.ThrowIfNull(shape);
        MediaKind = shape.Kind;
        _declaration = new CompiledParseDeclaration(shape, model);
        _rungResolver = _declaration.RungResolution is null ? null : new RungResolver(_declaration);

        // A family that reads its files onto typed axes needs no rung table and gets no rung; what the
        // parse publishes for it is the family's own rendering of the point it read, which is the same
        // community word a rung name carried and is derived from the evidence rather than chosen from a
        // fixed list.
        _quality = shape.FormatFamilies.Count > 0 ? shape.FormatFamilies[0].Quality : null;
    }

    /// <inheritdoc />
    public MediaKindId MediaKind { get; }

    /// <inheritdoc />
    public ParsedRelease? Parse(string releaseTitle)
    {
        if (string.IsNullOrWhiteSpace(releaseTitle))
        {
            return null;
        }

        var raw = releaseTitle.Trim();
        var working = ApplyPreRewrites(raw.Replace('_', ' '));

        var scanned = ScanSafely(raw, working);
        var extraTags = ScanTokenTables(working);

        var claim = Claim(raw, working);

        if (claim is not { } claimed)
        {
            return null;
        }

        var (pattern, match, captures) = claimed;

        // Tag captures join the token-table tags so rung rows reach both through the same subjects.
        foreach (var binding in pattern.Declaration.Captures)
        {
            if (binding.Target == CaptureTarget.Tag
                && captures.TryGetValue(binding.GroupName, out var tagValue))
            {
                extraTags.TryAdd(binding.Key!, tagValue);
            }
        }

        // Group and language read the text with the work's own title masked out: a title is allowed to
        // contain the name of a language or end in something that reads like a group.
        var masked = MaskTitle(working, match, pattern);
        var tags = scanned with
        {
            ReleaseGroup = ReleaseGroupScanner.Scan(masked),
            Languages = ReleaseLanguageScanner.Scan(masked),
            Extra = extraTags,
        };

        var context = new ParsePredicateContext(raw, working, tags, captures, [], _declaration.Guards);
        var rung = _rungResolver?.Resolve(context, raw);

        return Project(pattern, captures, tags, rung, ReadQuality(raw, working, extraTags), context);
    }

    /// <inheritdoc />
    public bool CanParse(string releaseTitle) => Parse(releaseTitle) is not null;

    /// <summary>Reads one release onto its family's axes and renders the point.</summary>
    /// <param name="raw">The release title as it arrived.</param>
    /// <param name="working">The separator-normalized form the guards read.</param>
    /// <param name="tags">The per-kind tags this kind's own patterns captured.</param>
    /// <returns>The rendered label, or <see langword="null"/> when the family declares no axis model.</returns>
    /// <remarks>
    /// The whole of the boundary, in one place: the host's scanners produce evidence, this kind's guards
    /// and tags ride along as the per-kind residue, and the family turns both into typed readings. Nothing
    /// between the two collapses evidence into a name, and the name that comes out the far end is a
    /// rendering of what was read rather than a row somebody chose.
    /// </remarks>
    private string? ReadQuality(string raw, string working, IReadOnlyDictionary<string, string> tags)
    {
        if (_quality is null)
        {
            return null;
        }

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var guardId in _declaration.Guards.Ids)
        {
            if (_declaration.Guards.Matches(guardId, raw, working))
            {
                matched.Add(guardId);
            }
        }

        var evidence = ReleaseEvidenceScanner.Scan(new EvidenceScanRequest
        {
            Title = raw,
            ReleaseGroup = ReleaseGroupScanner.Scan(working),
            Guards = matched,
            Tags = tags,
        });

        return _quality.Label(_quality.Read(evidence), QualityLabelDetail.Standard);
    }

    private string ApplyPreRewrites(string working)
    {
        foreach (var rewrite in _declaration.PreRewrites)
        {
            try
            {
                working = rewrite.Expression.Replace(working, rewrite.Declaration.Replacement);
            }
            catch (RegexMatchTimeoutException)
            {
                // A rewrite that cannot finish over one hostile title changes nothing about it.
            }
        }

        return working;
    }

    private static ScannedReleaseTags ScanSafely(string raw, string working)
    {
        try
        {
            return ReleaseTagScanner.Scan(raw, working);
        }
        catch (RegexMatchTimeoutException)
        {
            return ScannedReleaseTags.Empty;
        }
    }

    private Dictionary<string, string> ScanTokenTables(string working)
    {
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var table in _declaration.TokenTables)
        {
            foreach (var row in table.Rows)
            {
                Match? selected;

                try
                {
                    var matches = row.Expression.Matches(working);
                    selected = table.Declaration.Occurrence == OccurrenceSelection.LastOccurrence
                        ? matches.LastOrDefault()
                        : matches.FirstOrDefault();
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }

                if (selected is not { Success: true })
                {
                    continue;
                }

                var value = row.Declaration.Value ?? CapturedValue(row.Expression, selected);

                if (row.Constraint.Accepts(value))
                {
                    // The first row that writes a tag wins, consistent with first-passing-row tables.
                    extra.TryAdd(row.Declaration.Tag, value);
                }
            }
        }

        return extra;
    }

    private static string CapturedValue(Regex expression, Match match)
    {
        // Null row value means the pattern's own capture is the value: the first explicitly named group
        // that matched, or the whole match when the pattern names none.
        foreach (var name in expression.GetGroupNames())
        {
            if (!int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                && match.Groups[name].Success)
            {
                return match.Groups[name].Value;
            }
        }

        return match.Value;
    }

    private (CompiledTitlePattern Pattern, Match Match, Dictionary<string, string> Captures)? Claim(
        string raw, string working)
    {
        foreach (var pattern in _declaration.TitlePatterns)
        {
            // Parse(string) reads release names; a pattern restricted to other provenances may not fire.
            var sources = pattern.Declaration.Sources;

            if (sources.Count > 0 && !sources.Contains(MatchSource.ReleaseName))
            {
                continue;
            }

            Match match;

            try
            {
                match = pattern.Expression.Match(working);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (!match.Success || !GuardsPass(pattern, raw, working))
            {
                continue;
            }

            var captures = CollectCaptures(pattern.Expression, match);

            if (!TitleOf(pattern, captures, out _))
            {
                continue;
            }

            if (!ExpansionAccepts(pattern.Declaration.Expansion, captures))
            {
                // An absurd range is a mis-parse, not a very large release; the pattern declines.
                continue;
            }

            return (pattern, match, captures);
        }

        return null;
    }

    private bool GuardsPass(CompiledTitlePattern pattern, string raw, string working)
    {
        foreach (var guardRef in pattern.Declaration.Guards)
        {
            // Pattern guards gate on the same declared expressions the rung rows reference. A plain
            // reference must match; a negated one must not.
            var matched = _declaration.Guards.Matches(guardRef.GuardId, raw, working);

            if (matched == guardRef.Negated)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, string> CollectCaptures(Regex expression, Match match)
    {
        var captures = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in expression.GetGroupNames())
        {
            if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            var group = match.Groups[name];

            if (group.Success && group.Value.Length > 0)
            {
                captures[name] = group.Value;
            }
        }

        return captures;
    }

    private static bool TitleOf(
        CompiledTitlePattern pattern, IReadOnlyDictionary<string, string> captures, out string title)
    {
        title = string.Empty;

        foreach (var binding in pattern.Declaration.Captures)
        {
            if (binding.Target == CaptureTarget.TitleText
                && captures.TryGetValue(binding.GroupName, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                title = value;
                return true;
            }
        }

        return false;
    }

    private static bool ExpansionAccepts(
        RangeExpansion? expansion, IReadOnlyDictionary<string, string> captures)
    {
        if (expansion is null)
        {
            return true;
        }

        if (!captures.TryGetValue(expansion.FromGroup, out var fromText)
            || !captures.TryGetValue(expansion.ToGroup, out var toText))
        {
            // A range pattern whose range did not capture is an ordinary single reading.
            return true;
        }

        return int.TryParse(fromText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var from)
            && int.TryParse(toText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var to)
            && from <= to
            && (to - from + 1) <= expansion.MaxSpan;
    }

    private static string MaskTitle(string working, Match match, CompiledTitlePattern pattern)
    {
        foreach (var binding in pattern.Declaration.Captures)
        {
            if (binding.Target != CaptureTarget.TitleText)
            {
                continue;
            }

            var group = match.Groups[binding.GroupName];

            if (group.Success && group.Value.Length > 0)
            {
                return working.Remove(group.Index, group.Length);
            }
        }

        return working;
    }

    private ParsedRelease Project(
        CompiledTitlePattern pattern,
        Dictionary<string, string> captures,
        ScannedReleaseTags tags,
        RungOutcome? rung,
        string? renderedQuality,
        ParsePredicateContext context)
    {
        TitleOf(pattern, captures, out var rawTitle);

        string? year = null;
        var alternates = new List<string>();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DeclarativeParseFields.PatternId] = pattern.Declaration.PatternId,
            [DeclarativeParseFields.RevisionVersion] =
                tags.Revision.Version.ToString(CultureInfo.InvariantCulture),
            [DeclarativeParseFields.RevisionReal] =
                tags.Revision.Real.ToString(CultureInfo.InvariantCulture),
            [DeclarativeParseFields.RevisionIsRepack] = tags.Revision.IsRepack ? "true" : "false",
        };

        foreach (var binding in pattern.Declaration.Captures)
        {
            if (!captures.TryGetValue(binding.GroupName, out var value))
            {
                continue;
            }

            switch (binding.Target)
            {
                case CaptureTarget.TitleYear:
                    year ??= value;
                    break;
                case CaptureTarget.AlternateTitle:
                    alternates.Add(CleanTitle(value));
                    break;
                case CaptureTarget.ExternalId:
                    metadata[DeclarativeParseFields.ExternalIdPrefix + binding.Key] = value;
                    break;
                case CaptureTarget.ReleaseKind:
                    metadata[DeclarativeParseFields.ReleaseKind] = binding.Key ?? value;
                    break;
                case CaptureTarget.CoordinateComponent:
                    metadata[DeclarativeParseFields.CoordinatePrefix + binding.SpaceId + "." + binding.ComponentId]
                        = value;
                    break;
                case CaptureTarget.Tag:
                    metadata[DeclarativeParseFields.TagPrefix + binding.Key] = value;
                    break;
                case CaptureTarget.TitleText:
                default:
                    break;
            }
        }

        foreach (var (key, value) in tags.Extra)
        {
            metadata.TryAdd(DeclarativeParseFields.TagPrefix + key, value);
        }

        if (alternates.Count > 0)
        {
            metadata[DeclarativeParseFields.AlternateTitles] = string.Join(", ", alternates);
        }

        if (tags.StatedResolution > 0)
        {
            metadata[DeclarativeParseFields.StatedResolution] = ResolutionToken(tags.StatedResolution);
        }

        if (tags.IsRemux)
        {
            metadata[DeclarativeParseFields.IsRemux] = "true";
        }

        WriteExpansion(pattern.Declaration.Expansion, captures, metadata);

        var carried = rung?.CarriedResolution ?? 0;
        var effectiveResolution = carried > 0 ? carried : context.StatedResolution;

        return new ParsedRelease(
            MediaKind,
            CleanTitle(rawTitle),
            year,
            renderedQuality ?? rung?.TierId,
            tags.VideoCodec,
            tags.AudioCodec,
            effectiveResolution > 0 ? ResolutionToken(effectiveResolution) : null,
            tags.ReleaseGroup,
            tags.Languages.Count == 0 ? null : [.. tags.Languages],
            metadata);
    }

    private static void WriteExpansion(
        RangeExpansion? expansion,
        IReadOnlyDictionary<string, string> captures,
        Dictionary<string, string> metadata)
    {
        if (expansion is null
            || !captures.TryGetValue(expansion.FromGroup, out var fromText)
            || !captures.TryGetValue(expansion.ToGroup, out var toText))
        {
            return;
        }

        metadata[DeclarativeParseFields.RangeFrom] = fromText;
        metadata[DeclarativeParseFields.RangeTo] = toText;
        metadata[DeclarativeParseFields.RangeIsSpan] = expansion.EmitAsSpan ? "true" : "false";
    }

    private static string ResolutionToken(int height) =>
        height.ToString(CultureInfo.InvariantCulture) + "p";

    /// <summary>
    /// Turns a captured title's separators back into spaces. The full dotted-title respace — the one
    /// with acronym rejoining — is the host strategy the audits named <c>dotted-title-respace</c>; this
    /// is the strategy-free baseline every kind gets.
    /// </summary>
    private static string CleanTitle(string captured)
    {
        var title = captured.Trim(' ', '.', '-', '_');

        if (!title.Contains(' ', StringComparison.Ordinal) && title.Contains('.', StringComparison.Ordinal))
        {
            title = title.Replace('.', ' ');
        }

        var builder = new StringBuilder(title.Length);
        var previousWasSpace = false;

        foreach (var character in title)
        {
            var isSpace = character == ' ';

            if (!isSpace || !previousWasSpace)
            {
                builder.Append(character);
            }

            previousWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }
}
