using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>
/// What an absent reading means <i>to a requirement or a floor</i>. The typed replacement for a sentinel
/// rung and for a global round-up mode.
/// </summary>
/// <remarks>
/// Used by <see cref="AxisRequirement"/> and <see cref="AxisFloor"/> only. Ordering uses
/// <see cref="PreferenceUnknown"/>, which is a strict subset — see the remark there.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct UnknownEvidence
{
    private UnknownEvidence(UnknownEvidenceMode mode, AxisValue assumption)
    {
        Mode = mode;
        Assumption = assumption;
    }

    /// <summary>Gets the mode in which an absent reading sorts below every present one.</summary>
    public static UnknownEvidence Lowest => default;

    /// <summary>Gets the mode in which the requirement or floor does not apply when the reading is absent.</summary>
    public static UnknownEvidence Ignore => new(UnknownEvidenceMode.Ignore, AxisValue.None);

    /// <summary>Gets the mode in which a candidate with an absent reading is refused.</summary>
    public static UnknownEvidence Refuse => new(UnknownEvidenceMode.Refuse, AxisValue.None);

    /// <summary>Gets the mode.</summary>
    public UnknownEvidenceMode Mode { get; }

    /// <summary>Gets the assumption. Absent unless <see cref="Mode"/> is <see cref="UnknownEvidenceMode.Assume"/>.</summary>
    public AxisValue Assumption { get; }

    /// <summary>Creates the mode in which an absent reading is read as a stated value.</summary>
    /// <param name="value">The assumption.</param>
    /// <returns>The mode.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is absent.</exception>
    public static UnknownEvidence Assume(AxisValue value)
    {
        if (!value.IsKnown)
        {
            throw new ArgumentException("An assumption must be a value; assuming nothing is Lowest.", nameof(value));
        }

        return new UnknownEvidence(UnknownEvidenceMode.Assume, value);
    }
}

/// <summary>The four ways a requirement or a floor may read an absent value.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum UnknownEvidenceMode
{
    /// <summary>An absent reading sorts below every present one.</summary>
    Lowest = 0,

    /// <summary>The requirement or floor does not apply.</summary>
    Ignore = 1,

    /// <summary>A candidate with an absent reading is refused.</summary>
    Refuse = 2,

    /// <summary>An absent reading is read as a stated value.</summary>
    Assume = 3,
}
