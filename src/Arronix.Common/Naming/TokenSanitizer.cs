using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Arronix.Common.Naming;

/// <summary>
/// Turns arbitrary text into something a file system will actually accept as a name, and keeps the result
/// inside the length a file system will actually accept.
/// </summary>
/// <remarks>
/// <para>
/// This is the media-agnostic half of naming. It knows nothing about what the text means, which token it
/// came from or which template produced it — deciding which tokens exist and whether a template is valid
/// belongs to whoever owns the token vocabulary. What is here is the part that is the same for every
/// vocabulary: which characters a name may contain, how long it may be, which names the operating system
/// has already claimed, and what to do when two different inputs produce the same name.
/// </para>
/// <para>
/// <strong>The rules are the union across platforms, not the rules of the platform in use.</strong> A
/// library written on Linux is read over SMB from Windows, restored onto a NAS and synchronized to a Mac. A
/// sanitizer that applied only the current platform's rules would produce names that are perfectly legal
/// where they were created and unopenable everywhere else, and the failure would surface months later as a
/// file nobody can access. Applying every platform's restrictions everywhere costs a handful of characters
/// and removes that entire class of problem.
/// </para>
/// <para>
/// <strong>Lengths are measured in UTF-8 bytes.</strong> The common file systems cap a name at 255 — bytes
/// on ext4, APFS and most network file systems, UTF-16 code units on NTFS. A UTF-8 byte is never fewer than
/// the UTF-16 code units it encodes, so a name that fits a 255-byte budget fits all of them, while a name
/// measured in characters can be more than twice its budget in bytes and is rejected outright the first
/// time an accented title is written to a Linux volume.
/// </para>
/// </remarks>
public static class TokenSanitizer
{
    /// <summary>
    /// Stands in for a name that sanitized down to nothing, so a caller never receives an empty name it
    /// would then try to create a file from.
    /// </summary>
    public const string EmptyNamePlaceholder = "_";

    /// <summary>
    /// How many alternative names are tried before a collision is reported as unresolvable. The ceiling
    /// exists so that a predicate which is unconditionally true terminates rather than looping forever.
    /// </summary>
    private const int MaxCollisionAttempts = 1000;

    /// <summary>
    /// Characters no name may contain on at least one supported platform. The set is the union of the
    /// Windows reserved punctuation and the POSIX path separator; the ASCII control characters are excluded
    /// separately because they are a range rather than a list.
    /// </summary>
    private static readonly SearchValues<char> IllegalCharacters =
        SearchValues.Create("\"*/:<>?\\|");

    /// <summary>
    /// Characters trimmed from the end of a name. Windows silently strips both, so a name ending in either
    /// is not the name that comes back when the folder is listed.
    /// </summary>
    private static readonly char[] IllegalTrailingCharacters = [' ', '.'];

    /// <summary>
    /// Names the Windows device namespace has claimed. A file called any of these cannot be created, and a
    /// name whose part before the first dot is one of them is claimed too.
    /// </summary>
    private static readonly FrozenSet<string> ReservedNames = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Rewrites text so it is legal as a single file or folder name on every supported platform.
    /// </summary>
    /// <param name="value">The text to rewrite.</param>
    /// <returns>
    /// A legal name. Never empty, never whitespace, never a reserved device name, and never ending in a dot
    /// or a space.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Illegal characters are removed rather than substituted. Substituting a placeholder turns
    /// <c>"Title: Subtitle"</c> into a name carrying a character the text never had, and two different
    /// titles that differ only in punctuation still collide — which is what <see cref="MakeUnique"/> is
    /// for. Whitespace exposed by the removal is collapsed, so removing a character never leaves a double
    /// space behind.
    /// </remarks>
    public static string SanitizeComponent(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var stripped = StripIllegalCharacters(value);
        var collapsed = CollapseWhitespace(stripped);
        var trimmed = collapsed.TrimEnd(IllegalTrailingCharacters).TrimStart();

        if (trimmed.Length == 0)
        {
            return EmptyNamePlaceholder;
        }

        if (!IsReservedName(trimmed))
        {
            return trimmed;
        }

        // The placeholder goes on the end of the part before the first dot, not on the end of the name: the
        // device namespace claims "NUL.txt" exactly as it claims "NUL", so appending after the extension
        // would leave the name every bit as unusable as it was.
        var dot = trimmed.IndexOf('.');

        return dot < 0
            ? trimmed + EmptyNamePlaceholder
            : trimmed[..dot] + EmptyNamePlaceholder + trimmed[dot..];
    }

    /// <summary>
    /// Reports whether a name is one the Windows device namespace has claimed.
    /// </summary>
    /// <param name="component">A single file or folder name, without any directory part.</param>
    /// <returns><see langword="true"/> when the name cannot be created on Windows.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="component"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The check is made against the part before the first dot, because the device namespace claims
    /// <c>NUL.txt</c> exactly as it claims <c>NUL</c>.
    /// </remarks>
    public static bool IsReservedName(string component)
    {
        ArgumentNullException.ThrowIfNull(component);

        var dot = component.IndexOf('.');
        var stem = dot < 0 ? component : component[..dot];

        return ReservedNames.Contains(stem);
    }

    /// <summary>
    /// Shortens a name so its UTF-8 encoding fits a byte budget, keeping the extension.
    /// </summary>
    /// <param name="component">A single file or folder name, without any directory part.</param>
    /// <param name="maxLengthInBytes">
    /// The budget, in UTF-8 bytes. Pass the file system's own maximum name length rather than a constant.
    /// </param>
    /// <returns>The name, shortened only if it did not already fit.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="component"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxLengthInBytes"/> is not positive.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The extension is preserved wherever it can be, because it is what decides whether anything can open
    /// the file at all; only an extension that would leave no room for a name is dropped. The cut is made
    /// between grapheme clusters, so it never splits a surrogate pair into two unpaired halves and never
    /// separates a letter from the accent that belongs to it.
    /// </para>
    /// <para>
    /// Whatever trailing dot or space the cut exposes is trimmed, so shortening cannot produce a name that
    /// the previous rule would have rejected.
    /// </para>
    /// </remarks>
    public static string TruncateComponent(string component, int maxLengthInBytes)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLengthInBytes);

        if (Encoding.UTF8.GetByteCount(component) <= maxLengthInBytes)
        {
            return component;
        }

        var extension = Path.GetExtension(component);
        var extensionBytes = Encoding.UTF8.GetByteCount(extension);

        // An extension that would take more than half the budget is not an extension worth keeping: what is
        // left would be too short to identify the file.
        if (extensionBytes * 2 > maxLengthInBytes)
        {
            extension = string.Empty;
            extensionBytes = 0;
        }

        var stem = component[..(component.Length - extension.Length)];
        var shortened = TrimToUtf8Budget(stem, maxLengthInBytes - extensionBytes);

        if (shortened.Length == 0)
        {
            return TrimToUtf8Budget(component, maxLengthInBytes) is { Length: > 0 } fallback
                ? fallback
                : EmptyNamePlaceholder;
        }

        return shortened + extension;
    }

    /// <summary>
    /// Joins a folder and a file name, shortening the file name so that both it and the whole path stay
    /// within the file system's limits.
    /// </summary>
    /// <param name="folder">The folder the file goes in.</param>
    /// <param name="fileName">The file name, already sanitized.</param>
    /// <param name="maxPathLengthInBytes">
    /// The longest full path the file system accepts, in UTF-8 bytes.
    /// </param>
    /// <param name="maxFileNameLengthInBytes">
    /// The longest single name the file system accepts, in UTF-8 bytes.
    /// </param>
    /// <returns>The combined path, within both limits.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="folder"/> or <paramref name="fileName"/> is empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either limit is not positive.
    /// </exception>
    /// <exception cref="PathTooLongException">
    /// The folder alone leaves no room for a file name. Shortening the file name cannot fix that, so it is
    /// reported rather than papered over with a one-character name.
    /// </exception>
    /// <remarks>
    /// Only the file name is shortened. The folder is a location the caller chose and other files already
    /// live in it, so silently rewriting it would put this file somewhere else.
    /// </remarks>
    public static string CombineWithinLimits(
        string folder,
        string fileName,
        int maxPathLengthInBytes,
        int maxFileNameLengthInBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPathLengthInBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileNameLengthInBytes);

        // One byte for the separator the join introduces.
        var remaining = maxPathLengthInBytes - Encoding.UTF8.GetByteCount(folder) - 1;

        if (remaining <= 0)
        {
            throw new PathTooLongException(
                $"The folder '{folder}' is already at the path length limit of {maxPathLengthInBytes} bytes, leaving no room for a file name.");
        }

        var budget = Math.Min(remaining, maxFileNameLengthInBytes);

        return Path.Combine(folder, TruncateComponent(fileName, budget));
    }

    /// <summary>
    /// Produces a name that is not already taken, by numbering it.
    /// </summary>
    /// <param name="fileName">The preferred name, already sanitized.</param>
    /// <param name="isTaken">
    /// Answers whether a candidate name is already in use. It is called once with the preferred name and
    /// once per alternative.
    /// </param>
    /// <param name="maxLengthInBytes">
    /// The longest name the file system accepts, in UTF-8 bytes. The number is fitted inside this budget
    /// rather than appended past it.
    /// </param>
    /// <returns>The preferred name, or the first numbered variant of it that is free.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="fileName"/> is empty or whitespace.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="isTaken"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxLengthInBytes"/> is not positive.
    /// </exception>
    /// <exception cref="IOException">
    /// Every candidate up to the attempt ceiling was taken.
    /// </exception>
    /// <remarks>
    /// The number goes before the extension, not after it, so the alternative is still recognizably the
    /// same kind of file. Numbering starts at two because the unnumbered name is the first one.
    /// </remarks>
    public static string MakeUnique(string fileName, Func<string, bool> isTaken, int maxLengthInBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(isTaken);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLengthInBytes);

        if (!isTaken(fileName))
        {
            return fileName;
        }

        var extension = Path.GetExtension(fileName);
        var stem = fileName[..(fileName.Length - extension.Length)];

        for (var ordinal = 2; ordinal <= MaxCollisionAttempts; ordinal++)
        {
            var suffix = string.Create(CultureInfo.InvariantCulture, $" ({ordinal})");
            var budget = maxLengthInBytes
                - Encoding.UTF8.GetByteCount(suffix)
                - Encoding.UTF8.GetByteCount(extension);

            if (budget <= 0)
            {
                break;
            }

            var candidate = TrimToUtf8Budget(stem, budget) + suffix + extension;

            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        throw new IOException(
            $"No free name could be derived from '{fileName}' within {MaxCollisionAttempts} attempts.");
    }

    private static string StripIllegalCharacters(string value)
    {
        if (value.AsSpan().IndexOfAny(IllegalCharacters) < 0 && !value.Any(char.IsControl))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (char.IsControl(character) || IllegalCharacters.Contains(character))
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var character in value)
        {
            var isSpace = char.IsWhiteSpace(character);

            if (isSpace && previousWasSpace)
            {
                continue;
            }

            builder.Append(isSpace ? ' ' : character);
            previousWasSpace = isSpace;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Cuts text to fit a UTF-8 byte budget on a grapheme-cluster boundary, then trims whatever trailing
    /// dot or space the cut exposed.
    /// </summary>
    private static string TrimToUtf8Budget(string value, int budget)
    {
        if (budget <= 0)
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(value) <= budget)
        {
            return value.TrimEnd(IllegalTrailingCharacters);
        }

        var used = 0;
        var length = 0;
        var elements = StringInfo.GetTextElementEnumerator(value);

        while (elements.MoveNext())
        {
            var element = (string)elements.Current;
            var size = Encoding.UTF8.GetByteCount(element);

            if (used + size > budget)
            {
                break;
            }

            used += size;
            length += element.Length;
        }

        return value[..length].TrimEnd(IllegalTrailingCharacters);
    }
}
