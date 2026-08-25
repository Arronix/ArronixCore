using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Provider.Tmdb.Identity;

/// <summary>Owns the TMDb identifier scheme, canonical value grammar, and embedded marker syntax.</summary>
/// <remarks>Recognition is local and deterministic; Movies consumes only the resulting readings.</remarks>
public static partial class TmdbIdentity
{
    /// <summary>The external-identifier scheme this provider assigns.</summary>
    public const string Scheme = "tmdb";

    /// <summary>
    /// Recognizes every <c>{tmdb-12345}</c> marker embedded in a release, file, or folder name.
    /// </summary>
    /// <param name="text">The complete text being interpreted.</param>
    /// <returns>Canonical markers in source order; malformed values produce no reading.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<ExternalIdReading> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<ExternalIdReading>? readings = null;

        foreach (var match in MarkerPattern().EnumerateMatches(text))
        {
            var marker = text.Substring(match.Index, match.Length);
            var digits = marker[6..^1];

            if (!TryParseId(digits, out var id))
            {
                continue;
            }

            readings ??= [];
            readings.Add(new ExternalIdReading(
                ExternalId.Of(Scheme, id.ToString(CultureInfo.InvariantCulture)),
                marker,
                match.Index));
        }

        return readings ?? [];
    }

    /// <summary>
    /// Parses text against TMDb's canonical decimal movie-id grammar: a positive integer, no sign, no
    /// surrounding white space, no leading zero, and no value past <see cref="int.MaxValue"/>.
    /// </summary>
    /// <param name="value">The candidate id text.</param>
    /// <param name="id">The parsed id when <paramref name="value"/> is canonical; otherwise zero.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is canonical.</returns>
    public static bool TryParseId(string value, out int id)
    {
        ArgumentNullException.ThrowIfNull(value);
        id = default;

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed is <= 0 or > int.MaxValue
            || parsed.ToString(CultureInfo.InvariantCulture) != value)
        {
            return false;
        }

        id = (int)parsed;
        return true;
    }

    /// <summary>Determines whether a deserialized TMDb identifier is canonical.</summary>
    /// <param name="value">The id as TMDb's JSON deserialized it.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a canonical TMDb id.</returns>
    public static bool IsCanonicalId(int value) =>
        TryParseId(value.ToString(CultureInfo.InvariantCulture), out var parsed) && parsed == value;

    [GeneratedRegex(@"\{tmdb-\d+\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();
}
