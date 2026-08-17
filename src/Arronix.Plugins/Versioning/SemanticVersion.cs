using System.Globalization;

namespace Arronix.Plugins.Versioning;

/// <summary>
/// A semantic version, as far as the loader needs one.
/// </summary>
/// <remarks>
/// <para>
/// Held host-side rather than promoted to the contract assembly. Nothing an extension does requires it to
/// parse a version: it writes one string into its manifest and the loader reads it. Promoting a parser
/// against zero callers is exactly what the three-tier promotion rule exists to prevent.
/// </para>
/// <para>
/// Build metadata is deliberately not modeled. Semantic Versioning 2.0.0 excludes it from precedence, so
/// a value that cannot affect any decision the loader makes would only be a second way to write the same
/// version.
/// </para>
/// </remarks>
public readonly record struct SemanticVersion : IComparable<SemanticVersion>, IComparable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticVersion"/> struct.
    /// </summary>
    /// <param name="major">The major component.</param>
    /// <param name="minor">The minor component.</param>
    /// <param name="patch">The patch component.</param>
    /// <param name="prerelease">
    /// The dot-separated prerelease identifiers, or <see langword="null"/> for a release version.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A component is negative.</exception>
    public SemanticVersion(int major, int minor, int patch, string? prerelease = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);

        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = string.IsNullOrEmpty(prerelease) ? null : prerelease;
    }

    /// <summary>
    /// Gets the major component.
    /// </summary>
    public int Major { get; }

    /// <summary>
    /// Gets the minor component.
    /// </summary>
    public int Minor { get; }

    /// <summary>
    /// Gets the patch component.
    /// </summary>
    public int Patch { get; }

    /// <summary>
    /// Gets the prerelease identifiers, or <see langword="null"/> for a release version.
    /// </summary>
    public string? Prerelease { get; }

    /// <summary>
    /// Gets a value indicating whether this is a prerelease version.
    /// </summary>
    public bool IsPrerelease => Prerelease is not null;

    /// <summary>
    /// Determines whether one version precedes another.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> precedes <paramref name="right"/>.</returns>
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether one version follows another.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> follows <paramref name="right"/>.</returns>
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether one version precedes or equals another.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not follow <paramref name="right"/>.</returns>
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether one version follows or equals another.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not precede <paramref name="right"/>.</returns>
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Reads a version, filling in absent components with zero.
    /// </summary>
    /// <param name="value">The text to read, for example <c>0.3</c> or <c>1.2.3-beta.1</c>.</param>
    /// <param name="version">The version when the text was well-formed; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the text was well-formed; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// A partial version widens to the right — <c>0.3</c> is <c>0.3.0</c>. Wildcards are not accepted, so
    /// there is exactly one way to write any given version and exactly one meaning for it.
    /// </remarks>
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.AsSpan().Trim();

        // Build metadata carries no precedence, so it is accepted and discarded rather than modeled.
        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            if (plus == text.Length - 1)
            {
                return false;
            }

            text = text[..plus];
        }

        string? prerelease = null;
        var hyphen = text.IndexOf('-');
        if (hyphen >= 0)
        {
            var candidate = text[(hyphen + 1)..];
            if (!IsWellFormedPrerelease(candidate))
            {
                return false;
            }

            prerelease = candidate.ToString();
            text = text[..hyphen];
        }

        Span<int> components = [0, 0, 0];
        var componentCount = 0;
        var start = 0;

        for (var index = 0; index <= text.Length; index++)
        {
            if (index != text.Length && text[index] != '.')
            {
                continue;
            }

            if (componentCount == 3)
            {
                return false;
            }

            var slice = text[start..index];
            if (!IsNumericIdentifier(slice) || !int.TryParse(slice, NumberStyles.None, CultureInfo.InvariantCulture, out var component))
            {
                return false;
            }

            components[componentCount++] = component;
            start = index + 1;
        }

        if (componentCount == 0)
        {
            return false;
        }

        version = new SemanticVersion(components[0], components[1], components[2], prerelease);
        return true;
    }

    /// <summary>
    /// Reads a version.
    /// </summary>
    /// <param name="value">The text to read.</param>
    /// <returns>The version.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not a well-formed version.</exception>
    public static SemanticVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
        {
            throw new FormatException($"'{value}' is not a well-formed semantic version.");
        }

        return version;
    }

    /// <summary>
    /// Compares this version with another by precedence.
    /// </summary>
    /// <param name="other">The version to compare with.</param>
    /// <returns>A negative value, zero or a positive value.</returns>
    public int CompareTo(SemanticVersion other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0)
        {
            return comparison;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <inheritdoc />
    int IComparable.CompareTo(object? obj) => obj switch
    {
        null => 1,
        SemanticVersion other => CompareTo(other),
        _ => throw new ArgumentException("The value is not a semantic version.", nameof(obj))
    };

    /// <summary>
    /// Gets the version in the form <c>major.minor.patch</c>, with the prerelease identifiers appended.
    /// </summary>
    /// <returns>The version text, which <see cref="TryParse(string?, out SemanticVersion)"/> reads back.</returns>
    public override string ToString()
        => Prerelease is null
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{Prerelease}");

    private static int ComparePrerelease(string? left, string? right)
    {
        // A release always follows a prerelease of the same triple.
        if (left is null)
        {
            return right is null ? 0 : 1;
        }

        if (right is null)
        {
            return -1;
        }

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var shared = Math.Min(leftParts.Length, rightParts.Length);

        for (var index = 0; index < shared; index++)
        {
            var comparison = ComparePrereleaseIdentifier(leftParts[index], rightParts[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int ComparePrereleaseIdentifier(string left, string right)
    {
        var leftNumeric = IsNumericIdentifier(left);
        var rightNumeric = IsNumericIdentifier(right);

        if (leftNumeric && rightNumeric)
        {
            // Leading zeroes are rejected at parse time, so the identifiers fit an int comparison.
            return long.Parse(left, NumberStyles.None, CultureInfo.InvariantCulture)
                .CompareTo(long.Parse(right, NumberStyles.None, CultureInfo.InvariantCulture));
        }

        // Numeric identifiers always have lower precedence than alphanumeric ones.
        if (leftNumeric)
        {
            return -1;
        }

        if (rightNumeric)
        {
            return 1;
        }

        return string.CompareOrdinal(left, right);
    }

    private static bool IsNumericIdentifier(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value.Length > 18)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        // A leading zero on a multi-digit numeric identifier is not a version, it is a typo.
        return value.Length == 1 || value[0] != '0';
    }

    private static bool IsWellFormedPrerelease(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        var start = 0;
        for (var index = 0; index <= value.Length; index++)
        {
            if (index != value.Length && value[index] != '.')
            {
                continue;
            }

            var identifier = value[start..index];
            if (identifier.IsEmpty)
            {
                return false;
            }

            var allDigits = true;
            foreach (var character in identifier)
            {
                var alphanumeric = character is (>= '0' and <= '9') or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '-';
                if (!alphanumeric)
                {
                    return false;
                }

                if (character is < '0' or > '9')
                {
                    allDigits = false;
                }
            }

            if (allDigits && !IsNumericIdentifier(identifier))
            {
                return false;
            }

            start = index + 1;
        }

        return true;
    }
}
