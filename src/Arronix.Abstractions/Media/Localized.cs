using Arronix.Abstractions.DTOs;

namespace Arronix.Abstractions.Media;

/// <summary>A value rendered in one stated language.</summary>
/// <typeparam name="T">The owner-shaped value being localized.</typeparam>
/// <remarks>
/// The wrapper is common; the payload is not. A movie may localize a title-and-overview pair while a book
/// localizes a title-and-description pair, and neither shape is forced into the other.
/// </remarks>
public sealed record Localized<T>
    where T : notnull
{
    /// <summary>Creates a localized owner-shaped value.</summary>
    /// <param name="language">The BCP 47 language of the rendering.</param>
    /// <param name="value">The localized value.</param>
    public Localized(Language language, T value)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(value);
        Language = language;
        Value = value;
    }

    /// <summary>Gets the BCP 47 language of the rendering.</summary>
    public Language Language { get; }

    /// <summary>Gets the media-owned localized payload.</summary>
    public T Value { get; }
}
