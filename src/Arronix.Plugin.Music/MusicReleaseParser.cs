using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Parsing;

namespace Arronix.Plugin.Music;

/// <summary>
/// Reads a music release name into its parts.
/// </summary>
/// <remarks>
/// <para>
/// The conventional forms are <c>Performer - Work (Year) [Codec-Bitrate]</c> and
/// <c>Performer-Work-Year-Codec-Group</c>. Neither is a standard and both are widely violated, so the
/// parser is deliberately tolerant: it extracts what it can recognize and leaves the rest as free text
/// for the matcher to argue with.
/// </para>
/// <para>
/// Note what it does <em>not</em> do: decide which items the release satisfies. That is the matcher's
/// job, and separating the two is what lets the same parsed value be reused against a file name, a folder
/// name and a download client's title without re-parsing.
/// </para>
/// </remarks>
public sealed partial class MusicReleaseParser : IReleaseParser
{
    /// <inheritdoc />
    public MediaKindId MediaKind => MusicShape.Kind;

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
        var group = ReadGroup(ref working);
        var year = ReadYear(ref working);
        var tokens = ReadFormatTokens(ref working);

        var separated = SeparatorPattern().Match(working);
        if (!separated.Success)
        {
            return null;
        }

        var performer = Clean(separated.Groups["performer"].Value);
        var work = Clean(separated.Groups["work"].Value);

        if (performer.Length == 0 || work.Length == 0)
        {
            return null;
        }

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CreatorKey] = performer,
        };

        if (tokens.Bitrate is { } bitrate)
        {
            extra[BitrateKey] = bitrate.ToString(CultureInfo.InvariantCulture);
        }

        if (tokens.IsVariableBitrate)
        {
            extra[VariableBitrateKey] = "true";
        }

        if (tokens.BitDepth is { } depth)
        {
            extra[BitDepthKey] = depth.ToString(CultureInfo.InvariantCulture);
        }

        return new ParsedRelease(
            MusicShape.Kind,
            work,
            year,
            tokens.QualityName,
            tokens.Codec,
            tokens.Codec,
            null,
            group,
            null,
            extra);
    }

    /// <summary>The parsed-metadata key carrying the credited performer.</summary>
    public const string CreatorKey = "creator";

    /// <summary>The parsed-metadata key carrying a bitrate in kilobits per second.</summary>
    public const string BitrateKey = "bitrate";

    /// <summary>The parsed-metadata key set when the copy is variable-bitrate.</summary>
    public const string VariableBitrateKey = "variable-bitrate";

    /// <summary>The parsed-metadata key carrying a sample bit depth.</summary>
    public const string BitDepthKey = "bit-depth";

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

    private static FormatTokens ReadFormatTokens(ref string working)
    {
        string? codec = null;
        string? qualityName = null;
        int? bitrate = null;
        int? bitDepth = null;
        var variable = false;

        foreach (Match match in FormatPattern().Matches(working))
        {
            var token = match.Value.Trim('[', ']', '(', ')', ' ', '-', '.', '_');
            var upper = token.ToUpperInvariant();

            if (upper.StartsWith("MP3", StringComparison.Ordinal))
            {
                codec = "MP3";
                qualityName = token;
            }
            else if (upper.StartsWith("AAC", StringComparison.Ordinal)
                || upper.StartsWith("M4A", StringComparison.Ordinal))
            {
                codec = "AAC";
                qualityName = token;
            }
            else if (upper.StartsWith("FLAC", StringComparison.Ordinal))
            {
                codec = "FLAC";
                qualityName = token;
            }
            else if (upper.StartsWith("ALAC", StringComparison.Ordinal))
            {
                codec = "ALAC";
                qualityName = token;
            }
            else if (upper.StartsWith("WAV", StringComparison.Ordinal))
            {
                codec = "PCM";
                qualityName = token;
            }
            else if (upper is "V0" or "V2" or "VBR" or "V0 (VBR)")
            {
                variable = true;
                qualityName ??= token;
            }
            else if (upper.EndsWith("KBPS", StringComparison.Ordinal)
                && int.TryParse(
                    upper[..^4],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedRate))
            {
                bitrate = parsedRate;
            }
            else if (upper is "24BIT" or "24-BIT")
            {
                bitDepth = 24;
            }
        }

        working = FormatPattern().Replace(working, " ");

        if (bitrate is null && qualityName is not null)
        {
            var digits = new string(qualityName.Where(char.IsDigit).ToArray());
            if (digits.Length >= 3
                && int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var inline))
            {
                bitrate = inline;
            }
        }

        return new FormatTokens(codec, qualityName, bitrate, bitDepth, variable);
    }

    private static string Clean(string value)
    {
        var stripped = EmptyBracketPattern().Replace(value.Replace('_', ' ').Replace('.', ' '), " ");

        return WhitespacePattern().Replace(stripped, " ").Trim(' ', '-');
    }

    private sealed record FormatTokens(
        string? Codec,
        string? QualityName,
        int? Bitrate,
        int? BitDepth,
        bool IsVariableBitrate);

    [GeneratedRegex(@"[-_](?<group>[A-Za-z0-9]{2,})\s*$", RegexOptions.None, 200)]
    private static partial Regex GroupPattern();

    [GeneratedRegex(@"[\(\[\s\-_](?<year>(19|20)\d{2})[\)\]\s\-_]?", RegexOptions.None, 200)]
    private static partial Regex YearPattern();

    [GeneratedRegex(
        @"\b(MP3(-?\d{2,3})?|AAC(-?\d{2,3})?|M4A|FLAC|ALAC|WAV|V0|V2|VBR|\d{2,3}kbps|24bit|24-bit)\b",
        RegexOptions.IgnoreCase,
        200)]
    private static partial Regex FormatPattern();

    [GeneratedRegex(@"^(?<performer>.+?)\s*-\s*(?<work>.+)$", RegexOptions.None, 200)]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"\s{2,}", RegexOptions.None, 200)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"[\(\[]\s*[\)\]]", RegexOptions.None, 200)]
    private static partial Regex EmptyBracketPattern();
}
