using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>What an absent reading means <i>in an ordering</i>. Two modes, both order-preserving.</summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="UnknownEvidence"/>. One type in all three places would put <c>Ignore</c>
/// and <c>Refuse</c> on <see cref="AxisPreference"/>: the first admits strict preference cycles, and the
/// second is a requirement wearing a preference's clothes.
/// </para>
/// <para>
/// <b>Why <c>Ignore</c> cannot live here.</b> Skipping an axis makes comparison lexicographic
/// <i>with skipping</i>, which is not transitive. Three points over three axes, all absent in different
/// places, produce a strict preference cycle; and because a grab happens whenever the comparison says
/// "better", a cycle is an unbounded download loop rather than a corner case. Both surviving modes map an
/// absent reading to a <b>fixed</b> element of the axis's order — the bottom, or a stated value — so each
/// entry stays a function into a totally ordered set and the lexicographic composition stays a total
/// preorder.
/// </para>
/// <para>
/// Both of the removed modes are retained where they are safe: <see cref="AxisRequirement.WhenUnknown"/>
/// and <see cref="AxisFloor.WhenUnknown"/> keep the full <see cref="UnknownEvidence"/>, because neither
/// orders anything.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct PreferenceUnknown
{
    private PreferenceUnknown(PreferenceUnknownMode mode, AxisValue assumption)
    {
        Mode = mode;
        Assumption = assumption;
    }

    /// <summary>Gets the mode in which an absent reading sorts below every present one.</summary>
    public static PreferenceUnknown Lowest => default;

    /// <summary>Gets the mode.</summary>
    public PreferenceUnknownMode Mode { get; }

    /// <summary>Gets the assumption. Absent unless <see cref="Mode"/> is <see cref="PreferenceUnknownMode.Assume"/>.</summary>
    public AxisValue Assumption { get; }

    /// <summary>Creates the mode in which an absent reading is read as a stated value.</summary>
    /// <param name="value">The assumption.</param>
    /// <returns>The mode.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is absent.</exception>
    /// <remarks>
    /// This is what a global "round an unrecognized reading up to the next rung" mode becomes: per axis
    /// and legible, so the policy says "a release that does not say it is a bitstream copy is not one" in
    /// one place a person can read and change.
    /// </remarks>
    public static PreferenceUnknown Assume(AxisValue value)
    {
        if (!value.IsKnown)
        {
            throw new ArgumentException("An assumption must be a value; assuming nothing is Lowest.", nameof(value));
        }

        return new PreferenceUnknown(PreferenceUnknownMode.Assume, value);
    }
}

/// <summary>The two ways a preference may read an absent value. There is deliberately no third.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum PreferenceUnknownMode
{
    /// <summary>An absent reading sorts below every present one.</summary>
    Lowest = 0,

    /// <summary>An absent reading is read as a stated value.</summary>
    Assume = 1,
}
