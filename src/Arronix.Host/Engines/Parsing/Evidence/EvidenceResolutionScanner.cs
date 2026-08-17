using System.Linq;
using Arronix.Abstractions.Quality;

// Quality contracts are experimental; a resolution claim is reported in their vocabulary.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Reads what a release states about its raster, un-bucketed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Un-bucketed is the whole point.</b> A scan that folds an interlaced marker, an intermediate raster
/// and an upscale statement onto one round number destroys three distinctions before anything downstream
/// can reason about any of them: the scan type is gone, an intermediate raster is gone entirely, and an
/// upscale launders into a clean reading of the raster it was enlarged to. This scanner reports the line
/// count exactly as stated, reports separately <i>how</i> it was stated, and puts the scan type and the
/// upscale where they belong.
/// </para>
/// <para>
/// It also reports nothing at all when the release states nothing. There is no rounding, no nearest
/// neighbor and no container-shaped guess: an absent reading is representable downstream, so guessing
/// buys nothing and costs a false claim.
/// </para>
/// </remarks>
internal static class EvidenceResolutionScanner
{
    /// <summary>
    /// Reads the raster claim.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns>The claim, or an absent one.</returns>
    /// <remarks>
    /// <b>The most specific form wins; among equally specific forms the lowest claim wins.</b>
    /// Specificity is an ordering over forms of statement and not over values — an explicit line count is
    /// more specific than an explicit raster, which is more specific than a marketing name — so a title
    /// carrying both a line count and a marketing name is settled by the form, with no per-source rule
    /// and no list of sources whose claims must be distrusted. The lowest-claim tie-break is the same
    /// asymmetry the rest of the scan runs on: a missed claim ranks a release low, and a false claim
    /// promotes junk past everything asked for.
    /// </remarks>
    internal static EvidenceRasterClaim Read(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var claims = stream.OfClass(EvidenceTokenClass.Resolution).ToArray();
        if (claims.Length == 0)
        {
            return default;
        }

        var mostSpecific = claims.Min(static token => token.Form);
        var winner = claims
            .Where(token => token.Form == mostSpecific)
            .OrderBy(static token => token.Magnitude)
            .First();

        return new EvidenceRasterClaim((int)winner.Magnitude, winner.Form, winner.Scan);
    }
}

/// <summary>
/// What a release stated about its raster.
/// </summary>
/// <param name="Lines">The vertical resolution stated, or zero when none was stated.</param>
/// <param name="Form">How it was stated.</param>
/// <param name="Scan">The scan type the statement carried, when it carried one.</param>
internal readonly record struct EvidenceRasterClaim(int Lines, ResolutionClaimForm Form, ScanType? Scan)
{
    /// <summary>Gets whether the release stated a raster at all.</summary>
    internal bool IsStated => Lines > 0;
}
