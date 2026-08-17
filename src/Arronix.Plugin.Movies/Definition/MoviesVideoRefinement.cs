#pragma warning disable ARX0021 // Quality contracts are experimental; a media extension is their intended implementer.

using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Quality.Families;

namespace Arronix.Plugin.Movies.Definition;

/// <summary>
/// What only a movie release says: the naming dialects the video family has no business knowing.
/// </summary>
/// <remarks>
/// <para>
/// The video family reads what every kind's releases say — source words, resolutions, codecs, containers,
/// language markers. This reads the residue: bracketed fan-subtitling conventions, a scene habit of
/// spelling a whole disc without ever naming one, and a legacy spelling for a widescreen broadcast that no
/// typed vocabulary has a form for. All three are conventions of how <i>this</i> community writes titles,
/// not facts about video, and putting them in a family every kind shares would hard-code a movie
/// identifier into a contract.
/// </para>
/// <para>
/// <b>Everything here contributes at the weakest provenance there is, which is what it deserves.</b> These
/// are guesses about naming habits. The host discards any contribution that would overwrite a stronger
/// reading, so a guess can complete what the family left absent and can never overrule a measurement — and
/// because a refusal acts only on a reading the release actually stated, a guess cannot refuse a release
/// either. That last point is not decoration: the whole-disc probe below fires on titles carrying no disc
/// token at all, and under a model where a guess could refuse, those releases were silently never offered.
/// </para>
/// <para>
/// <b>Four guard classes that used to live here have left, and each left upwards rather than sideways.</b>
/// A German dual-language disc encode is a remux — that is now a family rule over the typed language
/// claims. A broadcast capture carried in the codec broadcasters transmit in is the transport stream
/// itself — a family rule over the typed codec. A bare resolution inside a streaming container is a stream
/// download — a family rule over the typed container. And a raster written as pixel dimensions is a
/// resolution claim the host's own scanner now reads. Four of seven classes are gone from the per-kind
/// residue entirely, and this seam exists for the three that genuinely are dialect.
/// </para>
/// </remarks>
public sealed class MoviesVideoRefinement : IQualityRefinement<VideoQuality>
{
    /// <summary>The bracketed convention for a disc-sourced fan-subtitled release.</summary>
    public const string AnimeDiscGuard = "anime-bd";

    /// <summary>The bracketed convention for a stream-sourced fan-subtitled release.</summary>
    public const string AnimeStreamGuard = "anime-web";

    /// <summary>The scene's whole-disc naming habit.</summary>
    public const string WholeDiscGuard = "br-disk";

    /// <summary>The legacy scene spelling for a high-resolution widescreen broadcast capture.</summary>
    public const string HighResolutionWidescreenGuard = "hr-ws";

    /// <summary>The resolution words a bracketed title spells outside the shared scan's reach.</summary>
    private static readonly (string Guard, int Lines)[] SpelledResolutions =
    [
        ("text-2160p", 2160),
        ("text-1080p", 1080),
        ("text-720p", 720),
        ("text-480p", 480),
    ];

    /// <inheritdoc />
    public static FormatFamilyId Family => VideoQualityType.Family;

    /// <inheritdoc />
    public static VideoQuality Refine(VideoQuality read, ReleaseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(evidence);

        var guards = evidence.Guards;

        var origin = read.Origin;
        var generation = read.Generation;

        if (guards.Contains(AnimeDiscGuard))
        {
            // A bracketed disc release is a disc encode unless it also says it is a bitstream copy, which
            // the family has already read from the remux token and which this must not undo.
            origin = EvidenceMerge.Stronger(origin, Assumed(VideoOrigin.HighDefinitionDisc));
            generation = EvidenceMerge.Stronger(generation, Evidence<int>.From(1, EvidenceSource.Assumed));
        }
        else if (guards.Contains(AnimeStreamGuard))
        {
            origin = EvidenceMerge.Stronger(origin, Assumed(VideoOrigin.Stream));
            generation = EvidenceMerge.Stronger(generation, Evidence<int>.From(0, EvidenceSource.Assumed));
        }

        return new VideoQuality
        {
            Origin = origin,
            Generation = generation,
            Resolution = EvidenceMerge.Stronger(read.Resolution, SpelledResolution(guards, origin)),
            DynamicRange = read.DynamicRange,
            Audio = read.Audio,
            Codec = read.Codec,
            FrameRate = read.FrameRate,
            Packaging = EvidenceMerge.Stronger(read.Packaging, WholeDisc(guards)),
            Flaws = read.Flaws,
            Corrections = read.Corrections,
            Mislabels = read.Mislabels,
            Repacked = read.Repacked,
        };
    }

    /// <summary>Reads a resolution a bracketed title spelled somewhere the shared scan did not settle on.</summary>
    /// <remarks>
    /// <para>
    /// Richest first, so a title spelling two takes the higher one. That is the opposite of the
    /// lowest-claim rule the family applies within one source, and deliberately: these guards fire only
    /// when the shared scan read nothing at all, so there is no competing claim to be cautious against —
    /// and a bracketed title that spells two resolutions is almost always naming the one it carries and
    /// the one it was made from.
    /// </para>
    /// <para>
    /// <b>Silence a refinement is allowed to fill is not the same as silence it was handed on purpose.</b>
    /// The family drops a resolution a projection capture claims, because a camera states its own raster
    /// and not the work's — so a guard probing the text a second time must not put the claim back. This is
    /// the one place the strengthen-only rule is not enough on its own: turning absence into presence is
    /// safe in general and wrong here, and the refinement is the half that knows it is guessing.
    /// </para>
    /// </remarks>
    private static Evidence<int> SpelledResolution(
        IReadOnlySet<string> guards,
        Evidence<VideoOrigin> origin)
    {
        if (origin.Or((VideoOrigin)(-1)) is VideoOrigin.CameraCapture or VideoOrigin.Workprint)
        {
            return Evidence<int>.None;
        }

        if (guards.Contains(HighResolutionWidescreenGuard))
        {
            return Evidence<int>.From(720, EvidenceSource.Assumed);
        }

        foreach (var (guard, lines) in SpelledResolutions)
        {
            if (guards.Contains(guard))
            {
                return Evidence<int>.From(lines, EvidenceSource.Assumed);
            }
        }

        return Evidence<int>.None;
    }

    private static Evidence<Packaging> WholeDisc(IReadOnlySet<string> guards) =>
        guards.Contains(WholeDiscGuard)
            ? Evidence<Packaging>.From(Packaging.DiscImage, EvidenceSource.Assumed)
            : Evidence<Packaging>.None;

    private static Evidence<VideoOrigin> Assumed(VideoOrigin origin) =>
        Evidence<VideoOrigin>.From(origin, EvidenceSource.Assumed);
}
