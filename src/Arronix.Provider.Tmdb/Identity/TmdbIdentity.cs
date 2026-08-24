using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;

namespace Arronix.Provider.Tmdb.Identity;

/// <summary>
/// The external identifier namespace and embedded marker spelling this provider owns, exclusively.
/// </summary>
/// <remarks>
/// Recognition here is local, deterministic, and performs no network call, as
/// <see cref="ICataloger.ReadExternalIds"/> requires. Movies does not know this scheme or its marker
/// syntax; it consumes only the resulting <see cref="ExternalIdReading"/> values.
/// </remarks>
public static partial class TmdbIdentity
{
    /// <summary>The external-identifier scheme this provider assigns.</summary>
    public const string Scheme = "tmdb";

    /// <summary>
    /// Recognizes every <c>{tmdb-12345}</c> marker embedded in a release, file, or folder name.
    /// </summary>
    /// <param name="text">The complete text being interpreted.</param>
    /// <returns>Every unambiguous marker recognized, in source order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<ExternalIdReading> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<ExternalIdReading>? readings = null;

        foreach (var match in MarkerPattern().EnumerateMatches(text))
        {
            var marker = text.Substring(match.Index, match.Length);
            var id = marker[6..^1];

            readings ??= [];
            readings.Add(new ExternalIdReading(ExternalId.Of(Scheme, id), marker, match.Index));
        }

        return readings ?? [];
    }

    [GeneratedRegex(@"\{tmdb-\d+\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();
}
