using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>
/// One axis's identity inside its format family.
/// </summary>
/// <param name="Value">The identifier.</param>
/// <remarks>
/// <para>
/// An axis identity is derived from the property that declares it, never authored. <see cref="FromProperty"/>
/// is the single derivation: the host's axis reader calls it when it reads a quality-facts type, and a
/// policy names an axis by calling it with <c>nameof</c>. One function means the two cannot drift, which is
/// the whole reason the derivation is stated here rather than performed twice.
/// </para>
/// <para>
/// The identity is not a display name. Prose comes from <c>[Display]</c> and reaches a reader through
/// <see cref="QualityAxis.Name"/>.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct QualityAxisId(string Value)
{
    /// <summary>Gets the identity that names no axis.</summary>
    public static QualityAxisId None => default;

    /// <summary>Gets whether this identity names an axis.</summary>
    public bool IsNamed => !string.IsNullOrEmpty(Value);

    /// <summary>Derives an axis identity from the name of the property that declares the axis.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentException"><paramref name="propertyName"/> is empty.</exception>
    public static QualityAxisId FromProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

        return new QualityAxisId(propertyName);
    }

    /// <summary>Gets the identifier.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Value ?? string.Empty;
}
