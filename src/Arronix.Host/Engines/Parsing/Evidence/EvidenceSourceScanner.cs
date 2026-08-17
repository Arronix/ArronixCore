using System.Linq;
using Arronix.Abstractions.Quality;

// The normalized token vocabulary is part of the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Reads which signal a release states it descends from, and whether it carries that signal whole.
/// </summary>
/// <remarks>
/// The scan reports a signal and never a rank. Whether a disc bitstream is worth more than a stream
/// capture is a question about somebody's preference, and this scanner is structurally incapable of
/// having an opinion about it: it returns a token out of a flat vocabulary.
/// </remarks>
internal static class EvidenceSourceScanner
{
    /// <summary>
    /// Reads the source token.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns>The normalized source token, or <see langword="null"/> when the release states none.</returns>
    /// <remarks>
    /// <para>
    /// <b>The most specific claim wins, and two equally specific claims that disagree cancel.</b>
    /// Specificity is how many segments the phrase spanned, so <c>HD DVD Rip</c> beats a stray <c>DVD</c>
    /// and <c>Blu-ray</c> beats a stray <c>BD</c>.
    /// </para>
    /// <para>
    /// Cancelling rather than picking a winner is the deliberate half. A title naming two unrelated
    /// sources at the same strength is a title contradicting itself, and the two failure directions are
    /// not symmetric: a missing source leaves the release unranked and visible, while a wrong source
    /// promotes it past everything the user actually asked for.
    /// </para>
    /// </remarks>
    internal static string? Read(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return EvidenceClaims.MostSpecific(stream.OfClass(EvidenceTokenClass.Source));
    }

    /// <summary>
    /// Reads whether the release states that it carries a master's own bitstream.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns><see langword="true"/> when a remux spelling is present.</returns>
    /// <remarks>
    /// Stated independently of the source, and deliberately so: a remux spelling with no source beside it
    /// is a real and common arrangement, and the scan's job is to report that the title said "remux" and
    /// said nothing about a disc. What that combination is worth is a reading, not a scan.
    /// </remarks>
    internal static bool ReadBitstreamClaim(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return stream.Has(EvidenceTokenClass.Remux);
    }

    /// <summary>
    /// Says whether the release states a streaming source.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    internal static bool StatesStream(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return stream.OfClass(EvidenceTokenClass.Source).Any(static token =>
            token.Value is EvidenceSourceTokens.Web
                or EvidenceSourceTokens.WebDownload
                or EvidenceSourceTokens.WebRip);
    }
}
