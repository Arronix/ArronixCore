#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended declarer.

using Arronix.Abstractions.DTOs;

namespace Arronix.Plugin.Movies.Definition;

/// <summary>
/// The complete movie quality ladder: every source × resolution × modifier rung Radarr ships, its
/// position, its semantic weight, its group and its plausible-size band — as declared data.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="QualityTier.Rank"/> is the rung's distinct position in the ladder, which the shape gate
/// requires to be unique; <see cref="QualityTier.Weight"/> is its <i>semantic</i> value, which two rungs
/// deliberately share. Radarr groups <c>WEBDL</c> and <c>WEBRip</c> of one resolution that way, and
/// collapsing the tie would make every re-encode an upgrade over the direct download it is a re-encode
/// of. Every comparison the host makes reads <see cref="QualityTier.EffectiveWeight"/>.
/// </para>
/// <para>
/// Size limits are stated in megabytes per minute of runtime, because a plausible size scales with
/// duration and with nothing else the platform reliably knows. A null ceiling means no ceiling: from
/// <c>Bluray-1080p</c> upwards a remux of a long film legitimately runs to tens of gigabytes, and a
/// capped limit would reject exactly the files the user asked for.
/// </para>
/// <para>
/// Nothing here is executable. The weight comparison, the cutoff rule, the revision fall-through and the
/// size gate are all host behavior over these numbers; what was 830 lines of per-kind quality model is
/// now thirty rows.
/// </para>
/// </remarks>
public static class MoviesLadder
{
    /// <summary>The rung a release lands on when nothing in its title identified one.</summary>
    /// <remarks>
    /// Not a member of <see cref="Tiers"/>. Weight one puts it below every real rung, so an unreadable
    /// release never blocks a readable one.
    /// </remarks>
    public static QualityTier Unknown { get; } =
        Tier("Unknown", 1, 1, "unknown", null, null, 0, 100, 95);

    /// <summary>Every rung, ascending. Declared order is the ladder.</summary>
    public static IReadOnlyList<QualityTier> Tiers { get; } =
    [
        // Pre-release. None of these carry a resolution: a cam is a cam whatever it was shot at, and
        // pretending otherwise would let a "1080p" cam outrank a 720p disc.
        Tier("WORKPRINT", 2, 2, "workprint", null, null, 0, 100, 95),
        Tier("CAM", 3, 3, "cam", null, null, 0, 100, 95),
        Tier("TELESYNC", 4, 4, "telesync", null, null, 0, 100, 95),
        Tier("TELECINE", 5, 5, "telecine", null, null, 0, 100, 95),
        Tier("REGIONAL", 6, 6, "dvd", "480p", null, 0, 100, 95),
        Tier("DVDSCR", 7, 7, "dvd", "480p", null, 0, 100, 95),

        // Standard definition.
        Tier("SDTV", 8, 8, "tv", "480p", null, 0, 100, 95),
        Tier("DVD", 9, 9, "dvd", null, null, 0, 100, 95),
        Tier("DVD-R", 10, 10, "dvd", "480p", null, 0, 100, 95),

        // 480p and 576p. The two WEB rungs share a weight and a group name.
        Tier("WEBRip-480p", 11, 11, "webrip", "480p", "WEB 480p", 0, 100, 95),
        Tier("WEBDL-480p", 12, 11, "webdl", "480p", "WEB 480p", 0, 100, 95),
        Tier("Bluray-480p", 13, 12, "bluray", "480p", null, 0, 100, 95),
        Tier("Bluray-576p", 14, 13, "bluray", "576p", null, 0, 100, 95),

        // 720p.
        Tier("HDTV-720p", 15, 14, "tv", "720p", null, 0, 100, 95),
        Tier("WEBRip-720p", 16, 15, "webrip", "720p", "WEB 720p", 0, 100, 95),
        Tier("WEBDL-720p", 17, 15, "webdl", "720p", "WEB 720p", 0, 100, 95),
        Tier("Bluray-720p", 18, 16, "bluray", "720p", null, 0, 100, 95),

        // 1080p and up: no ceiling.
        Tier("HDTV-1080p", 19, 17, "tv", "1080p", null, 0, 100, 95),
        Tier("WEBRip-1080p", 20, 18, "webrip", "1080p", "WEB 1080p", 0, 100, 95),
        Tier("WEBDL-1080p", 21, 18, "webdl", "1080p", "WEB 1080p", 0, 100, 95),
        Tier("Bluray-1080p", 22, 19, "bluray", "1080p", null, 0, null, null),
        Tier("Remux-1080p", 23, 20, "bluray", "1080p", null, 0, null, null),

        // 2160p.
        Tier("HDTV-2160p", 24, 21, "tv", "2160p", null, 0, null, null),
        Tier("WEBRip-2160p", 25, 22, "webrip", "2160p", "WEB 2160p", 0, null, null),
        Tier("WEBDL-2160p", 26, 22, "webdl", "2160p", "WEB 2160p", 0, null, null),
        Tier("Bluray-2160p", 27, 23, "bluray", "2160p", null, 0, null, null),
        Tier("Remux-2160p", 28, 24, "bluray", "2160p", null, 0, null, null),

        // Whole-disc and untouched-stream rungs. Both sit at the top of the ladder and both are, for most
        // users, unwanted — which is a profile decision and not a ranking one.
        Tier("BR-DISK", 29, 25, "bluray", "1080p", null, 0, null, null),
        Tier("Raw-HD", 30, 26, "tv", "1080p", null, 0, null, null)
    ];

    private static QualityTier Tier(
        string name,
        int rank,
        int weight,
        string source,
        string? resolution,
        string? group,
        double min,
        double? max,
        double? preferred) => new(
            name,
            rank,
            source,
            resolution,
            Weight: weight,
            GroupName: group,
            MinSizeMbPerMinute: min,
            MaxSizeMbPerMinute: max,
            PreferredSizeMbPerMinute: preferred);
}
