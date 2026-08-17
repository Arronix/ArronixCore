using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Identifies one level of a media kind's containment hierarchy within the shape that declares it.
/// </summary>
/// <remarks>
/// <para>
/// Without two-way implicit conversions, as every identity type in the platform now is. An implicit
/// conversion would make a level identifier and a grouping-axis identifier mutually assignable, and
/// cross-referencing the wrong one is the exact confusion this brand exists to prevent: a shape names
/// levels from a dozen positions and the validator resolves every one of them.
/// </para>
/// <para>
/// Other identifiers in the shape model — axis, space, field and facet identifiers — stay plain
/// <c>string</c> dictionary keys, where a brand would buy nothing.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct MediaLevelId
{
    private MediaLevelId(string value) => Value = value;

    /// <summary>
    /// Gets the identifier text. Unique within the shape that declares the level.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a level identifier.
    /// </summary>
    /// <param name="value">The identifier text.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is <see langword="null"/>, empty or white space.
    /// </exception>
    public static MediaLevelId FromString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new MediaLevelId(value);
    }

    /// <summary>
    /// Attempts to create a level identifier.
    /// </summary>
    /// <param name="value">The identifier text.</param>
    /// <param name="id">The identifier when the text was usable; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the text was usable; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out MediaLevelId id)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            id = default;
            return false;
        }

        id = new MediaLevelId(value);
        return true;
    }

    /// <summary>
    /// Gets the identifier text, or an empty string for the default value.
    /// </summary>
    /// <returns>The identifier text.</returns>
    public override string ToString() => Value ?? string.Empty;
}
