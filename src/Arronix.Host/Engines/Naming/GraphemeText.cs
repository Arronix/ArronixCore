using System.Globalization;
using System.Text;

namespace Arronix.Host.Engines.Naming;

/// <summary>
/// Grapheme-cluster-aware text measurement and cutting.
/// </summary>
/// <remarks>
/// Rendered names are budgeted in UTF-8 bytes but cut between grapheme clusters, so a cut never splits a
/// surrogate pair and never separates a base letter from its combining mark
/// (<c>docs/design/naming-and-tokens.md</c> resolution #10 and §6.5(c) — the case Sonarr carries two
/// regression fixtures for). The <c>:N</c> specifier counts grapheme clusters, which is what a user
/// means by "N characters".
/// </remarks>
internal static class GraphemeText
{
    /// <summary>
    /// Counts the UTF-8 bytes of a value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The byte count.</returns>
    public static int ByteCount(string value) => Encoding.UTF8.GetByteCount(value);

    /// <summary>
    /// Caps a value at a number of grapheme clusters.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="cap">Positive keeps the head; negative keeps the tail.</param>
    /// <returns>The capped value, with no ellipsis of its own.</returns>
    /// <remarks>
    /// The tail form trims separators <i>before</i> filling the budget, so <c>:-13</c> yields thirteen
    /// graphemes — the surveyed implementation trims after budgeting and quietly delivers twelve
    /// (§6.5(g), deliberately not carried).
    /// </remarks>
    public static string CapGraphemes(string value, int cap)
    {
        if (cap == 0 || value.Length == 0)
        {
            return value;
        }

        var keepTail = cap < 0;
        var budget = Math.Abs(cap);
        var trimmed = keepTail ? value.TrimStart(' ', '.') : value.TrimEnd(' ', '.');
        var boundaries = Boundaries(trimmed);
        var count = boundaries.Count - 1;

        if (count <= budget)
        {
            return trimmed;
        }

        return keepTail
            ? trimmed[boundaries[count - budget]..]
            : trimmed[..boundaries[budget]];
    }

    /// <summary>
    /// Shortens a value so its UTF-8 encoding fits a byte budget, cutting between grapheme clusters.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="maxBytes">The budget. Non-positive yields an empty string.</param>
    /// <returns>The longest prefix that fits.</returns>
    public static string TrimToByteBudget(string value, int maxBytes)
    {
        if (maxBytes <= 0)
        {
            return string.Empty;
        }

        if (ByteCount(value) <= maxBytes)
        {
            return value;
        }

        var boundaries = Boundaries(value);
        var kept = 0;
        var bytes = 0;

        for (var index = 1; index < boundaries.Count; index++)
        {
            var clusterBytes = Encoding.UTF8.GetByteCount(value, boundaries[index - 1], boundaries[index] - boundaries[index - 1]);

            if (bytes + clusterBytes > maxBytes)
            {
                break;
            }

            bytes += clusterBytes;
            kept = boundaries[index];
        }

        return value[..kept].TrimEnd(' ', '.');
    }

    private static List<int> Boundaries(string value)
    {
        var boundaries = new List<int> { 0 };
        var enumerator = StringInfo.GetTextElementEnumerator(value);

        while (enumerator.MoveNext())
        {
            boundaries.Add(enumerator.ElementIndex + ((string)enumerator.Current).Length);
        }

        return boundaries;
    }
}
