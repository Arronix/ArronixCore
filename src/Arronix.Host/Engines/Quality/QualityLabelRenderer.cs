using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Quality;

// Reads and produces the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// Renders a point in the community's vocabulary, and reads one of its own renderings back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant this type exists to keep: a label is produced from a point and is never read back for a
/// comparison.</b> Nothing that ranks, admits, cuts off or assesses a size touches a rendered string.
/// Reading one back exists for exactly two callers — a stored string and a label a user pasted — and both
/// convert to a point immediately.
/// </para>
/// <para>
/// <b>Reading back is verified rather than trusted.</b> A rule's predicate cannot be inverted in general,
/// so a parse builds the point a rule <i>states</i> — its equalities and its set memberships, plus whatever
/// the suffixes spelled — and then renders that point and checks it comes back as the string that went in.
/// A parse that cannot be re-rendered fails, which makes the guarantee an identity on every label this
/// renderer can produce rather than a claim about a rule table nobody re-read.
/// </para>
/// <para>
/// <b>The suffix list is ordered, and the order is what separates the two detail levels.</b> The first
/// suffix a family declares is the standard one and joins to the word without a separator, which is what
/// spells a resolution onto a source word. Every further suffix is a full-detail suffix, space-joined and
/// elided when it renders empty — which is how a correction count spells one word when there has been a
/// correction and nothing at all when there has not, without the renderer owning an opinion about which
/// axes are revisions.
/// </para>
/// </remarks>
internal sealed class QualityLabelRenderer
{
    private static readonly Regex Hole = new(@"\{[^{}]*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly FormatFamilyId family;
    private readonly IReadOnlyList<DeclaredAxis> axes;
    private readonly IReadOnlyList<CompiledLabel> labels;
    private readonly IReadOnlyList<DeclaredSuffix> suffixes;

    /// <summary>Initializes a new instance of the <see cref="QualityLabelRenderer"/> class.</summary>
    /// <param name="family">The family whose points are rendered.</param>
    /// <param name="axes">The declared axes, in declaration order.</param>
    /// <param name="labels">The rendering rules, in declared order; the first match wins.</param>
    /// <param name="suffixes">The declared suffixes, the first of which is the standard one.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    internal QualityLabelRenderer(
        FormatFamilyId family,
        IReadOnlyList<DeclaredAxis> axes,
        IReadOnlyList<CompiledLabel> labels,
        IReadOnlyList<DeclaredSuffix> suffixes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(suffixes);

        this.family = family;
        this.axes = axes;
        this.labels = labels;
        this.suffixes = suffixes;
    }

    /// <summary>Renders a point.</summary>
    /// <param name="point">The point.</param>
    /// <param name="detail">How much of the point to spell.</param>
    /// <returns>The label. Empty when no rule matches and the family declared no catch-all.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is <see langword="null"/>.</exception>
    internal string Render(QualityPoint point, QualityLabelDetail detail)
    {
        ArgumentNullException.ThrowIfNull(point);

        if (detail == QualityLabelDetail.Diagnostic)
        {
            return Diagnose(point);
        }

        if (WordFor(point) is not { } word)
        {
            return string.Empty;
        }

        if (detail == QualityLabelDetail.Source)
        {
            return word;
        }

        var rendered = new StringBuilder(word);

        foreach (var suffix in suffixes)
        {
            if (IsStandard(suffix) && suffix.AppliesWhen(word))
            {
                rendered.Append(Spell(suffix, point));

                break;
            }
        }

        if (detail != QualityLabelDetail.Full)
        {
            return rendered.ToString();
        }

        foreach (var suffix in suffixes)
        {
            if (IsStandard(suffix) || !suffix.AppliesWhen(word))
            {
                continue;
            }

            var part = Spell(suffix, point);

            if (part.Length > 0)
            {
                rendered.Append(' ').Append(part);
            }
        }

        return rendered.ToString();
    }

    /// <summary>
    /// Gets whether a suffix belongs to the standard label rather than to the full one.
    /// </summary>
    /// <param name="suffix">The suffix.</param>
    /// <returns><see langword="true"/> when it spells the standard label's own axis.</returns>
    /// <remarks>
    /// The standard label is a source word and one more axis; the full label is that plus the revision. So
    /// the axis of the <i>first</i> declared suffix is the standard label's axis, every suffix over that
    /// axis is a standard suffix — which is what lets one axis be spelled two ways for two families of words
    /// — and every suffix over any other axis belongs to the full label and is space-joined onto it.
    /// </remarks>
    private bool IsStandard(DeclaredSuffix suffix) =>
        suffixes.Count > 0 && suffix.Axis.Axis.Id == suffixes[0].Axis.Axis.Id;

    /// <summary>Reads one of this renderer's own labels back into a point.</summary>
    /// <param name="label">The label.</param>
    /// <param name="point">Receives the point.</param>
    /// <returns><see langword="true"/> when the label was understood.</returns>
    internal bool TryRead(string label, out QualityPoint point)
    {
        point = Empty();

        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var trimmed = label.Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var head = parts[0];
        var tail = parts[1..];

        foreach (var (word, standard) in Heads(head))
        {
            foreach (var rule in labels)
            {
                if (!string.Equals(rule.Label, word, StringComparison.Ordinal))
                {
                    continue;
                }

                var candidate = Assemble(rule, standard, tail);

                if (Renders(candidate, trimmed))
                {
                    point = candidate;

                    return true;
                }
            }
        }

        return false;
    }

    private bool Renders(QualityPoint candidate, string label) =>
        string.Equals(Render(candidate, QualityLabelDetail.Full), label, StringComparison.Ordinal)
        || string.Equals(Render(candidate, QualityLabelDetail.Standard), label, StringComparison.Ordinal)
        || string.Equals(Render(candidate, QualityLabelDetail.Source), label, StringComparison.Ordinal);

    private QualityPoint Assemble(CompiledLabel rule, AxisAssertion? standard, IReadOnlyList<string> tail)
    {
        var stated = new Dictionary<QualityAxisId, AxisReading>();

        foreach (var assertion in rule.Rule.Assertions)
        {
            State(stated, assertion);
        }

        if (standard is not null)
        {
            State(stated, standard);
        }

        foreach (var part in tail)
        {
            foreach (var suffix in suffixes)
            {
                if (!IsStandard(suffix) && TryUnspell(suffix, part, out var assertion) && assertion is not null)
                {
                    State(stated, assertion);

                    break;
                }
            }
        }

        var readings = new AxisReading[axes.Count];

        for (var index = 0; index < axes.Count; index++)
        {
            var id = axes[index].Axis.Id;

            readings[index] = stated.TryGetValue(id, out var reading) ? reading : AxisReading.Absent(id);
        }

        return new QualityPoint { Family = family, Readings = readings };
    }

    /// <summary>Records one value the label stated.</summary>
    /// <param name="stated">What the label has said so far.</param>
    /// <param name="assertion">The value.</param>
    /// <remarks>
    /// Everything read back from a label arrives as a <i>claim</i>, never as a measurement, and that is
    /// load-bearing rather than a default: a label is a rendering somebody wrote down, and a point rebuilt
    /// from one must not be able to outrank a file the platform has actually probed.
    /// </remarks>
    private static void State(Dictionary<QualityAxisId, AxisReading> stated, AxisAssertion assertion)
    {
        var id = assertion.Axis.Axis.Id;

        if (assertion.Axis.Kind == AxisValueShape.MemberSet)
        {
            var held = stated.TryGetValue(id, out var existing) ? existing.Values : [];
            var members = new AxisValue[held.Count + 1];

            for (var index = 0; index < held.Count; index++)
            {
                members[index] = held[index];
            }

            members[^1] = assertion.Value;
            stated[id] = AxisReading.OfMany(id, EvidenceSource.ReleaseTitle, members);

            return;
        }

        stated[id] = AxisReading.Of(id, assertion.Value, EvidenceSource.ReleaseTitle);
    }

    /// <summary>Splits a head word into the rule's word and whatever the standard suffix spelled.</summary>
    /// <param name="head">The first space-separated part of the label.</param>
    /// <returns>The candidate readings, the suffixed one first.</returns>
    private IEnumerable<(string Word, AxisAssertion? Standard)> Heads(string head)
    {
        foreach (var suffix in suffixes)
        {
            if (IsStandard(suffix)
                && TryUnspell(suffix, head, out var assertion, out var word)
                && assertion is not null)
            {
                yield return (word, assertion);
            }
        }

        yield return (head, null);
    }

    private static bool TryUnspell(DeclaredSuffix suffix, string part, out AxisAssertion? assertion) =>
        TryUnspell(suffix, part, out assertion, out _);

    private static bool TryUnspell(DeclaredSuffix suffix, string part, out AxisAssertion? assertion, out string rest)
    {
        assertion = null;
        rest = part;

        // A quantity is read by pattern first, because its vocabulary is infinite and a candidate spelling
        // could otherwise match the tail of a different number. The pattern is built from the format itself,
        // so it reads back exactly what that format writes.
        if (suffix.Axis.Kind == AxisValueShape.Quantity)
        {
            var match = suffix.Pattern.Match(part);

            if (match.Success
                && double.TryParse(
                    match.Groups[1].ValueSpan,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var magnitude))
            {
                assertion = new AxisAssertion(suffix.Axis, AxisValue.Quantity(magnitude));
                rest = part[..match.Index];

                return true;
            }
        }

        // Otherwise the vocabulary is finite, so the suffix is read back by spelling each candidate and
        // seeing which one the label ends with. That is also what covers a format whose hole spells a fixed
        // word rather than a number, which is how a count spells one word without the renderer having to
        // know what a correction is.
        foreach (var member in suffix.Candidates())
        {
            var spelled = Spell(suffix, member);

            if (spelled.Length > 0 && part.EndsWith(spelled, StringComparison.Ordinal))
            {
                assertion = new AxisAssertion(suffix.Axis, member);
                rest = part[..^spelled.Length];

                return true;
            }
        }

        return false;
    }

    private static string Spell(DeclaredSuffix suffix, QualityPoint point)
    {
        var reading = point[suffix.Axis.Axis.Id];

        return reading is { IsKnown: true, Values.Count: > 0 } ? Spell(suffix, reading.Values[0]) : string.Empty;
    }

    private static string Spell(DeclaredSuffix suffix, AxisValue value)
    {
        object argument = suffix.Axis.Kind == AxisValueShape.Quantity ? value.Magnitude : value.Token;

        return string.Format(CultureInfo.InvariantCulture, suffix.Format, argument);
    }

    /// <summary>Finds the word a point renders as.</summary>
    /// <param name="point">The point.</param>
    /// <returns>
    /// The first matching rule's word, or <see langword="null"/> when no rule matched at all. The two are
    /// different: a rule may deliberately render no word of its own and leave the whole label to a suffix,
    /// which is how a release whose origin nothing stated still renders the resolution it does state instead
    /// of discarding it.
    /// </returns>
    private string? WordFor(QualityPoint point)
    {
        foreach (var label in labels)
        {
            if (label.Rule.When(point))
            {
                return label.Label;
            }
        }

        return null;
    }

    private string Diagnose(QualityPoint point)
    {
        var rendered = new StringBuilder();

        foreach (var axis in axes)
        {
            var reading = point[axis.Axis.Id];

            if (reading is not { IsKnown: true, Values.Count: > 0 })
            {
                continue;
            }

            if (rendered.Length > 0)
            {
                rendered.Append(" · ");
            }

            for (var index = 0; index < reading.Values.Count; index++)
            {
                if (index > 0)
                {
                    rendered.Append('+');
                }

                rendered.Append(reading.Values[index].Token);
            }

            if (!string.IsNullOrEmpty(axis.Axis.Unit))
            {
                rendered.Append(' ').Append(axis.Axis.Unit);
            }
        }

        return rendered.ToString();
    }

    private QualityPoint Empty()
    {
        var readings = new AxisReading[axes.Count];

        for (var index = 0; index < axes.Count; index++)
        {
            readings[index] = AxisReading.Absent(axes[index].Axis.Id);
        }

        return new QualityPoint { Family = family, Readings = readings };
    }

    /// <summary>Turns a composite format into the pattern that reads its rendering back.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The pattern, capturing what the hole spelled.</returns>
    internal static Regex PatternFor(string format)
    {
        ArgumentNullException.ThrowIfNull(format);

        var pattern = new StringBuilder();
        var read = 0;
        var holes = 0;

        foreach (Match hole in Hole.Matches(format))
        {
            pattern.Append(Regex.Escape(format[read..hole.Index]));
            pattern.Append(@"(-?\d+(?:\.\d+)?)");
            read = hole.Index + hole.Length;
            holes++;
        }

        // A format with no hole spells the same word for every value, so there is nothing for a pattern to
        // read back and the candidate spellings are the only honest route.
        if (holes != 1)
        {
            return new Regex("(?!)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        pattern.Append(Regex.Escape(format[read..])).Append('$');

        return new Regex(pattern.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}

/// <summary>One rendering rule and the word it renders.</summary>
/// <param name="Rule">The rewritten rule.</param>
/// <param name="Label">The community's word.</param>
internal sealed record CompiledLabel(CompiledLabelRule Rule, string Label);

/// <summary>One declared suffix.</summary>
/// <param name="Axis">The axis it spells.</param>
/// <param name="Format">The composite format its value is spelled with.</param>
/// <param name="AppliesWhen">Which words take it.</param>
internal sealed record DeclaredSuffix(DeclaredAxis Axis, string Format, Func<string, bool> AppliesWhen)
{
    private Regex? pattern;

    /// <summary>Gets the pattern that reads this suffix's rendering back.</summary>
    internal Regex Pattern => pattern ??= QualityLabelRenderer.PatternFor(Format);

    /// <summary>Gets the values worth trying when reading a rendering back.</summary>
    /// <returns>The candidates.</returns>
    /// <remarks>
    /// A closed axis offers its members. A quantity offers one: the first count above nothing, which is the
    /// only quantity a format that spells a word rather than a number can be distinguishing. Everything
    /// else about a quantity is read by pattern, and every parse is verified by re-rendering, so a
    /// candidate list that is too short costs a failed parse and never a wrong point.
    /// </remarks>
    internal IEnumerable<AxisValue> Candidates() =>
        Axis.Kind == AxisValueShape.Quantity ? [AxisValue.Quantity(1)] : Axis.Axis.Members;
}
