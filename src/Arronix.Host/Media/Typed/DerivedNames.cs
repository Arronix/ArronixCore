using System.Globalization;
using System.Text;

namespace Arronix.Host.Media.Typed;

/// <summary>
/// The one place a declared name becomes an identifier or a label.
/// </summary>
/// <remarks>
/// Every identifier a typed kind publishes — field, level, axis, facet, token — is derived from a member
/// name by these three rules. Centralized because two rules that agree today and are written twice will
/// disagree eventually, and the failure would be a cross-reference that resolves in one derivation step and
/// not in the next.
/// </remarks>
internal static class DerivedNames
{
    /// <summary>
    /// Turns a member name into the identifier consumers reference it by: <c>OriginalTitle</c> becomes
    /// <c>originalTitle</c>, <c>ExternalIds</c> becomes <c>externalIds</c>.
    /// </summary>
    /// <param name="memberName">The member name.</param>
    /// <returns>The identifier.</returns>
    internal static string Identifier(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        if (!char.IsUpper(memberName[0]))
        {
            return memberName;
        }

        // A run of capitals is one word: "URL" lowercases whole, "URLTemplate" keeps "Template".
        var run = 0;
        while (run < memberName.Length && char.IsUpper(memberName[run]))
        {
            run++;
        }

        var lead = run <= 1 ? 1 : run == memberName.Length ? run : run - 1;

        return string.Concat(
            memberName[..lead].ToLower(CultureInfo.InvariantCulture),
            memberName[lead..]);
    }

    /// <summary>
    /// Turns a member name into a label: <c>OriginalTitle</c> becomes <c>Original title</c>.
    /// </summary>
    /// <param name="memberName">The member name.</param>
    /// <returns>The label.</returns>
    internal static string Label(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        var text = new StringBuilder(memberName.Length + 8);

        for (var index = 0; index < memberName.Length; index++)
        {
            var character = memberName[index];
            var startsWord =
                index > 0
                && char.IsUpper(character)
                && (!char.IsUpper(memberName[index - 1])
                    || (index + 1 < memberName.Length && char.IsLower(memberName[index + 1])));

            if (startsWord)
            {
                text.Append(' ');
                text.Append(char.ToLower(character, CultureInfo.InvariantCulture));
                continue;
            }

            text.Append(index == 0 ? char.ToUpper(character, CultureInfo.InvariantCulture) : character);
        }

        return text.ToString();
    }

    /// <summary>
    /// Turns a singular label into a plural one, for a kind that did not name itself.
    /// </summary>
    /// <param name="singular">The singular label.</param>
    /// <returns>The plural label.</returns>
    /// <remarks>
    /// Deliberately crude, and only ever a fallback: a kind that cares says both names on the builder. A
    /// general pluralizer in the contract assembly would be a dictionary of English irregulars sitting
    /// inside a platform whose labels are meant to be localizable.
    /// </remarks>
    internal static string Plural(string singular)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(singular);

        if (singular.EndsWith('s') || singular.EndsWith('x') || singular.EndsWith("ch", StringComparison.Ordinal))
        {
            return singular + "es";
        }

        return singular.EndsWith('y') && singular.Length > 1 && !"aeiou".Contains(singular[^2], StringComparison.Ordinal)
            ? string.Concat(singular.AsSpan(0, singular.Length - 1), "ies")
            : singular + "s";
    }

    /// <summary>
    /// Turns a member name into the word a naming token spells it with: <c>OriginalTitle</c> stays
    /// <c>OriginalTitle</c>, so a template reads <c>{Movie OriginalTitle}</c>.
    /// </summary>
    /// <param name="memberName">The member name.</param>
    /// <returns>The token word.</returns>
    internal static string TokenWord(string memberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        return char.ToUpper(memberName[0], CultureInfo.InvariantCulture) + memberName[1..];
    }
}
