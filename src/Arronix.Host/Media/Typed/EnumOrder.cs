using System.Globalization;
using System.Linq;

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Reads an enumeration's members in the order the enumeration declares them numerically.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the platform's own reflection helpers sort members by their <i>unsigned</i> binary
/// value, so a signed enumeration with a negative member reports that member last rather than first. For an
/// ordinary field's choices that is a cosmetic wrong; for an ordered selection policy it is a correctness
/// bug, because the order <i>is</i> the threshold — a value below every other one would sort above all of
/// them and "at least this far along" would mean the opposite of what it says.
/// </para>
/// <para>
/// The unsigned case is left on the platform's own ordering, which is already right for it.
/// </para>
/// </remarks>
internal static class EnumOrder
{
    /// <summary>
    /// Gets an enumeration's member names, least value first.
    /// </summary>
    /// <param name="enumType">The enumeration.</param>
    /// <returns>The names, in ascending numeric order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="enumType"/> is <see langword="null"/>.</exception>
    internal static IReadOnlyList<string> Names(Type enumType)
    {
        ArgumentNullException.ThrowIfNull(enumType);

        return IsUnsigned(enumType)
            ? Enum.GetNames(enumType)
            : [.. Enum.GetNames(enumType).OrderBy(name => SignedValue(enumType, name))];
    }

    /// <summary>
    /// Gets an enumeration's members, least value first.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration.</typeparam>
    /// <returns>The members, in ascending numeric order.</returns>
    internal static IReadOnlyList<TEnum> Values<TEnum>()
        where TEnum : struct, Enum =>
        IsUnsigned(typeof(TEnum))
            ? Enum.GetValues<TEnum>()
            : [.. Enum.GetValues<TEnum>().OrderBy(static member => Convert.ToInt64(member, CultureInfo.InvariantCulture))];

    private static bool IsUnsigned(Type enumType)
    {
        var underlying = Enum.GetUnderlyingType(enumType);

        return underlying == typeof(byte)
            || underlying == typeof(ushort)
            || underlying == typeof(uint)
            || underlying == typeof(ulong);
    }

    private static long SignedValue(Type enumType, string name) =>
        Convert.ToInt64(Enum.Parse(enumType, name), CultureInfo.InvariantCulture);
}
