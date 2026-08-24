using System.Text;

namespace Arronix.Common.Naming;

/// <summary>Defines equality for naming-token spellings across declaration, ownership, and rendering.</summary>
public static class NamingTokenName
{
    /// <summary>Lower-cases a token name and removes separators to produce its lookup and collision key.</summary>
    /// <param name="tokenName">The token name, with or without braces.</param>
    /// <returns>The canonical token key.</returns>
    /// <remarks>
    /// The naming grammar treats <c>{Series Title}</c>, <c>{series.title}</c>, and
    /// <c>{SERIES_TITLE}</c> as the same token. Every registry and renderer uses this operation so a spelling
    /// cannot bypass a reserved name or acquire a second owner.
    /// </remarks>
    public static string Canonicalize(string tokenName)
    {
        ArgumentNullException.ThrowIfNull(tokenName);

        var buffer = tokenName.Length <= 256
            ? stackalloc char[tokenName.Length]
            : new char[tokenName.Length];
        var length = 0;

        foreach (var rune in tokenName.EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune))
            {
                continue;
            }

            length += Rune.ToLowerInvariant(rune).EncodeToUtf16(buffer[length..]);
        }

        return new string(buffer[..length]);
    }
}
