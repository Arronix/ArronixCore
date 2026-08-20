
using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Tv;

/// <summary>
/// Resolves a parsed episodic release onto the declared quality ladder.
/// </summary>
/// <remarks>
/// The ladder differs from the film one in ways that are not cosmetic — a broadcast capture is a first-class
/// rung here and barely exists there, and <c>Raw-HD</c> has no film equivalent at all — which is why the
/// ladder is declared per format family on the shape rather than shared. The container is identical
/// machinery in every media kind; the rungs never are.
/// </remarks>
public sealed class TvQualityModel : IQualityModel
{
    private static readonly Dictionary<string, QualityTier> ByName =
        TvShape.Ladder.ToDictionary(tier => tier.Name, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public MediaKindId MediaKind => TvIds.MediaKind;

    /// <inheritdoc />
    public QualityTier EvaluateQuality(ParsedRelease parsedRelease)
    {
        ArgumentNullException.ThrowIfNull(parsedRelease);

        var resolution = parsedRelease.AdditionalMetadata is { } metadata
            && metadata.TryGetValue(TvReleaseFields.Resolution, out var value)
                ? value
                : null;
        var name = TierName(parsedRelease.Quality, resolution);
        var tier = name is not null && ByName.TryGetValue(name, out var resolved)
            ? resolved
            : TvShape.Unknown;

        // The parser reads the revision marker; before the tier carried a typed revision slot the marker
        // was dropped here, so a PROPER could never outrank the release it corrected. Now it rides the
        // tier.
        return ReadRevision(parsedRelease) is { } revision ? tier with { Revision = revision } : tier;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Effective weight first, so tiers in one quality group compare equal; the revision axis breaks the
    /// tie, so a re-issue upgrades the release it corrects without a rung between them.
    /// </remarks>
    public bool IsUpgrade(QualityTier currentQuality, QualityTier newQuality)
    {
        ArgumentNullException.ThrowIfNull(currentQuality);
        ArgumentNullException.ThrowIfNull(newQuality);

        var byWeight = newQuality.CompareTo(currentQuality);

        if (byWeight != 0)
        {
            return byWeight > 0;
        }

        var current = currentQuality.Revision ?? QualityRevision.Initial;
        var candidate = newQuality.Revision ?? QualityRevision.Initial;

        return candidate.CompareTo(current) > 0;
    }

    /// <inheritdoc />
    public bool MeetsCutoff(QualityTier quality, CutoffPolicy cutoff)
    {
        ArgumentNullException.ThrowIfNull(quality);
        ArgumentNullException.ThrowIfNull(cutoff);

        return cutoff.MeetsCutoff(quality);
    }

    private static QualityRevision? ReadRevision(ParsedRelease parsedRelease)
    {
        if (parsedRelease.AdditionalMetadata is not { } metadata
            || !metadata.TryGetValue(TvReleaseFields.Revision, out var marker))
        {
            return null;
        }

        return marker switch
        {
            "proper" => new QualityRevision(2, 0, false),
            "repack" => new QualityRevision(2, 0, true),
            _ => null
        };
    }

    private static string? TierName(string? source, string? resolution)
    {
        if (source is "dvd")
        {
            return "DVD";
        }

        if (source is "raw")
        {
            return "Raw-HD";
        }

        if (source is null)
        {
            return resolution is null ? null : $"WEBDL-{resolution}";
        }

        var normalizedResolution = resolution ?? DefaultResolutionFor(source);

        return source switch
        {
            "bluray-remux" => normalizedResolution is "2160p"
                ? "Bluray-2160p Remux"
                : "Bluray-1080p Remux",
            "bluray" => $"Bluray-{normalizedResolution}",
            "webdl" => $"WEBDL-{normalizedResolution}",
            "webrip" => $"WEBRip-{normalizedResolution}",
            "tv" => normalizedResolution is "480p" ? "SDTV" : $"HDTV-{normalizedResolution}",
            _ => null
        };
    }

    private static string DefaultResolutionFor(string source) => source switch
    {
        "bluray-remux" => "1080p",
        "bluray" => "1080p",
        _ => "720p"
    };
}
