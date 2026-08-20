using System.Text.RegularExpressions;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Parsing;

namespace Arronix.Plugin.Books;

/// <summary>
/// Reads a book release name into its parts.
/// </summary>
/// <remarks>
/// <para>
/// Three things here have no analog in the other kinds. The first is subtitle stripping: a work's
/// catalog title routinely carries a colon and a second clause that release names omit, so both forms
/// are produced and the matcher may try either. The second is that the flavor - written or spoken - is
/// frequently only recoverable from a format token, and it decides which ladder the copy is ranked on.
/// The third is that a release name may carry a public identifier outright, and a thirteen-digit number in
/// a book title is almost never a coincidence.
/// </para>
/// </remarks>
public sealed partial class BooksReleaseParser : IReleaseParser
{
    /// <summary>The parsed-metadata key carrying the credited writer.</summary>
    public const string CreatorKey = "creator";

    /// <summary>The parsed-metadata key carrying the title with its subtitle removed.</summary>
    public const string TitleWithoutSubtitleKey = "title-no-subtitle";

    /// <summary>The parsed-metadata key carrying the subtitle, where the name had one.</summary>
    public const string SubtitleKey = "subtitle";

    /// <summary>The parsed-metadata key carrying the format family the name implies.</summary>
    public const string FlavorKey = "flavor";

    /// <summary>The parsed-metadata key carrying a thirteen-digit book number read from the name.</summary>
    public const string BookNumberKey = "isbn13";

    /// <summary>The parsed-metadata key carrying the media-owned written or spoken family.</summary>
    public const string FormatFamilyKey = "books.format-family";

    /// <inheritdoc />
    public MediaKindId MediaKind => BooksShape.Kind;

    /// <inheritdoc />
    public bool CanParse(string releaseTitle) => Parse(releaseTitle) is not null;

    /// <inheritdoc />
    public ParsedRelease? Parse(string releaseTitle)
    {
        if (string.IsNullOrWhiteSpace(releaseTitle))
        {
            return null;
        }

        var working = releaseTitle.Trim();
        var bookNumber = ReadBookNumber(ref working);
        var group = ReadGroup(ref working);
        var year = ReadYear(ref working);
        var format = ReadFormat(ref working);

        var separated = SeparatorPattern().Match(working);

        if (!separated.Success)
        {
            return null;
        }

        var writer = Clean(separated.Groups["writer"].Value);
        var title = Clean(separated.Groups["title"].Value);

        if (writer.Length == 0 || title.Length == 0)
        {
            return null;
        }

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CreatorKey] = writer,
        };

        var colon = title.IndexOf(':', StringComparison.Ordinal);

        if (colon > 0)
        {
            extra[TitleWithoutSubtitleKey] = title[..colon].Trim();
            extra[SubtitleKey] = title[(colon + 1)..].Trim();
        }
        else
        {
            extra[TitleWithoutSubtitleKey] = title;
        }

        if (format.Flavor is not null)
        {
            extra[FlavorKey] = format.Flavor;
        }

        extra[FormatFamilyKey] = format.Flavor == BooksShape.SpokenFamilyId
            ? BooksShape.SpokenFamilyId
            : BooksShape.WrittenFamilyId;

        if (bookNumber is not null)
        {
            extra[BookNumberKey] = bookNumber;
        }

        return new ParsedRelease(
            MediaKind: BooksShape.Kind,
            Title: title,
            Year: year,
            Quality: format.QualityName,
            ReleaseGroup: group,
            AdditionalMetadata: extra);
    }

    private static string? ReadBookNumber(ref string working)
    {
        var match = BookNumberPattern().Match(working);

        if (!match.Success)
        {
            return null;
        }

        var digits = match.Groups["isbn"].Value;
        working = working.Remove(match.Index, match.Length);

        return digits;
    }

    private static string? ReadGroup(ref string working)
    {
        var match = GroupPattern().Match(working);

        if (!match.Success)
        {
            return null;
        }

        working = working.Remove(match.Index, match.Length);

        return match.Groups["group"].Value;
    }

    private static string? ReadYear(ref string working)
    {
        var match = YearPattern().Match(working);

        if (!match.Success)
        {
            return null;
        }

        working = working.Remove(match.Index, match.Length);

        return match.Groups["year"].Value;
    }

    private static FormatTokens ReadFormat(ref string working)
    {
        string? flavor = null;
        string? qualityName = null;

        foreach (Match match in FormatPattern().Matches(working))
        {
            var token = match.Value.Trim('[', ']', '(', ')', ' ', '-', '.', '_');

            switch (token.ToUpperInvariant())
            {
                case "EPUB":
                    flavor = BooksShape.WrittenFamilyId;
                    qualityName = "EPUB";
                    break;
                case "AZW3":
                case "AZW":
                    flavor = BooksShape.WrittenFamilyId;
                    qualityName = "AZW3";
                    break;
                case "MOBI":
                    flavor = BooksShape.WrittenFamilyId;
                    qualityName = "MOBI";
                    break;
                case "PDF":
                    flavor = BooksShape.WrittenFamilyId;
                    qualityName = "PDF";
                    break;
                case "RETAIL":
                case "EBOOK":
                    flavor ??= BooksShape.WrittenFamilyId;
                    break;
                case "M4B":
                case "AAX":
                    flavor = BooksShape.SpokenFamilyId;
                    qualityName = "M4B";
                    break;
                case "M4A":
                    flavor = BooksShape.SpokenFamilyId;
                    qualityName = "AAC";
                    break;
                case "MP3":
                    flavor = BooksShape.SpokenFamilyId;
                    qualityName = "MP3";
                    break;
                case "FLAC":
                    flavor = BooksShape.SpokenFamilyId;
                    qualityName = "FLAC";
                    break;
                case "AUDIOBOOK":
                case "UNABRIDGED":
                case "ABRIDGED":
                    flavor ??= BooksShape.SpokenFamilyId;
                    break;
                default:
                    break;
            }
        }

        working = FormatPattern().Replace(working, " ");

        return new FormatTokens(flavor, qualityName);
    }

    private static string Clean(string value)
    {
        var stripped = EmptyBracketPattern().Replace(value.Replace('_', ' '), " ");

        return WhitespacePattern().Replace(stripped, " ").Trim(' ', '-', '.');
    }

    private sealed record FormatTokens(string? Flavor, string? QualityName);

    [GeneratedRegex(@"\b(?<isbn>97[89]\d{10})\b", RegexOptions.None, 200)]
    private static partial Regex BookNumberPattern();

    [GeneratedRegex(@"[-_](?<group>[A-Za-z0-9]{2,})\s*$", RegexOptions.None, 200)]
    private static partial Regex GroupPattern();

    [GeneratedRegex(@"[\(\[\s\-_](?<year>(1[5-9]|20)\d{2})[\)\]\s\-_]?", RegexOptions.None, 200)]
    private static partial Regex YearPattern();

    [GeneratedRegex(
        @"\b(EPUB|AZW3|AZW|MOBI|PDF|RETAIL|EBOOK|M4B|AAX|M4A|MP3|FLAC|AUDIOBOOK|UNABRIDGED|ABRIDGED)\b",
        RegexOptions.IgnoreCase,
        200)]
    private static partial Regex FormatPattern();

    [GeneratedRegex(@"^(?<writer>.+?)\s*-\s*(?<title>.+)$", RegexOptions.None, 200)]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"\s{2,}", RegexOptions.None, 200)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"[\(\[]\s*[\)\]]", RegexOptions.None, 200)]
    private static partial Regex EmptyBracketPattern();
}
