using System.Globalization;
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Quality;

namespace Arronix.Plugin.Music;

/// <summary>
/// Ranks copies of a recording against one another.
/// </summary>
/// <remarks>
/// <para>
/// The ladder is not defined here: it is declared once, on the shape's format family, and read back. That
/// is deliberate. A ladder that exists in two places is a ladder that will disagree with itself, and the
/// shape's copy is the one the platform publishes, stores against and renders from.
/// </para>
/// <para>
/// Music has one family, so every copy is comparable with every other and a single ceiling is meaningful.
/// A kind with two families - text and spoken, say - cannot say that, and the difference is the reason
/// families exist.
/// </para>
/// </remarks>
public sealed class MusicQualityModel : IQualityModel
{
    /// <inheritdoc />
    public MediaKindId MediaKind => MusicShape.Kind;

    /// <summary>Gets the declared ladder, best last.</summary>
    public static IReadOnlyList<QualityTier> Ladder { get; } =
        MusicShape.Declaration.FormatFamilies[0].Ladder;

    /// <summary>Gets the tier meaning "recognized as audio, but not placed on the ladder".</summary>
    /// <remarks>
    /// Present because this family still declares a ladder. A family that reads its files onto typed axes
    /// declares no sentinel at all, because an axis reading carries its own typed absence.
    /// </remarks>
    public static QualityTier Unknown { get; } = MusicShape.Declaration.FormatFamilies[0].Unknown!;

    /// <inheritdoc />
    public QualityTier EvaluateQuality(ParsedRelease parsedRelease)
    {
        ArgumentNullException.ThrowIfNull(parsedRelease);

        var metadata = parsedRelease.AdditionalMetadata;
        var codec = metadata is not null && metadata.TryGetValue(MusicReleaseParser.CodecKey, out var value)
            ? value
            : null;

        var depth = ReadInt(metadata, MusicReleaseParser.BitDepthKey);
        var bitrate = ReadInt(metadata, MusicReleaseParser.BitrateKey);
        var variable = metadata is not null
            && metadata.ContainsKey(MusicReleaseParser.VariableBitrateKey);

        if (string.Equals(codec, "FLAC", StringComparison.Ordinal))
        {
            return depth >= 24 ? Named("FLAC 24bit") : Named("FLAC");
        }

        if (string.Equals(codec, "ALAC", StringComparison.Ordinal))
        {
            return Named("ALAC");
        }

        if (string.Equals(codec, "PCM", StringComparison.Ordinal))
        {
            return Named("WAV");
        }

        if (string.Equals(codec, "AAC", StringComparison.Ordinal))
        {
            return bitrate >= 300 ? Named("AAC-320") : Named("AAC-256");
        }

        if (string.Equals(codec, "MP3", StringComparison.Ordinal))
        {
            if (variable)
            {
                return Named("MP3-VBR");
            }

            return bitrate switch
            {
                >= 320 => Named("MP3-320"),
                >= 256 => Named("MP3-256"),
                >= 192 => Named("MP3-192"),
                >= 1 => Named("MP3-128"),
                _ => Unknown,
            };
        }

        return Unknown;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Compares effective weight, not rank: the ladder declares no quality groups today, so the answers
    /// are unchanged, but a grouped ladder would make a rank comparison domain-wrong and the typed slot
    /// exists to prevent exactly that.
    /// </remarks>
    public bool IsUpgrade(QualityTier currentQuality, QualityTier newQuality)
    {
        ArgumentNullException.ThrowIfNull(currentQuality);
        ArgumentNullException.ThrowIfNull(newQuality);

        return newQuality.CompareTo(currentQuality) > 0;
    }

    /// <inheritdoc />
    public bool MeetsCutoff(QualityTier quality, CutoffPolicy cutoff)
    {
        ArgumentNullException.ThrowIfNull(quality);
        ArgumentNullException.ThrowIfNull(cutoff);

        return cutoff.MeetsCutoff(quality);
    }

    /// <summary>
    /// Returns the tier a file's extension alone implies, before any tag or release name is read.
    /// </summary>
    /// <param name="path">The file's path or name.</param>
    /// <returns>The implied tier, or the unknown tier.</returns>
    /// <remarks>
    /// The extension set is the shape's declared discriminator for a family, which makes this a lookup
    /// rather than a guess. It is intentionally coarse: a lossless container says nothing about bit depth,
    /// so it lands on the lower of the two lossless tiers until a tag says otherwise.
    /// </remarks>
    public static QualityTier TierForExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Unknown;
        }

        var dot = path.LastIndexOf('.');
        if (dot < 0 || dot == path.Length - 1)
        {
            return Unknown;
        }

        return path[dot..].ToUpperInvariant() switch
        {
            ".FLAC" => Named("FLAC"),
            ".ALAC" => Named("ALAC"),
            ".WAV" => Named("WAV"),
            ".M4A" or ".AAC" => Named("AAC-256"),
            ".OGG" or ".OPUS" => Named("Vorbis"),
            ".MP3" => Named("MP3-320"),
            _ => Unknown,
        };
    }

    private static QualityTier Named(string name) =>
        Ladder.First(tier => string.Equals(tier.Name, name, StringComparison.Ordinal));

    private static int ReadInt(IReadOnlyDictionary<string, string>? metadata, string key) =>
        metadata is not null
            && metadata.TryGetValue(key, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
}
