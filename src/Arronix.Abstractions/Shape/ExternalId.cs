using System.Globalization;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// An identifier assigned by a catalog outside the platform, carried as a namespaced pair.
/// </summary>
/// <param name="Scheme">The catalog that assigned the identifier, lower-case: <c>"tvdb"</c>, <c>"isbn13"</c>.</param>
/// <param name="Value">The identifier as that catalog writes it.</param>
/// <remarks>
/// <para>
/// The surveyed applications split evenly between integer and string external identifiers, and no shared
/// contract can span that with a primitive: widening everything to <see cref="long"/> loses check digits
/// and leading zeros, and narrowing everything to <see cref="int"/> loses most catalogs outright.
/// </para>
/// <para>
/// <paramref name="Scheme"/> is an <b>open</b> vocabulary. The host carries, compares and groups by it
/// and never branches on it, so a new catalog needs no platform change.
/// </para>
/// </remarks>
public readonly record struct ExternalId(string Scheme, string Value)
{
    private const char SchemeSeparator = ':';

    /// <summary>
    /// Creates an external identifier.
    /// </summary>
    /// <param name="scheme">The assigning catalog.</param>
    /// <param name="value">The identifier text.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="ArgumentException">Either argument is <see langword="null"/>, empty or white space.</exception>
    public static ExternalId Of(string scheme, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ExternalId(scheme, value);
    }

    /// <summary>
    /// Creates an external identifier from a numeric catalog key.
    /// </summary>
    /// <param name="scheme">The assigning catalog.</param>
    /// <param name="value">The numeric identifier, rendered invariantly.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="ArgumentException"><paramref name="scheme"/> is <see langword="null"/>, empty or white space.</exception>
    public static ExternalId Of(string scheme, long value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        return new ExternalId(scheme, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Attempts to read the <c>scheme:value</c> form.
    /// </summary>
    /// <param name="text">The text to read, for example <c>"tvdb:81189"</c>.</param>
    /// <param name="id">The identifier when the text was well-formed; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the text was well-formed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? text, out ExternalId id)
    {
        id = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var separator = text.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (separator <= 0 || separator == text.Length - 1)
        {
            return false;
        }

        var scheme = text[..separator];
        var value = text[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        id = new ExternalId(scheme, value);
        return true;
    }

    /// <summary>
    /// Gets the <c>scheme:value</c> form, which <see cref="TryParse(string?, out ExternalId)"/> reads back.
    /// </summary>
    /// <returns>The identifier text.</returns>
    public override string ToString() => $"{Scheme}{SchemeSeparator}{Value}";
}

/// <summary>
/// Declares that a level admits identifiers from one external catalog.
/// </summary>
/// <remarks>
/// Declaring the catalogs is what lets the host offer identifier search, report which catalog an item
/// came from and detect two library entries that are the same work, without knowing what any of the
/// catalogs contain.
/// </remarks>
public sealed record ExternalIdScheme
{
    /// <summary>
    /// Gets the scheme token used in <see cref="ExternalId.Scheme"/>, lower-case.
    /// </summary>
    public required string Scheme { get; init; }

    /// <summary>
    /// Gets the catalog's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is the catalog the level's own records are keyed by.
    /// At most one scheme per level is primary.
    /// </summary>
    public bool IsPrimary { get; init; }
}
