using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Arronix.Abstractions.Quality;

/// <summary>One value on one axis, or the typed absence of one.</summary>
/// <remarks>
/// <para>
/// Carries both a comparable magnitude and the community's spelling, because the same value must serve a
/// comparison and a rendered file name, and deriving one from the other in either direction is how a
/// display string ends up load-bearing.
/// </para>
/// <para>
/// <b>Identity is the magnitude, never the spelling.</b> Two members of one closed axis are the same value
/// when they share a declared rank, whatever either one is spelled; two quantities are the same value when
/// they share a magnitude. That is what lets a policy a user composed in an editor name the same member a
/// family declared without the two having to agree on a word.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct AxisValue
{
    private readonly AxisValueKind kind;
    private readonly string? token;

    private AxisValue(AxisValueKind kind, int declaredRank, double magnitude, string? token)
    {
        this.kind = kind;
        this.token = token;
        DeclaredRank = declaredRank;
        Magnitude = magnitude;
    }

    /// <summary>Gets the absent value.</summary>
    public static AxisValue None => default;

    /// <summary>Gets whether there is a value at all.</summary>
    public bool IsKnown => kind != AxisValueKind.Absent;

    /// <summary>Gets the member's position in the declared order. Zero for a quantity.</summary>
    public int DeclaredRank { get; }

    /// <summary>Gets the quantity. Zero for a member.</summary>
    public double Magnitude { get; }

    /// <summary>Gets the community spelling, or the formatted quantity.</summary>
    public string Token => token ?? kind switch
    {
        AxisValueKind.Quantity => Magnitude.ToString(CultureInfo.InvariantCulture),
        AxisValueKind.Member => DeclaredRank.ToString(CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    /// <summary>
    /// Gets the number this value compares on: the declared rank of a member, the magnitude of a quantity.
    /// </summary>
    internal double Ordinate => kind == AxisValueKind.Member ? DeclaredRank : Magnitude;

    /// <summary>Creates a member of a closed axis.</summary>
    /// <param name="declaredRank">The member's position in the family's declared order.</param>
    /// <param name="token">The member's community spelling.</param>
    /// <returns>The value.</returns>
    public static AxisValue Member(int declaredRank, string token) =>
        new(AxisValueKind.Member, declaredRank, 0d, token);

    /// <summary>Creates a quantity.</summary>
    /// <param name="magnitude">The quantity, in the axis's unit.</param>
    /// <returns>The value.</returns>
    public static AxisValue Quantity(double magnitude) =>
        new(AxisValueKind.Quantity, 0, magnitude, null);

    /// <summary>Determines whether two values name the same point on one axis, ignoring their spelling.</summary>
    /// <param name="other">The other value.</param>
    /// <returns><see langword="true"/> when both are known and name the same point.</returns>
    public bool Names(AxisValue other) =>
        IsKnown && other.IsKnown && kind == other.kind && Ordinate.Equals(other.Ordinate);

    /// <summary>Gets the value's spelling.</summary>
    /// <returns>The spelling.</returns>
    public override string ToString() => IsKnown ? Token : string.Empty;
}

/// <summary>Which of the two shapes an <see cref="AxisValue"/> holds.</summary>
internal enum AxisValueKind
{
    Absent = 0,
    Member = 1,
    Quantity = 2,
}
