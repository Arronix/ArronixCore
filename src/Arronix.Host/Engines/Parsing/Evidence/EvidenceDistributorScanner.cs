using System.Linq;

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// Reads which streaming service a capture names.
/// </summary>
/// <remarks>
/// <para>
/// Distributor codes are two to four letters and collide with everything: initialisms in work titles,
/// group names, and audio qualifiers. The guard is therefore not a longer expression but a fact about
/// arrangement — <b>a service code is claimed only in a title that also states a streaming source</b>.
/// A distributor code is a statement about where a stream came from, so a title with no stream in it is
/// not making that statement.
/// </para>
/// <para>
/// The guard is what makes the vocabulary affordable. Without it, every three-letter code has to be
/// argued about one at a time; with it, the remaining risk is a title that genuinely names a stream and
/// happens to contain the same three letters, which is a much smaller set.
/// </para>
/// </remarks>
internal static class EvidenceDistributorScanner
{
    /// <summary>
    /// Reads the distributor code.
    /// </summary>
    /// <param name="stream">The classified title.</param>
    /// <returns>The normalized code, or <see langword="null"/>.</returns>
    internal static string? Read(EvidenceTokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!EvidenceSourceScanner.StatesStream(stream))
        {
            return null;
        }

        var codes = stream.OfClass(EvidenceTokenClass.Distributor)
            .Select(static token => token.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return codes.Length == 1 ? codes[0] : null;
    }
}
