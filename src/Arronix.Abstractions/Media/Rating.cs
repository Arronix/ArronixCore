using System.Text.Json.Serialization;

namespace Arronix.Abstractions.Media;

/// <summary>The numeric interval in which a published rating is expressed.</summary>
public readonly record struct RatingScale
{
    /// <summary>Creates a scale with an inclusive lower and upper bound.</summary>
    /// <param name="minimum">The least value on the scale.</param>
    /// <param name="maximum">The greatest value on the scale.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maximum"/> is not greater than <paramref name="minimum"/>.
    /// </exception>
    /// <remarks>
    /// Named as the constructor a deserializer rebuilds the value with. Without that, a struct with an
    /// implicit parameterless constructor and no settable member is rebuilt as its default, and a scale of
    /// zero to zero contains nothing: the rating that carries it then fails its own validation at a point
    /// that names neither the payload nor this type. The scale is an invariant, so the constructor that
    /// establishes it is the only way back into one.
    /// </remarks>
    [JsonConstructor]
    public RatingScale(decimal minimum, decimal maximum)
    {
        if (maximum <= minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum),
                maximum,
                "A rating scale's maximum must be greater than its minimum.");
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>Gets the least value on the scale.</summary>
    public decimal Minimum { get; }

    /// <summary>Gets the greatest value on the scale.</summary>
    public decimal Maximum { get; }

    /// <summary>Gets the conventional zero-to-five scale.</summary>
    public static RatingScale OutOfFive { get; } = new(0m, 5m);

    /// <summary>Gets the conventional zero-to-ten scale.</summary>
    public static RatingScale OutOfTen { get; } = new(0m, 10m);

    /// <summary>Gets the conventional percentage scale.</summary>
    public static RatingScale Percent { get; } = new(0m, 100m);

    /// <summary>Gets whether this is a constructed, non-empty interval.</summary>
    /// <remarks>Not written to the wire: it restates what the bounds already say.</remarks>
    [JsonIgnore]
    public bool IsValid => Maximum > Minimum;

    /// <summary>Determines whether a value belongs to this scale.</summary>
    public bool Contains(decimal value) => IsValid && value >= Minimum && value <= Maximum;

    /// <summary>Projects a value onto the unit interval without losing its original scale.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is outside this scale.</exception>
    public decimal Normalize(decimal value)
    {
        if (!Contains(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"The value must be between {Minimum} and {Maximum}, inclusive.");
        }

        return (value - Minimum) / (Maximum - Minimum);
    }
}

/// <summary>Whose judgment a rating represents.</summary>
public enum RatingVoice
{
    /// <summary>The source did not state whose judgment the value represents.</summary>
    Unspecified = 0,

    /// <summary>An aggregate of audience or reader votes.</summary>
    Audience = 1,

    /// <summary>An aggregate of published reviews.</summary>
    Critic = 2,

    /// <summary>The source's own editorial judgment rather than an aggregate.</summary>
    Editorial = 3
}

/// <summary>One numeric assessment published by one open-vocabulary authority.</summary>
/// <remarks>
/// A media type decides whether its item carries ratings. This type only preserves what any such rating
/// means: its authority, original scale, voice and sample size. It never names a vendor.
/// </remarks>
public sealed record Rating
{
    /// <summary>Creates one rating.</summary>
    /// <param name="source">The authority that published it.</param>
    /// <param name="value">The value in the source's original scale.</param>
    /// <param name="scale">The source's scale.</param>
    /// <param name="voice">Whose judgment it represents.</param>
    /// <param name="sampleSize">The number of votes or reviews behind an aggregate, when stated.</param>
    public Rating(
        string source,
        decimal value,
        RatingScale scale,
        RatingVoice voice,
        long? sampleSize = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (!scale.Contains(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Rating '{source}' must be between {scale.Minimum} and {scale.Maximum}, inclusive.");
        }

        if (sampleSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleSize), sampleSize, "A sample size cannot be negative.");
        }

        Source = source;
        Value = value;
        Scale = scale;
        Voice = voice;
        SampleSize = sampleSize;
    }

    /// <summary>Gets the open-vocabulary authority that published the rating.</summary>
    public string Source { get; }

    /// <summary>Gets the rating in its source's original scale.</summary>
    public decimal Value { get; }

    /// <summary>Gets the original scale.</summary>
    public RatingScale Scale { get; }

    /// <summary>Gets whose judgment the rating represents.</summary>
    public RatingVoice Voice { get; }

    /// <summary>Gets the number of votes or reviews behind an aggregate, when stated.</summary>
    public long? SampleSize { get; }

    /// <summary>Gets the rating projected onto the unit interval for like-voice comparisons.</summary>
    /// <remarks>
    /// Not written to the wire. It is <see cref="Value"/> divided through <see cref="Scale"/>, both of
    /// which the payload carries, so a written copy is a second source of truth that an untrusted payload
    /// can make disagree with them. Writing it also evaluates <see cref="RatingScale.Normalize"/>, which
    /// throws for a scale that does not contain its value — an unreadable rating would then fail while
    /// being written rather than while being validated.
    /// </remarks>
    [JsonIgnore]
    public decimal NormalizedValue => Scale.Normalize(Value);
}
