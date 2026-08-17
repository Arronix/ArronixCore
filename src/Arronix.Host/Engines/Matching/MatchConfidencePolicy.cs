using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

// Definition contracts are experimental; this is the host's own answer inside one of them.
#pragma warning disable ARX0013
#pragma warning disable ARX0019

namespace Arronix.Host.Engines.Matching;

/// <summary>
/// How far to trust a match, when the media kind says nothing.
/// </summary>
/// <remarks>
/// <para>
/// A confidence table answers one question: given that the matcher reached an item by identifier, or by
/// title and year, or by title alone, how sure is it? That question has the same answer for every media
/// kind, because it is about the strength of the evidence rather than about what the evidence is evidence
/// of. An identifier that resolves is exact for a movie, a series and a book alike.
/// </para>
/// <para>
/// It used to be declared per kind, which meant every kind copied the same four rows and any kind could
/// silently disagree with the others about what "exact" meant. It is host policy, so the host owns it, and
/// a kind that genuinely needs different rows still declares them — they take precedence entire.
/// </para>
/// </remarks>
internal static class MatchConfidencePolicy
{
    /// <summary>
    /// Gets the host's rows, in the order the matcher evaluates them.
    /// </summary>
    /// <remarks>
    /// Ordered strongest evidence first, and total over <see cref="MatchBasis"/> so that the matcher's
    /// "no row covered this" warning cannot be reached through the default. A basis added to the enum later
    /// without a row here would reach it, which is the intended way to notice.
    /// </remarks>
    internal static IReadOnlyList<ConfidenceRule> Default { get; } =
    [
        new ConfidenceRule(MatchBasis.Identifier, null, MatchConfidence.Exact),
        new ConfidenceRule(MatchBasis.Coordinate, null, MatchConfidence.High),
        new ConfidenceRule(MatchBasis.TitleWithYear, null, MatchConfidence.High),
        new ConfidenceRule(MatchBasis.Scope, null, MatchConfidence.Medium),
        new ConfidenceRule(MatchBasis.TitleOnly, null, MatchConfidence.Low),
    ];

    /// <summary>
    /// Chooses the rows a matcher evaluates for one kind.
    /// </summary>
    /// <param name="declared">What the kind declared, which may be nothing.</param>
    /// <returns>The kind's rows when it has any; otherwise the host's.</returns>
    /// <remarks>
    /// All or nothing rather than row-by-row merge. A kind that states its own table has thought about the
    /// whole of it, and quietly filling the gaps from the host's would produce behavior neither the kind nor
    /// the host wrote down.
    /// </remarks>
    internal static IReadOnlyList<ConfidenceRule> For(IReadOnlyList<ConfidenceRule> declared)
        => declared.Count > 0 ? declared : Default;
}
