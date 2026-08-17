using System.Linq;

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Reads how many times a release has been re-issued, and why.
/// </summary>
/// <remarks>
/// <para>
/// Three independent statements, kept independent. <b>Which issue this is</b> counts corrections to the
/// encode. <b>How many mislabel fixes</b> counts times the previous issue was the wrong content
/// altogether. <b>Whether it is a repack</b> says the encode did not change and only the packaging did.
/// Collapsing the three into one comparable number is what forces an argument about which of them should
/// outrank the others, and that argument belongs to whoever writes a policy, not to a scanner.
/// </para>
/// <para>
/// One arithmetic, stated once: <b>a first issue is one, a bare correction marker states the second
/// issue, and a numbered marker states the issue after the number it carries.</b> So a second repack is
/// the third issue. The count of corrections is one less than the issue number, and that subtraction
/// happens exactly once, downstream, where the axis is declared.
/// </para>
/// </remarks>
internal static class EvidenceRevisionScanner
{
    /// <summary>
    /// Reads the re-issue statements.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns>The statements.</returns>
    internal static EvidenceRevisionClaim Read(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var claims = stream.OfClass(EvidenceTokenClass.Revision).ToArray();

        var issue = 1;
        var mislabels = 0;
        var repacked = false;

        foreach (var claim in claims)
        {
            switch (claim.Value)
            {
                case EvidenceRevisionMarkers.Issue:
                    issue = Math.Max(issue, (int)claim.Magnitude);
                    break;

                case EvidenceRevisionMarkers.Mislabel:
                    mislabels++;
                    break;

                case EvidenceRevisionMarkers.Repack:
                    repacked = true;
                    break;

                default:
                    break;
            }
        }

        return new EvidenceRevisionClaim(issue, mislabels, repacked);
    }
}

/// <summary>
/// What a release stated about its own re-issues.
/// </summary>
/// <param name="Issue">Which issue this is. A first issue is one.</param>
/// <param name="Mislabels">How many times a previous issue carried the wrong content.</param>
/// <param name="IsRepack">Whether this issue is the same encode packaged again.</param>
internal readonly record struct EvidenceRevisionClaim(int Issue, int Mislabels, bool IsRepack);
