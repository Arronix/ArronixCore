using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>
/// A format family's identity: the only thing that makes two quality points comparable.
/// </summary>
/// <param name="Value">The identifier.</param>
/// <remarks>
/// A quality type belongs to a format family rather than to a media kind, because quality is a property of
/// a file and a file belongs to a family. Two points of different families are never ranked against one
/// another: <see cref="QualityPolicy.Compare"/> refuses the pair rather than answering it.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct FormatFamilyId(string Value)
{
    /// <summary>Gets the identity that names no family.</summary>
    public static FormatFamilyId None => default;

    /// <summary>Gets whether this identity names a family.</summary>
    public bool IsNamed => !string.IsNullOrEmpty(Value);

    /// <summary>Creates a family identity.</summary>
    /// <param name="value">The identifier.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public static FormatFamilyId From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new FormatFamilyId(value);
    }

    /// <summary>Gets the identifier.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() => Value ?? string.Empty;
}
