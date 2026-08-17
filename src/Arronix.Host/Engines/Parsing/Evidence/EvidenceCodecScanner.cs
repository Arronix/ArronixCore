using System.Linq;

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Reads the video codec and the audio presentation a release states.
/// </summary>
/// <remarks>
/// <para>
/// The video vocabulary reaches H.266 / VVC, which matters more than it looks: a release stating a codec
/// the scan does not know reads as stating no codec at all, and a codec is not a curiosity — it feeds the
/// expected-size model and it is one of the two things people most often write a rule about.
/// </para>
/// <para>
/// Neither vocabulary is ordered here. Codec efficiency is not fidelity: at an equal quality target a
/// more efficient codec produces a smaller file of the same quality, not a better one.
/// </para>
/// </remarks>
internal static class EvidenceCodecScanner
{
    /// <summary>
    /// Reads the video codec.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns>The normalized codec token, or <see langword="null"/>.</returns>
    internal static string? ReadVideoCodec(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return EvidenceClaims.MostSpecific(stream.OfClass(EvidenceTokenClass.VideoCodec));
    }

    /// <summary>
    /// Reads the audio presentation.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns>The normalized audio token, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <b>The richest presentation wins, and this is the one place a scanner ranks anything.</b> It is
    /// allowed to, because it is not ranking preference: one release genuinely carries several tracks at
    /// once — a lossy core beside a lossless extension, a stereo commentary beside a surround program —
    /// and the evidence record has one slot. What survives to the speakers is a physical fact about the
    /// tracks, so choosing the richest loses nothing a reader could have wanted. A listener who prefers
    /// the small file is stating a policy over the same evidence.
    /// </remarks>
    internal static string? ReadAudioFormat(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        string? richest = null;
        var rank = int.MinValue;

        foreach (var token in stream.OfClass(EvidenceTokenClass.AudioFormat))
        {
            if (!EvidenceVocabulary.AudioRichness.TryGetValue(token.Value, out var candidate)
                || candidate <= rank)
            {
                continue;
            }

            rank = candidate;
            richest = token.Value;
        }

        return richest;
    }

    /// <summary>
    /// Reads the frame rate.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns>The stated frame rate, or <see langword="null"/>.</returns>
    internal static double? ReadFrameRate(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var claims = stream.OfClass(EvidenceTokenClass.FrameRate).ToArray();

        return claims.Length == 1 ? claims[0].Magnitude : null;
    }
}
