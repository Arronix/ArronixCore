using System.Linq;
using Arronix.Architecture.Tests.Repository;

namespace Arronix.Architecture.Tests.Naming;

/// <summary>
/// The nouns that belong to one media kind, and the test that decides whether an identifier uses one.
/// </summary>
/// <remarks>
/// <para>
/// Factored out of the rule it serves so that the detector itself can be tested. A governance rule is
/// only as good as its detector: one that under-reports passes forever while checking nothing, and one
/// that over-reports gets suppressed within a week. Both failure modes are silent, so both are asserted
/// against worked examples in <c>DetectorSelfTests</c>.
/// </para>
/// <para>
/// Matching is by word rather than by substring. The nouns below sit inside perfectly innocent English
/// identifiers - authorization, tracking, a bookmark - and the difference between "contains the letters"
/// and "uses the word" is the difference between a rule people keep and a rule people disable.
/// </para>
/// </remarks>
internal static class MediaVocabulary
{
    /// <summary>
    /// Gets the forbidden nouns, singular and plural.
    /// </summary>
    /// <remarks>
    /// Both numbers are listed rather than stemmed. Stemming would have to have an opinion about
    /// "series", and a governance rule should not contain a linguistics engine.
    /// </remarks>
    public static IReadOnlyList<string> Nouns { get; } =
    [
        "Series",
        "Episode", "Episodes",
        "Season", "Seasons",
        "Movie", "Movies",
        "Album", "Albums",
        "Track", "Tracks",
        "Artist", "Artists",
        "Book", "Books",
        "Author", "Authors"
    ];

    /// <summary>
    /// Determines whether an identifier is named after one media kind.
    /// </summary>
    /// <param name="identifier">The identifier.</param>
    /// <returns><see langword="true"/> when one of its words is a media noun.</returns>
    public static bool Names(string identifier) =>
        SourceScanner.Words(identifier).Any(word => Nouns.Contains(word, StringComparer.OrdinalIgnoreCase));
}
