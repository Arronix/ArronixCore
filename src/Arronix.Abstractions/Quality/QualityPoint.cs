using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>One file's quality: a point in its family's axis space.</summary>
/// <remarks>
/// This is what a file record stores, a notification renders and a naming token spells. It carries no
/// rank, no weight and no name — a name is a rendering and a rank is a policy's opinion.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record QualityPoint
{
    /// <summary>Gets the family this point belongs to. The only thing that makes two points comparable.</summary>
    public required FormatFamilyId Family { get; init; }

    /// <summary>Gets the readings, one per declared axis, in the family's declaration order.</summary>
    public required IReadOnlyList<AxisReading> Readings { get; init; }

    /// <summary>Reads one axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The reading; an unknown reading when the axis is not declared.</returns>
    public AxisReading this[QualityAxisId axis]
    {
        get
        {
            foreach (var reading in Readings)
            {
                if (reading.Axis == axis)
                {
                    return reading;
                }
            }

            return AxisReading.Absent(axis);
        }
    }
}
