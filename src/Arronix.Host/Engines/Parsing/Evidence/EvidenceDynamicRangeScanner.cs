using System.Linq;
using Arronix.Abstractions.Quality;

// The normalized token vocabulary is part of the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Reads every dynamic-range format a release states, not just one.
/// </summary>
/// <remarks>
/// <para>
/// <b>A release genuinely carries two at once.</b> A Dolby Vision layer over an HDR10+ base is real and
/// increasingly common, and a scan with one slot for it has three bad options: drop one layer, invent a
/// combined name for every pair, or pick a winner and lose the fact that the other layer is there. This
/// scanner reports the set, in the order the title stated it, and never invents a member.
/// </para>
/// <para>
/// Nothing here collapses a stated pair into a "with fallback" reading. Whether a base layer is a usable
/// fallback for a display that cannot render the top layer is a fact about the file, not about the title,
/// and a probe is what answers it.
/// </para>
/// </remarks>
internal static class EvidenceDynamicRangeScanner
{
    /// <summary>How much range each format carries, for the single-slot projection only.</summary>
    private static readonly Dictionary<string, int> Range = new(StringComparer.Ordinal)
    {
        [EvidenceDynamicRangeTokens.StandardDynamicRange] = 0,
        [EvidenceDynamicRangeTokens.HybridLogGamma] = 1,
        [EvidenceDynamicRangeTokens.HighDynamicRange10] = 2,
        [EvidenceDynamicRangeTokens.HighDynamicRange10Plus] = 3,
        [EvidenceDynamicRangeTokens.DolbyVision] = 4,
    };

    /// <summary>
    /// Reads the dynamic-range formats.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns>The formats, in the order the title stated them, without repeats.</returns>
    internal static IReadOnlyList<string> Read(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return
        [
            .. stream.OfClass(EvidenceTokenClass.DynamicRange)
                .Select(static token => token.Value)
                .Distinct(StringComparer.Ordinal)
        ];
    }
}
