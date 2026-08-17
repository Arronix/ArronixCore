using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Arronix.Abstractions.Quality;

/// <summary>A set-valued reading, for an axis a release can carry several members of at once.</summary>
/// <typeparam name="TValue">The member type.</typeparam>
/// <remarks>
/// An empty set and an absent reading are different: "the evidence named no flaws" and "we did not look"
/// are different claims, and a policy that refuses a flaw must not refuse a release it never inspected.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct EvidenceSet<TValue>
    where TValue : struct, Enum
{
    private readonly TValue[]? members;
    private readonly bool known;

    private EvidenceSet(EvidenceSource source, TValue[] members)
    {
        this.members = members;
        this.known = true;
        Source = source;
    }

    /// <summary>Gets the reading meaning "we did not look".</summary>
    public static EvidenceSet<TValue> None => default;

    /// <summary>Gets whether anything was looked for.</summary>
    public bool IsKnown => known;

    /// <summary>Gets where the reading came from.</summary>
    public EvidenceSource Source { get; }

    /// <summary>Gets the members found. Empty when nothing was found or nothing was looked for.</summary>
    public IReadOnlyList<TValue> Members => members ?? [];

    /// <summary>Gets the reading meaning "we looked and found nothing".</summary>
    /// <param name="source">Where the absence was established.</param>
    /// <returns>The reading.</returns>
    public static EvidenceSet<TValue> Empty(EvidenceSource source) => new(source, []);

    /// <summary>Creates a reading holding the stated members.</summary>
    /// <param name="source">Where the members were read from.</param>
    /// <param name="members">The members found.</param>
    /// <returns>The reading.</returns>
    public static EvidenceSet<TValue> Of(EvidenceSource source, params TValue[] members) =>
        new(source, members is null or [] ? [] : members.Distinct().ToArray());

    /// <summary>Gets whether a member was found.</summary>
    /// <param name="member">The member.</param>
    /// <returns><see langword="true"/> when the reading holds it.</returns>
    public bool Has(TValue member) => members is not null && Array.IndexOf(members, member) >= 0;
}
