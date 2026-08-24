using System;

namespace Arronix.Provider.Tmdb.Movies;

/// <summary>
/// Thrown instead of returning a durable <c>Movie</c> that this provider has no authority to identify.
/// </summary>
/// <remarks>
/// <para>
/// <c>MediaItemId</c> is a host-minted surrogate: nothing outside the platform chooses its value or its
/// range. G04 ("Close the typed provider-pairing contract") has not yet approved a construction or
/// materialization path by which a cataloger or curator supplies a not-yet-cataloged item's durable
/// identity — see the G04 exit gate in <c>docs/design/typed-media-roadmap.md</c> and the findings in
/// <c>docs/research/g05/tmdb-provider-pressure-test.md</c>.
/// </para>
/// <para>
/// Minting a placeholder key here would either collide with a real host-assigned one or misrepresent an
/// unmaterialized discovery result as a valid library record. This provider stops at the typed boundary
/// instead: every fact TMDb supplied was fetched, parsed, and mapped to the movie domain's exact shape
/// (<see cref="TmdbMovieMapper.ToMovie"/> proves that mapping directly), and only the identity assignment
/// is withheld pending an approved contract.
/// </para>
/// </remarks>
public sealed class MovieMaterializationNotSupportedException : NotSupportedException
{
    internal MovieMaterializationNotSupportedException(string message) : base(message)
    {
    }
}

/// <summary>Raises <see cref="MovieMaterializationNotSupportedException"/> with a consistent, actionable message.</summary>
internal static class MovieMaterializationBoundary
{
    public static MovieMaterializationNotSupportedException For(string operation, int matchCount) =>
        new(
            $"TMDb returned {matchCount} matching movie(s) for '{operation}', but this provider cannot " +
            "produce a durable Movie: MediaItemId is host-minted, and G04 has not yet defined how a " +
            "cataloger or curator supplies one before a durable key exists. See the G04 exit gate in " +
            "docs/design/typed-media-roadmap.md and docs/research/g05/tmdb-provider-pressure-test.md.");
}
