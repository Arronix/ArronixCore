using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Engines.Parsing;

/// <summary>
/// The host's parse engine: executes one kind's declared title-reading model behind the stable
/// <see cref="IReleaseParser"/> seam, so downstream consumers cannot tell a declared kind from a
/// hand-written one and both registration paths flow through one pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Separator normalization runs first, then the kind's ordered pattern list. The first pattern whose
/// expression matches and whose guards pass claims the release. Only declared captures and token tables
/// are projected; format-specific interpretation happens after this media-neutral reader.
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
    private readonly Func<string, string>? _respace;
    /// <summary>Initializes a new instance of the <see cref="DeclarativeReleaseParser"/> class.</summary>
    /// <param name="shape">The kind's derived structure.</param>
    /// <param name="model">The kind's release models. Refused, with a message naming the offending
    /// row, when any declared cross-reference dangles.</param>
    internal DeclarativeReleaseParser(MediaShape shape, MediaKindModel model)
    {
        ArgumentNullException.ThrowIfNull(shape);
        MediaKind = shape.Kind;
        _declaration = new CompiledParseDeclaration(model);
        _respace = model.Respace;
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

        var extraTags = ScanTokenTables(working);

        var claim = Claim(raw, working);

        if (claim is not { } claimed)
        {
            return null;
        }

        var (pattern, match, captures) = claimed;

        return Project(pattern, captures, extraTags);
    }

    /// <inheritdoc />
    public bool CanParse(string releaseTitle) => Parse(releaseTitle) is not null;

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

    private ParsedRelease Project(
        CompiledTitlePattern pattern,
        Dictionary<string, string> captures,
        IReadOnlyDictionary<string, string> extraTags)
    {
        TitleOf(pattern, captures, out var rawTitle);

        string? year = null;
        var alternates = new List<string>();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DeclarativeParseFields.PatternId] = pattern.Declaration.PatternId,
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

        foreach (var (key, value) in extraTags)
        {
            metadata.TryAdd(DeclarativeParseFields.TagPrefix + key, value);
        }

        if (alternates.Count > 0)
        {
            metadata[DeclarativeParseFields.AlternateTitles] = string.Join(", ", alternates);
        }

        WriteExpansion(pattern.Declaration.Expansion, captures, metadata);

        return new ParsedRelease(
            MediaKind: MediaKind,
            Title: CleanTitle(rawTitle),
            Year: year,
            AdditionalMetadata: metadata);
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

    /// <summary>Normalizes separators using the media definition's title rewrite when supplied.</summary>
    private string CleanTitle(string captured)
    {
        var title = captured.Trim(' ', '-', '_');

        if (_respace is not null)
        {
            var separatedTitle = title.Contains('.', StringComparison.Ordinal)
                && !title.EndsWith(".", StringComparison.Ordinal)
                    ? title + "."
                    : title;

            title = _respace(separatedTitle);
        }
        else
        {
            title = title.Trim('.');

            if (!title.Contains(' ', StringComparison.Ordinal) && title.Contains('.', StringComparison.Ordinal))
            {
                title = title.Replace('.', ' ');
            }
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
