using System.Linq;

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// The one rule the single-valued scanners share for choosing between several claims.
/// </summary>
/// <remarks>
/// Written once rather than per scanner, because the rule <i>is</i> the semantics and three copies of it
/// would be three chances to drift. Every caller that needs a different rule has to say so out loud by
/// not calling this.
/// </remarks>
internal static class EvidenceClaims
{
    /// <summary>
    /// Picks the most specific claim, or none when two equally specific claims disagree.
    /// </summary>
    /// <param name="claims">The claims of one class.</param>
    /// <returns>The winning value, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Specificity is the number of segments a phrase spanned: a longer spelling states more, so it beats
    /// a shorter one that happens to sit in the same title. Repeated agreement is not a disagreement — a
    /// title stating the same thing twice states it once.
    /// </remarks>
    internal static string? MostSpecific(IEnumerable<EvidenceToken> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var ordered = claims.ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }

        var strongest = ordered.Max(static token => token.SegmentCount);
        var winners = ordered
            .Where(token => token.SegmentCount == strongest)
            .Select(static token => token.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return winners.Length == 1 ? winners[0] : null;
    }
}
