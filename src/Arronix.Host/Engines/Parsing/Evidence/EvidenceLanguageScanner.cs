using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Quality;

// Quality contracts are experimental; language claims are reported in their vocabulary.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Reads the languages a release states, and whether it states that it carries more than one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Language is quality-bearing, and the interesting half is the marker rather than the name.</b> A
/// dual-language marker beside a disc source and no rip marker is how a whole class of release states
/// that it is a bitstream copy: the second audio track had to be muxed in, and muxing is what a bitstream
/// copy is. Surfacing the marker as typed evidence is what lets that reading be a rule about a language
/// axis instead of a per-kind guard string with a nationality in its name.
/// </para>
/// <para>
/// So the claim carries two things: which language, and whether the claim came from a <i>marker</i>
/// rather than from a language <i>name</i>. A marker names no language — that is what makes it a marker —
/// so its claim carries the unknown language, and everything that reads it reads the flag.
/// </para>
/// </remarks>
internal static class EvidenceLanguageScanner
{
    /// <summary>
    /// Reads the language claims.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns>The named languages in stated order, followed by a marker claim when one survived.</returns>
    /// <remarks>
    /// <para>
    /// Names are looked up only over segments the release vocabulary did not already claim, so a language
    /// spelling can never steal a segment a longer, more specific phrase accounted for.
    /// </para>
    /// <para>
    /// <b>The two-letter markers additionally require a named language somewhere in the title.</b> They
    /// abbreviate "dual language", and a release that carries a second audio track without naming either
    /// language is not a thing the scan can distinguish from a two-letter group name. The self-describing
    /// markers — the ones that spell out that there are several — need no such support.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<LanguageClaim> Read(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var named = new List<LanguageClaim>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < stream.Segments.Count; index++)
        {
            if (stream.IsClaimed(index))
            {
                continue;
            }

            if (!EvidenceVocabulary.LanguageNames.TryGetValue(stream.Segments[index], out var language)
                || !seen.Add(language.Code))
            {
                continue;
            }

            named.Add(new LanguageClaim(new Language(language.Code, language.Name), false));
        }

        return StatesSeveral(stream, named.Count > 0)
            ? [.. named, new LanguageClaim(Language.Unknown, true)]
            : named;
    }

    private static bool StatesSeveral(EvidenceTokenStream stream, bool anyNamed)
    {
        foreach (var token in stream.OfClass(EvidenceTokenClass.LanguageMarker))
        {
            var spelling = stream.Segments[token.Index];
            var isAbbreviation = token.SegmentCount == 1 && spelling.Length <= 2;

            if (!isAbbreviation || anyNamed)
            {
                return true;
            }
        }

        return false;
    }
}
