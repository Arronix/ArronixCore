#pragma warning disable ARX0019 // Definition contracts are experimental; these tests exercise the declaration.
#pragma warning disable ARX0021 // Quality contracts are experimental; these tests exercise the axes model.

using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Quality;
using Arronix.Host.Engines.Parsing.Evidence;

namespace Arronix.Plugin.Movies.Tests.Support;

/// <summary>
/// The declared guards, evaluated the way the host evaluates them, so a fixture can hand one release to
/// the shared family and to this kind's refinement exactly as the parse engine does.
/// </summary>
/// <remarks>
/// <para>
/// The guards are declared data — an expression, which text form it reads, and whether it is
/// case-sensitive — so running them here is running the declaration rather than re-implementing it. What
/// this fixture cannot see is the title mask the parse engine applies before its own scans, which is why
/// the end-to-end corpus assertions go through the real engine and these axis assertions do not.
/// </para>
/// <para>
/// Separator normalization is the one thing that must match, because the guards are written against the
/// working form and not the raw one: a guard reading a spaced spelling would silently never fire against a
/// raw title.
/// </para>
/// </remarks>
internal static class MoviesGuards
{
    private static readonly Dictionary<string, (Regex Expression, GuardInput Input)> Compiled = Compile();

    /// <summary>Scans one release title into evidence, with this kind's guards attached.</summary>
    /// <param name="releaseTitle">The release title.</param>
    /// <param name="guards">The guards that matched.</param>
    /// <returns>The evidence.</returns>
    internal static ReleaseEvidence Scan(string releaseTitle, IReadOnlySet<string> guards) =>
        ReleaseEvidenceScanner.Scan(new EvidenceScanRequest { Title = releaseTitle, Guards = guards });

    /// <summary>Reports which declared guards one release title matches.</summary>
    /// <param name="releaseTitle">The release title.</param>
    /// <returns>The matching guard identifiers.</returns>
    internal static IReadOnlySet<string> Matching(string releaseTitle)
    {
        var raw = releaseTitle.Trim();
        var working = raw.Replace('_', ' ');
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (guardId, guard) in Compiled)
        {
            if (guard.Expression.IsMatch(guard.Input == GuardInput.Raw ? raw : working))
            {
                matched.Add(guardId);
            }
        }

        return matched;
    }

    private static Dictionary<string, (Regex, GuardInput)> Compile() =>
        MoviesDeclaration.Parsing.Guards.ToDictionary(
            static guard => guard.GuardId,
            static guard => (
                new Regex(
                    guard.Regex,
                    guard.CaseSensitive
                        ? RegexOptions.CultureInvariant
                        : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1)),
                guard.Input),
            StringComparer.Ordinal);
}
