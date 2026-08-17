using System.Globalization;

namespace Arronix.Common.Text;

/// <summary>
/// Conversions between byte counts and the binary size units operators read and write.
/// </summary>
/// <remarks>
/// <para>
/// Every member here works in binary multiples of 1024 and says so. The implementation this replaces divided
/// by 1024 and then labeled the result KB, MB and GB, which are decimal units of 1000 — so a value the
/// platform reported as "1.0 GB" was 1073741824 bytes, not 1000000000, and a size limit an operator wrote as
/// 1 GB silently became 7.4% larger than they asked for. The arithmetic was never wrong; the labels were, and
/// they are corrected here rather than the arithmetic, because 1024-based sizes are what disks, transfers and
/// configuration limits are actually expressed in.
/// </para>
/// <para>
/// The conversion members are named to match: a mebibyte is 1024², a gibibyte is 1024³.
/// </para>
/// </remarks>
public static class ByteSizeExtensions
{
    /// <summary>
    /// Number of bytes in one kibibyte, and the ratio between each adjacent pair of units below.
    /// </summary>
    private const ulong BytesPerKibibyte = 1024;

    /// <summary>
    /// Number of bytes in one mebibyte.
    /// </summary>
    private const long BytesPerMebibyte = 1024L * 1024L;

    /// <summary>
    /// Number of bytes in one gibibyte.
    /// </summary>
    private const long BytesPerGibibyte = 1024L * 1024L * 1024L;

    /// <summary>
    /// The binary unit suffixes, smallest first. Eight entries cover the whole range of
    /// <see cref="long"/>, whose maximum is just under 8 EiB.
    /// </summary>
    private static readonly string[] BinaryUnitSuffixes =
    [
        "B",
        "KiB",
        "MiB",
        "GiB",
        "TiB",
        "PiB",
        "EiB",
    ];

    /// <summary>
    /// Formats a byte count in the largest binary unit that leaves a value of at least one, to one decimal
    /// place.
    /// </summary>
    /// <param name="bytes">The number of bytes. Negative counts are formatted with a leading minus sign.</param>
    /// <returns>The formatted size, for example <c>"1.0 KiB"</c> or <c>"976.6 KiB"</c>.</returns>
    /// <remarks>
    /// Formatted with the invariant culture, because the result is as likely to reach a log file, a header or
    /// a machine-read report as a screen, and a size whose decimal separator depended on the host's locale
    /// could not be parsed back.
    /// </remarks>
    public static string ToBinarySizeString(this long bytes)
    {
        if (bytes == 0)
        {
            return "0 B";
        }

        // Taken as an unsigned magnitude so that long.MinValue, which has no positive counterpart, is
        // formatted exactly rather than approximated by long.MaxValue as it was before.
        var magnitude = bytes < 0 ? (ulong)(-(bytes + 1)) + 1 : (ulong)bytes;

        var unit = 0;
        var scale = 1UL;

        while (magnitude / scale >= BytesPerKibibyte && unit < BinaryUnitSuffixes.Length - 1)
        {
            scale *= BytesPerKibibyte;
            unit++;
        }

        // Chosen by repeated comparison rather than by a logarithm: the floating-point logarithm the
        // previous implementation used could land on the wrong side of an exact power of 1024 and report,
        // for instance, "0.9 MiB" for exactly one mebibyte.
        var scaled = (decimal)magnitude / scale;
        var sign = bytes < 0 ? "-" : string.Empty;

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1:n1} {2}",
            sign,
            scaled,
            BinaryUnitSuffixes[unit]);
    }

    /// <summary>
    /// Converts a count of mebibytes (1024² bytes each) to bytes.
    /// </summary>
    /// <param name="mebibytes">The number of mebibytes.</param>
    /// <returns>The equivalent number of bytes.</returns>
    public static long Mebibytes(this int mebibytes) => mebibytes * BytesPerMebibyte;

    /// <summary>
    /// Converts a count of gibibytes (1024³ bytes each) to bytes.
    /// </summary>
    /// <param name="gibibytes">The number of gibibytes.</param>
    /// <returns>The equivalent number of bytes.</returns>
    public static long Gibibytes(this int gibibytes) => gibibytes * BytesPerGibibyte;

    /// <summary>
    /// Converts a fractional count of mebibytes (1024² bytes each) to whole bytes.
    /// </summary>
    /// <param name="mebibytes">The number of mebibytes.</param>
    /// <returns>The equivalent number of bytes, rounded to the nearest whole byte.</returns>
    /// <exception cref="OverflowException">The result does not fit in a <see cref="long"/>.</exception>
    public static long Mebibytes(this double mebibytes) =>
        Convert.ToInt64(mebibytes * BytesPerMebibyte);

    /// <summary>
    /// Converts a fractional count of gibibytes (1024³ bytes each) to whole bytes.
    /// </summary>
    /// <param name="gibibytes">The number of gibibytes.</param>
    /// <returns>The equivalent number of bytes, rounded to the nearest whole byte.</returns>
    /// <exception cref="OverflowException">The result does not fit in a <see cref="long"/>.</exception>
    public static long Gibibytes(this double gibibytes) =>
        Convert.ToInt64(gibibytes * BytesPerGibibyte);
}
