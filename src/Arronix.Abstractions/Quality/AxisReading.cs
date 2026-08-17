using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>What one axis says about one file.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct AxisReading
{
    private readonly IReadOnlyList<AxisValue>? values;

    /// <summary>Gets the axis.</summary>
    public required QualityAxisId Axis { get; init; }

    /// <summary>Gets the values read. Empty when nothing was read or nothing was found.</summary>
    public IReadOnlyList<AxisValue> Values
    {
        get => values ?? [];
        init => values = value;
    }

    /// <summary>Gets whether anything was looked for.</summary>
    public bool IsKnown { get; init; }

    /// <summary>Gets where the reading came from.</summary>
    public EvidenceSource Source { get; init; }

    /// <summary>Gets the single value of a single-valued axis, or <see cref="AxisValue.None"/>.</summary>
    public AxisValue Value => Values.Count == 1 ? Values[0] : AxisValue.None;

    /// <summary>Creates a reading meaning "nothing in the evidence spoke to this axis".</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The reading.</returns>
    public static AxisReading Absent(QualityAxisId axis) => new() { Axis = axis, IsKnown = false };

    /// <summary>Creates a single-valued reading.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="value">The value read.</param>
    /// <param name="source">Where it was read from.</param>
    /// <returns>The reading.</returns>
    public static AxisReading Of(QualityAxisId axis, AxisValue value, EvidenceSource source) =>
        new() { Axis = axis, Values = [value], IsKnown = true, Source = source };

    /// <summary>Creates a set-valued reading.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="source">Where the members were read from.</param>
    /// <param name="values">The values read. Empty means "we looked and found nothing".</param>
    /// <returns>The reading.</returns>
    public static AxisReading OfMany(QualityAxisId axis, EvidenceSource source, params AxisValue[] values) =>
        new() { Axis = axis, Values = values ?? [], IsKnown = true, Source = source };

    /// <summary>Gets whether the reading holds a value naming the same point as the one given.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Holds(AxisValue value)
    {
        foreach (var held in Values)
        {
            if (held.Names(value))
            {
                return true;
            }
        }

        return false;
    }
}
