using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>
/// One axis reading, or the typed absence of one.
/// </summary>
/// <typeparam name="TValue">The axis's value type: an enum for a closed axis, a number for a quantity.</typeparam>
/// <remarks>
/// <para>
/// Not <c>TValue?</c>, for two reasons. A nullable carries no provenance, and provenance decides trust —
/// a resolution a release <i>title</i> claims and a resolution a container <i>probe</i> measured are not
/// the same evidence, and telling them apart is what removes a per-kind list of sources whose claims must
/// be ignored. And a nullable makes "absent" comparable by accident: <see langword="null"/> sorts
/// somewhere, silently, which is exactly how a sentinel "unknown" rung comes to exist.
/// </para>
/// <para>
/// There is deliberately no <c>Value</c> property that throws. An absent reading is a state a caller must
/// handle, and the type makes handling it the only thing you can do.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct Evidence<TValue>
    where TValue : struct
{
    private readonly TValue value;
    private readonly bool known;

    private Evidence(TValue value, EvidenceSource source)
    {
        this.value = value;
        this.known = true;
        Source = source;
    }

    /// <summary>Gets the reading meaning "nothing in the evidence spoke to this axis".</summary>
    public static Evidence<TValue> None => default;

    /// <summary>Gets whether anything was read.</summary>
    public bool IsKnown => known;

    /// <summary>Gets where the reading came from. Meaningless when nothing was read.</summary>
    public EvidenceSource Source { get; }

    /// <summary>Creates a reading with its provenance.</summary>
    /// <param name="value">The value read.</param>
    /// <param name="source">Where it was read from.</param>
    /// <returns>The reading.</returns>
    public static Evidence<TValue> From(TValue value, EvidenceSource source) => new(value, source);

    /// <summary>Reads the value when there is one.</summary>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="true"/> when a value was read.</returns>
    public bool TryGet(out TValue value)
    {
        value = this.value;

        return known;
    }

    /// <summary>Reads the value, or a stated fallback when there is none.</summary>
    /// <param name="fallback">The value to use when nothing was read.</param>
    /// <returns>The value or the fallback.</returns>
    public TValue Or(TValue fallback) => known ? value : fallback;
}
