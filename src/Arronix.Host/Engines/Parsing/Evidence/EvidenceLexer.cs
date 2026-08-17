using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Quality;

// Quality contracts are experimental; the lexer states resolution claim forms in their vocabulary.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Splits a release title into classified tokens in one forward pass.
/// </summary>
/// <remarks>
/// <para>
/// The shape is a lexer over token classes rather than one expression per class. A title is cut into
/// segments at every character that is neither a letter, a digit nor a plus sign; the walk then takes the
/// longest phrase the vocabulary knows at each position, falling back to a handful of short anchored
/// expressions for the things that are shapes rather than words — a line count, a raster, an upscale
/// transformation, a frame rate, a re-issue number.
/// </para>
/// <para>
/// <b>Longest-phrase-first is the only precedence in the scan, and it is what dissolves most of the
/// ambiguity a per-class alternation has to hand-tune.</b> <c>WEB-DL</c> cuts into <c>web</c> and
/// <c>dl</c>, and the two-segment phrase claims both before the one-segment dual-language marker can see
/// the <c>dl</c>; <c>DTS-HD MA</c> claims three segments and takes the <c>ma</c> away from anything that
/// might have read it as a distributor. Neither needs a rule written about it.
/// </para>
/// <para>
/// What longest-phrase-first cannot settle is a segment that is a whole valid token and also a perfectly
/// ordinary word or initialism. Those go to <see cref="RequiresSupport"/>, which is one rule over one
/// list rather than a lookaround per spelling.
/// </para>
/// </remarks>
internal static partial class EvidenceLexer
{
    /// <summary>How far away a supporting token may sit, in segments.</summary>
    /// <remarks>
    /// Three, because a distributor code and a container marker routinely sit between a resolution and
    /// the source token it supports, and four would reach across a whole work title.
    /// </remarks>
    private const int SupportDistance = 3;

    /// <summary>
    /// The classes whose presence makes a short, ambiguous segment believable.
    /// </summary>
    /// <remarks>
    /// Audio formats are deliberately absent. <c>DD</c> is itself a two-letter audio spelling, so letting
    /// audio support a short token would let two unsupported guesses prop each other up — which is
    /// exactly the arrangement a group name like <c>MT-dd</c> produces beside a work title containing the
    /// word "Web".
    /// </remarks>
    private static readonly EvidenceTokenClass[] SupportingClasses =
    [
        EvidenceTokenClass.Resolution,
        EvidenceTokenClass.VideoCodec,
        EvidenceTokenClass.Source,
        EvidenceTokenClass.Remux,
        EvidenceTokenClass.DynamicRange,
        EvidenceTokenClass.Packaging,
        EvidenceTokenClass.Container,
    ];

    /// <summary>
    /// Reads a release title into classified tokens.
    /// </summary>
    /// <param name="title">The release title, as it arrived.</param>
    /// <returns>The tokens, with unsupported ambiguous claims already dropped.</returns>
    internal static EvidenceTokenStream Tokenize(string? title)
    {
        var segments = Segment(title);
        var claimed = new List<EvidenceToken>();

        var position = 0;
        while (position < segments.Length)
        {
            var span = TryPhrase(segments, position, claimed);
            if (span > 0)
            {
                position += span;
                continue;
            }

            ReadShapes(segments[position], position, claimed);
            position++;
        }

        return new EvidenceTokenStream(segments, Support(segments, claimed));
    }

    /// <summary>
    /// Cuts a title into normalized segments.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>The segments, lowercased.</returns>
    /// <remarks>
    /// A plus sign stays inside a segment because it is load-bearing in two vocabularies at once —
    /// <c>DD+</c> and <c>HDR10+</c> — and dropping it would fold each onto its weaker sibling. Every other
    /// punctuation mark separates, which is what makes <c>Blu-ray</c>, <c>Blu.ray</c>, <c>Blu_ray</c> and
    /// <c>Blu ray</c> the same two segments and removes a whole class of separator handling.
    /// </remarks>
    internal static string[] Segment(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return [];
        }

        var segments = new List<string>();
        var start = -1;

        for (var index = 0; index <= title.Length; index++)
        {
            var isTokenCharacter = index < title.Length
                && (char.IsLetterOrDigit(title[index]) || title[index] == '+');

            if (isTokenCharacter)
            {
                if (start < 0)
                {
                    start = index;
                }

                continue;
            }

            if (start >= 0)
            {
                segments.Add(title[start..index].ToLowerInvariant());
                start = -1;
            }
        }

        return [.. segments];
    }

    private static int TryPhrase(string[] segments, int position, List<EvidenceToken> claimed)
    {
        var longest = Math.Min(EvidenceVocabulary.LongestPhrase, segments.Length - position);

        for (var span = longest; span >= 1; span--)
        {
            var phrase = string.Join(' ', segments, position, span);
            if (!EvidenceVocabulary.Phrases.TryGetValue(phrase, out var produced))
            {
                continue;
            }

            foreach (var entry in produced)
            {
                claimed.Add(
                    new EvidenceToken(entry.Class, entry.Value, position, span, entry.Magnitude, entry.Form, null));
            }

            return span;
        }

        return 0;
    }

    /// <summary>
    /// Reads the tokens that are shapes rather than words.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <param name="index">Its position.</param>
    /// <param name="claimed">Receives the tokens.</param>
    /// <remarks>
    /// Each expression is anchored, under forty characters, and serves exactly one token class. That is a
    /// design rule rather than a style preference: it makes a single expression that quietly grows into a
    /// second class structurally impossible, and it keeps every one of them short enough to read.
    /// </remarks>
    private static void ReadShapes(string segment, int index, List<EvidenceToken> claimed)
    {
        // A transformation is the most specific statement a title can make about a raster: it names the
        // raster the file has AND the fact that the picture was enlarged into it.
        var upscale = UpscalePattern().Match(segment);
        if (upscale.Success)
        {
            claimed.Add(
                EvidenceToken.Lines(
                    index,
                    1,
                    int.Parse(upscale.Groups[2].Value, CultureInfo.InvariantCulture),
                    ResolutionClaimForm.LineCount,
                    ScanType.Progressive));
            claimed.Add(EvidenceToken.Of(EvidenceTokenClass.Flaw, EvidenceFlawTokens.Upscaled, index, 1));
            return;
        }

        var raster = RasterPattern().Match(segment);
        if (raster.Success)
        {
            claimed.Add(
                EvidenceToken.Lines(
                    index,
                    1,
                    int.Parse(raster.Groups[2].Value, CultureInfo.InvariantCulture),
                    ResolutionClaimForm.Raster,
                    null));
            return;
        }

        if (ReadRevision(segment, index, claimed))
        {
            return;
        }

        var rate = FrameRatePattern().Match(segment);
        if (rate.Success)
        {
            // A fractional rate written with a period cannot be read, because the period is a segment
            // separator and removing that would cost far more than a frame rate is worth. The range check
            // is what stops the fractional part being claimed on its own as a whole rate.
            var frames = double.Parse(rate.Groups[1].Value, CultureInfo.InvariantCulture);
            if (frames is >= 23d and <= 240d)
            {
                claimed.Add(EvidenceToken.Number(EvidenceTokenClass.FrameRate, "rate", index, 1, frames));
            }

            return;
        }

        var lines = LineCountPattern().Match(segment);
        if (lines.Success)
        {
            claimed.Add(
                EvidenceToken.Lines(
                    index,
                    1,
                    int.Parse(lines.Groups[1].Value, CultureInfo.InvariantCulture),
                    ResolutionClaimForm.LineCount,
                    lines.Groups[2].Value == "i" ? ScanType.Interlaced : ScanType.Progressive));

            // A compact spelling writes the source and the raster with no separator between them. The
            // raster is unambiguous on its own, so whatever precedes it is looked up as a phrase rather
            // than guessed at.
            if (lines.Index > 0
                && EvidenceVocabulary.Phrases.TryGetValue(segment[..lines.Index], out var prefix))
            {
                foreach (var entry in prefix)
                {
                    claimed.Add(
                        new EvidenceToken(entry.Class, entry.Value, index, 1, entry.Magnitude, entry.Form, null));
                }
            }

            return;
        }

        ReadChannelSuffixed(segment, index, claimed);
    }

    /// <summary>
    /// Reads a re-issue statement.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <param name="index">Its position.</param>
    /// <param name="claimed">Receives the tokens.</param>
    /// <returns><see langword="true"/> when the segment was a re-issue statement.</returns>
    /// <remarks>
    /// One place owns the arithmetic, and the arithmetic is one sentence: <b>a bare marker states the
    /// second issue and a numbered marker states the issue after the number it carries.</b> So a first
    /// issue is one, a bare correction is two, and a second repack is three. The evidence record carries
    /// the issue number rather than a correction count, and the axis that wants a count subtracts one —
    /// which is the only place the two spellings are allowed to disagree.
    /// </remarks>
    private static bool ReadRevision(string segment, int index, List<EvidenceToken> claimed)
    {
        if (string.Equals(segment, "real", StringComparison.Ordinal))
        {
            claimed.Add(EvidenceToken.Of(EvidenceTokenClass.Revision, EvidenceRevisionMarkers.Mislabel, index, 1));
            return true;
        }

        if (string.Equals(segment, "rerip", StringComparison.Ordinal))
        {
            claimed.Add(EvidenceToken.Of(EvidenceTokenClass.Revision, EvidenceRevisionMarkers.Repack, index, 1));
            claimed.Add(
                EvidenceToken.Number(EvidenceTokenClass.Revision, EvidenceRevisionMarkers.Issue, index, 1, 2d));
            return true;
        }

        var version = VersionPattern().Match(segment);
        if (version.Success)
        {
            claimed.Add(
                EvidenceToken.Number(
                    EvidenceTokenClass.Revision,
                    EvidenceRevisionMarkers.Issue,
                    index,
                    1,
                    double.Parse(version.Groups[1].Value, CultureInfo.InvariantCulture)));
            return true;
        }

        var repack = RepackPattern().Match(segment);
        if (repack.Success)
        {
            claimed.Add(EvidenceToken.Of(EvidenceTokenClass.Revision, EvidenceRevisionMarkers.Repack, index, 1));
            claimed.Add(
                EvidenceToken.Number(
                    EvidenceTokenClass.Revision,
                    EvidenceRevisionMarkers.Issue,
                    index,
                    1,
                    IssueNumber(repack.Groups[1].Value)));
            return true;
        }

        var proper = ProperPattern().Match(segment);
        if (proper.Success)
        {
            claimed.Add(
                EvidenceToken.Number(
                    EvidenceTokenClass.Revision,
                    EvidenceRevisionMarkers.Issue,
                    index,
                    1,
                    IssueNumber(proper.Groups[1].Value)));
            return true;
        }

        return false;
    }

    private static double IssueNumber(string stated) =>
        stated.Length == 0 ? 2d : double.Parse(stated, CultureInfo.InvariantCulture) + 1d;

    /// <summary>
    /// Reads an audio or codec spelling that has a channel or profile digit welded onto it.
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <param name="index">Its position.</param>
    /// <param name="claimed">Receives the tokens.</param>
    /// <remarks>
    /// <c>DD5.1</c> cuts into <c>dd5</c> and <c>1</c>, because the channel count is written with no
    /// separator before it. The whole segment is always tried first, so <c>ac3</c>, <c>mp3</c>,
    /// <c>x264</c> and <c>vc1</c> — spellings whose digit is part of the name — are never damaged.
    /// </remarks>
    private static void ReadChannelSuffixed(string segment, int index, List<EvidenceToken> claimed)
    {
        if (segment.Length < 3 || !char.IsAsciiDigit(segment[^1]))
        {
            return;
        }

        if (!EvidenceVocabulary.Phrases.TryGetValue(segment[..^1], out var stem))
        {
            return;
        }

        foreach (var entry in stem)
        {
            if (entry.Class is not (EvidenceTokenClass.AudioFormat or EvidenceTokenClass.VideoCodec))
            {
                continue;
            }

            claimed.Add(EvidenceToken.Of(entry.Class, entry.Value, index, 1));
        }
    }

    /// <summary>
    /// Drops ambiguous claims that nothing in the title supports.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <param name="claimed">Every claim the walk made.</param>
    /// <returns>The surviving claims.</returns>
    private static IReadOnlyList<EvidenceToken> Support(string[] segments, List<EvidenceToken> claimed)
    {
        var ambiguous = new bool[claimed.Count];
        for (var index = 0; index < claimed.Count; index++)
        {
            ambiguous[index] = RequiresSupport(segments, claimed[index]);
        }

        var kept = new List<EvidenceToken>(claimed.Count);
        for (var index = 0; index < claimed.Count; index++)
        {
            if (!ambiguous[index] || IsSupported(claimed, ambiguous, index))
            {
                kept.Add(claimed[index]);
            }
        }

        return kept;
    }

    /// <summary>
    /// Says whether a claim needs something else in the title to stand up.
    /// </summary>
    /// <param name="segments">The segments.</param>
    /// <param name="token">The claim.</param>
    /// <returns><see langword="true"/> when the claim is ambiguous on its own.</returns>
    /// <remarks>
    /// Two cases, and both are stated as a property of the spelling rather than as a list of spellings.
    /// <b>A one- or two-letter all-alphabetic segment</b> is indistinguishable from an initialism in a
    /// work title or a group name — <c>BD</c>, <c>DD</c>, <c>DV</c>, <c>DL</c>, <c>TS</c> and <c>WP</c>
    /// are all real vocabulary and all real English fragments. A digit rescues a segment from this
    /// (<c>4K</c>, <c>R5</c>, <c>v2</c>), because no work title spells a word that way. <b>The bare word
    /// <c>web</c></b> is the second case and is named explicitly: it is the one three-letter entry in the
    /// whole vocabulary that is also a common noun, and a title carrying it with no raster and no codec
    /// anywhere near it is a work title, not a stream capture.
    /// </remarks>
    private static bool RequiresSupport(string[] segments, EvidenceToken token)
    {
        if (token.SegmentCount != 1)
        {
            return false;
        }

        var text = segments[token.Index];

        if (string.Equals(text, EvidenceSourceTokens.Web, StringComparison.Ordinal))
        {
            return true;
        }

        return text.Length <= 2 && text.All(char.IsLetter);
    }

    private static bool IsSupported(IReadOnlyList<EvidenceToken> claimed, bool[] ambiguous, int subject)
    {
        var token = claimed[subject];

        for (var index = 0; index < claimed.Count; index++)
        {
            if (index == subject || ambiguous[index])
            {
                continue;
            }

            var candidate = claimed[index];
            if (Math.Abs(candidate.Index - token.Index) > SupportDistance)
            {
                continue;
            }

            if (Array.IndexOf(SupportingClasses, candidate.Class) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^(\d{3,4}|\dk)to(\d{3,4})p$")]
    private static partial Regex UpscalePattern();

    [GeneratedRegex(@"(?<!\d)(\d{3,4})x(\d{3,4})(?!\d)")]
    private static partial Regex RasterPattern();

    [GeneratedRegex(@"(?<!\d)(\d{3,4})([pi])(?![a-z0-9])")]
    private static partial Regex LineCountPattern();

    [GeneratedRegex(@"^(\d{2,3})fps$")]
    private static partial Regex FrameRatePattern();

    [GeneratedRegex(@"^v([1-9])$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^repack(\d*)$")]
    private static partial Regex RepackPattern();

    [GeneratedRegex(@"^proper(\d*)$")]
    private static partial Regex ProperPattern();
}

/// <summary>
/// The values a <see cref="EvidenceTokenClass.Revision"/> token carries.
/// </summary>
internal static class EvidenceRevisionMarkers
{
    /// <summary>Which issue this is. A first issue is one.</summary>
    internal const string Issue = "issue";

    /// <summary>The previous issue carried the wrong content.</summary>
    internal const string Mislabel = "mislabel";

    /// <summary>The same encode, packaged again.</summary>
    internal const string Repack = "repack";
}

/// <summary>
/// The classified tokens one title produced, with the segments they were read from.
/// </summary>
/// <param name="segments">The normalized segments.</param>
/// <param name="tokens">The surviving claims.</param>
internal sealed class EvidenceTokenStream(IReadOnlyList<string> segments, IReadOnlyList<EvidenceToken> tokens)
{
    private bool[]? _covered;

    /// <summary>Gets the normalized segments, in the order the title stated them.</summary>
    internal IReadOnlyList<string> Segments { get; } = segments;

    /// <summary>Gets the surviving claims, in the order the title stated them.</summary>
    internal IReadOnlyList<EvidenceToken> Tokens { get; } = tokens;

    /// <summary>Gets every claim of one class.</summary>
    /// <param name="tokenClass">The class.</param>
    /// <returns>The claims.</returns>
    internal IEnumerable<EvidenceToken> OfClass(EvidenceTokenClass tokenClass) =>
        Tokens.Where(token => token.Class == tokenClass);

    /// <summary>Gets whether any claim of one class survived.</summary>
    /// <param name="tokenClass">The class.</param>
    /// <returns><see langword="true"/> when one did.</returns>
    internal bool Has(EvidenceTokenClass tokenClass) => Tokens.Any(token => token.Class == tokenClass);

    /// <summary>Gets whether a segment was claimed by a surviving token.</summary>
    /// <param name="segmentIndex">The segment.</param>
    /// <returns><see langword="true"/> when something claimed it.</returns>
    /// <remarks>
    /// What this is for: the language table is looked up over the segments the release vocabulary did
    /// <i>not</i> claim, so a language spelling can never quietly steal a segment a longer, more specific
    /// phrase already accounted for.
    /// </remarks>
    internal bool IsClaimed(int segmentIndex)
    {
        if (_covered is null)
        {
            var covered = new bool[Segments.Count];
            foreach (var token in Tokens)
            {
                for (var offset = 0; offset < token.SegmentCount; offset++)
                {
                    var position = token.Index + offset;
                    if (position < covered.Length)
                    {
                        covered[position] = true;
                    }
                }
            }

            _covered = covered;
        }

        return segmentIndex >= 0 && segmentIndex < _covered.Length && _covered[segmentIndex];
    }
}
