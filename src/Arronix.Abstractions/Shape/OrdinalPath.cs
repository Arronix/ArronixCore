using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// A lexicographically comparable ordinal tuple of up to four components, stored inline.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a collection. An <c>IReadOnlyList&lt;long&gt;</c> inside a record gives reference
/// equality, so two paths with identical components would compare unequal — a silent de-duplication bug
/// in every dictionary and every distinct query the platform runs.
/// </para>
/// <para>
/// Four components cover every surveyed addressing scheme with one to spare, and an inline tuple keeps
/// the value allocation-free on the hot path where every item carries one. It renders as its dotted
/// form and reads back through <see cref="TryParse(ReadOnlySpan{char}, out OrdinalPath)"/>.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct OrdinalPath : IComparable<OrdinalPath>
{
    /// <summary>
    /// The greatest number of components a path may carry.
    /// </summary>
    public const int MaxLength = 4;

    private const char ComponentSeparator = '.';

    private readonly long _first;
    private readonly long _second;
    private readonly long _third;
    private readonly long _fourth;
    private readonly int _length;

    private OrdinalPath(long first, long second, long third, long fourth, int length)
    {
        _first = first;
        _second = second;
        _third = third;
        _fourth = fourth;
        _length = length;
    }

    /// <summary>
    /// Gets the path with no components, which compares less than every non-empty path.
    /// </summary>
    public static OrdinalPath Empty => default;

    /// <summary>
    /// Gets the number of populated components.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// Gets one component.
    /// </summary>
    /// <param name="index">The zero-based component index, outermost first.</param>
    /// <returns>The component value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside <see cref="Length"/>.</exception>
    public long this[int index] => index >= 0 && index < _length
        ? index switch
        {
            0 => _first,
            1 => _second,
            2 => _third,
            _ => _fourth
        }
        : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Creates a one-component path.</summary>
    /// <param name="first">The outermost component.</param>
    /// <returns>The path.</returns>
    public static OrdinalPath Of(long first) => new(first, 0, 0, 0, 1);

    /// <summary>Creates a two-component path.</summary>
    /// <param name="first">The outermost component.</param>
    /// <param name="second">The second component.</param>
    /// <returns>The path.</returns>
    public static OrdinalPath Of(long first, long second) => new(first, second, 0, 0, 2);

    /// <summary>Creates a three-component path.</summary>
    /// <param name="first">The outermost component.</param>
    /// <param name="second">The second component.</param>
    /// <param name="third">The third component.</param>
    /// <returns>The path.</returns>
    public static OrdinalPath Of(long first, long second, long third) => new(first, second, third, 0, 3);

    /// <summary>Creates a four-component path.</summary>
    /// <param name="first">The outermost component.</param>
    /// <param name="second">The second component.</param>
    /// <param name="third">The third component.</param>
    /// <param name="fourth">The innermost component.</param>
    /// <returns>The path.</returns>
    public static OrdinalPath Of(long first, long second, long third, long fourth)
        => new(first, second, third, fourth, MaxLength);

    /// <summary>
    /// Compares two paths component by component, treating a shorter path as lesser where the two share
    /// a prefix.
    /// </summary>
    /// <param name="other">The path to compare against.</param>
    /// <returns>A negative value, zero or a positive value as this path sorts before, with or after <paramref name="other"/>.</returns>
    public int CompareTo(OrdinalPath other)
    {
        var shared = Math.Min(_length, other._length);
        for (var index = 0; index < shared; index++)
        {
            var comparison = this[index].CompareTo(other[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return _length.CompareTo(other._length);
    }

    /// <summary>Determines whether one path sorts before another.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> sorts first.</returns>
    public static bool operator <(OrdinalPath left, OrdinalPath right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether one path sorts before another or equals it.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not sort after <paramref name="right"/>.</returns>
    public static bool operator <=(OrdinalPath left, OrdinalPath right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether one path sorts after another.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> sorts last.</returns>
    public static bool operator >(OrdinalPath left, OrdinalPath right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether one path sorts after another or equals it.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not sort before <paramref name="right"/>.</returns>
    public static bool operator >=(OrdinalPath left, OrdinalPath right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Attempts to read the dotted form.
    /// </summary>
    /// <param name="text">The text to read, for example <c>"3.14"</c>. Empty text yields <see cref="Empty"/>.</param>
    /// <param name="value">The path when the text was well-formed; otherwise <see cref="Empty"/>.</param>
    /// <returns><see langword="true"/> when the text was well-formed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out OrdinalPath value)
    {
        value = Empty;

        var components = (stackalloc long[MaxLength]);
        var count = 0;
        var remaining = text;

        while (!remaining.IsEmpty)
        {
            if (count == MaxLength)
            {
                return false;
            }

            var separator = remaining.IndexOf(ComponentSeparator);
            var segment = separator < 0 ? remaining : remaining[..separator];
            if (!long.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var component))
            {
                return false;
            }

            components[count++] = component;

            if (separator < 0)
            {
                break;
            }

            remaining = remaining[(separator + 1)..];
            if (remaining.IsEmpty)
            {
                return false;
            }
        }

        value = count switch
        {
            0 => Empty,
            1 => Of(components[0]),
            2 => Of(components[0], components[1]),
            3 => Of(components[0], components[1], components[2]),
            _ => Of(components[0], components[1], components[2], components[3])
        };

        return true;
    }

    /// <summary>
    /// Gets the dotted form, which <see cref="TryParse(ReadOnlySpan{char}, out OrdinalPath)"/> reads back.
    /// </summary>
    /// <returns>The path text, empty for <see cref="Empty"/>.</returns>
    public override string ToString() => _length switch
    {
        0 => string.Empty,
        1 => ToText(_first),
        2 => string.Concat(ToText(_first), ".", ToText(_second)),
        3 => string.Concat(ToText(_first), ".", ToText(_second), ".", ToText(_third)),
        _ => string.Concat(
            ToText(_first),
            ".",
            ToText(_second),
            ".",
            ToText(_third),
            ".",
            ToText(_fourth))
    };

    private static string ToText(long component) => component.ToString(CultureInfo.InvariantCulture);
}
