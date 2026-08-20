using System.Linq;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// The bag of positions one item occupies, one reading per coordinate space it is addressed in.
/// </summary>
/// <remarks>
/// A fixed schema cannot express this. The surveyed leaf entities carry up to six coordinate fields,
/// several nullable and one with its own confidence flag, and which of them is populated is decided per
/// item and per release rather than per level. A bag of typed readings keeps that open without giving up
/// ordering, which a dictionary of strings would.
/// </remarks>
public sealed record CoordinateSet
{
    /// <summary>
    /// Gets the set carrying no readings.
    /// </summary>
    public static CoordinateSet Empty { get; } = new() { Readings = [] };

    /// <summary>
    /// Gets the readings, at most one per <see cref="CoordinateReading.SpaceId"/>.
    /// </summary>
    public required IReadOnlyList<CoordinateReading> Readings { get; init; }

    /// <summary>
    /// Creates a set from readings.
    /// </summary>
    /// <param name="readings">The readings, at most one per space.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="readings"/> is <see langword="null"/>.</exception>
    public static CoordinateSet Of(params CoordinateReading[] readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        return readings.Length == 0 ? Empty : new CoordinateSet { Readings = readings };
    }

    /// <summary>
    /// Looks up the reading for one space.
    /// </summary>
    /// <param name="spaceId">The space to look up.</param>
    /// <param name="reading">The reading when the space is populated; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the space is populated; otherwise <see langword="false"/>.</returns>
    public bool TryGet(string spaceId, out CoordinateReading reading)
    {
        foreach (var candidate in Readings)
        {
            if (string.Equals(candidate.SpaceId, spaceId, StringComparison.Ordinal))
            {
                reading = candidate;
                return true;
            }
        }

        reading = default;
        return false;
    }

    /// <summary>
    /// Returns a set carrying <paramref name="reading"/>, replacing any existing reading for the same
    /// space.
    /// </summary>
    /// <param name="reading">The reading to add or replace.</param>
    /// <returns>The new set. The original is unchanged.</returns>
    public CoordinateSet With(CoordinateReading reading)
    {
        var readings = Readings
            .Where(existing => !string.Equals(existing.SpaceId, reading.SpaceId, StringComparison.Ordinal))
            .Append(reading)
            .ToArray();

        return new CoordinateSet { Readings = readings };
    }
}
