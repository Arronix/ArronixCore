using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Arronix.Abstractions.Quality.Families;

/// <summary>
/// What a video file at one point should weigh, computed rather than tabulated.
/// </summary>
/// <remarks>
/// <para>
/// A file's size is its bitrate times its duration. A video stream's bitrate is its pixel rate times what
/// each pixel costs, and what a pixel costs depends on the codec and on how near-transparent the encode is
/// aiming to be — which is what the origin and the generation already say. So the whole model is six codec
/// rows, seven master rows and six audio rows, and it produces an answer for combinations no hand-written
/// per-rung table has a row for: ask it for an intermediate raster at sixty frames in a codec nobody
/// tabulated and it answers.
/// </para>
/// <para>
/// <b>Where the numbers come from</b>, so the difference from a table of magic numbers is demonstrable
/// rather than asserted. The reference point is H.264 at 0.100 bits per pixel, which is 1920x1080 at 24
/// frames costing about 5 Mbit/s — the figure published encoding ladders and published upload
/// recommendations independently agree on. The other codecs are that reference scaled by their published
/// efficiency ratios. The master factors come from the published disc and broadcast specifications: a
/// 40 Mbit/s Blu-ray video ceiling and 100 Mbit/s for its ultra-high-definition successor, 9.8 Mbit/s total
/// for DVD-Video, 19.39 Mbit/s of transport for terrestrial broadcast. The audio allowances are the
/// published rate ranges for the surround and object formats.
/// </para>
/// <para>
/// <b>The band is wide on purpose.</b> A legitimate encode of one point varies by roughly threefold between
/// a size-conscious modern encode and a high-bitrate older one. The gate's job is to catch a two-hundred
/// megabyte "disc bitstream" and a sixty gigabyte "480 lines", not to police taste — and where an input is
/// missing and no defensible center exists, it says so rather than returning a band so wide it asserts
/// nothing.
/// </para>
/// <para>
/// The ranks it reads are the declared numeric values of the family's own axis enumerations, which is what
/// a reading carries. Until the standard video family lands in the contract assembly with its own model,
/// this is where that model lives; when it does, the tables move with it and nothing else changes, because
/// nothing else knows them.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class VideoSizeModel
{
    private const double ReferenceBitsPerPixel = 0.100;
    private const double AssumedFrameRate = 24d;
    private const int AssumedAudioKilobits = 640;
    private const double FloorShare = 0.35;
    private const double CeilingShare = 3.0;

    /// <summary>Computes what a file at one point should weigh.</summary>
    /// <param name="lines">The vertical resolution.</param>
    /// <param name="frameRate">The frame rate.</param>
    /// <param name="codecRank">The codec's declared rank.</param>
    /// <param name="originRank">The origin's declared rank.</param>
    /// <param name="generation">How many lossy re-encodes since that origin.</param>
    /// <param name="audioRank">The audio presentation's declared rank.</param>
    /// <param name="duration">The item's duration.</param>
    /// <returns>The expectation, or an unassessable one.</returns>
    /// <remarks>
    /// Three inputs have no defensible substitute and each returns an unassessable expectation rather than a
    /// guess. Without a resolution the pixel rate spans twentyfold, which is wider than the plausibility
    /// band itself. Without either an origin or a generation the master factor spans tenfold, likewise.
    /// Without a duration there is no second term at all, because size is bitrate times duration and nothing
    /// else. Everything else has a defensible center and widens the band instead: an absent frame rate is
    /// read as twenty-four with a taller ceiling, an absent codec as the reference codec with a band opened
    /// in both directions — further downward, because assuming the reference codec for what is really a
    /// modern one over-predicts and would reject a perfectly good small file.
    /// </remarks>
    public static SizeExpectation Expect(
        int? lines,
        double? frameRate,
        int? codecRank,
        int? originRank,
        int? generation,
        int? audioRank,
        TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || lines is not { } height || height <= 0)
        {
            return SizeExpectation.NotAssessable;
        }

        if (originRank is null && generation is null)
        {
            return SizeExpectation.NotAssessable;
        }

        var width = WidthFor(height);
        var rate = frameRate ?? AssumedFrameRate;
        var bitsPerPixel = codecRank is { } codec ? BitsPerPixel(codec) : ReferenceBitsPerPixel;
        var master = MasterFactor(originRank, generation);
        var audio = audioRank is { } presentation ? AudioKilobits(presentation) : AssumedAudioKilobits;

        var videoBitsPerSecond = width * (double)height * rate * bitsPerPixel * master.Factor;
        var totalBitsPerSecond = videoBitsPerSecond + (audio * 1000d);
        var expected = totalBitsPerSecond * duration.TotalSeconds / 8d;

        var floor = expected * FloorShare;
        var ceiling = expected * CeilingShare;

        if (frameRate is null)
        {
            ceiling *= 2.5;
        }

        if (codecRank is null)
        {
            floor *= 0.5;
            ceiling *= 1.5;
        }

        if (master.Assumed)
        {
            ceiling *= 1.5;
        }

        return new SizeExpectation(
            (long)expected,
            (long)floor,
            (long)ceiling,
            Basis(width, height, rate, bitsPerPixel, master, audio, duration, frameRate is null, codecRank is null));
    }

    /// <summary>The raster a stated line count implies.</summary>
    /// <param name="lines">The vertical resolution.</param>
    /// <returns>The width in pixels.</returns>
    /// <remarks>
    /// Sixteen by nine rounded to an even number, except at the two standard-definition line counts, where
    /// the disc and broadcast rasters are 720 wide whatever their display aspect says.
    /// </remarks>
    public static int WidthFor(int lines) =>
        lines is 480 or 576 ? 720 : Even((int)Math.Round(lines * 16d / 9d, MidpointRounding.AwayFromZero));

    private static int Even(int width) => width % 2 == 0 ? width : width + 1;

    private static double BitsPerPixel(int codecRank) =>
        codecRank switch
        {
            VideoCodecRank.Mpeg2 => 0.200,
            VideoCodecRank.Mpeg4Part2 => 0.150,
            VideoCodecRank.Vc1 => 0.120,
            VideoCodecRank.H264 => ReferenceBitsPerPixel,
            VideoCodecRank.Vp9 or VideoCodecRank.H265 => 0.055,
            VideoCodecRank.Av1 => 0.040,
            VideoCodecRank.H266 => 0.036,
            _ => ReferenceBitsPerPixel,
        };

    private static int AudioKilobits(int audioRank) =>
        audioRank switch
        {
            AudioPresentationRank.RoomCapture => 128,
            AudioPresentationRank.LossyStereo => 192,
            AudioPresentationRank.LossySurround => AssumedAudioKilobits,
            AudioPresentationRank.LossyObject => 768,
            AudioPresentationRank.Lossless => 4500,
            AudioPresentationRank.LosslessObject => 6000,
            _ => AssumedAudioKilobits,
        };

    private static MasterFactorReading MasterFactor(int? originRank, int? generation)
    {
        if (originRank is not { } origin)
        {
            // Nothing says which master, but something says how far from it. A rip targets a size below its
            // source by definition, and a rip of a rip targets below that again, so the generation alone
            // still carries a defensible center — with the ceiling opened, because the master it descends
            // from is a guess.
            return new MasterFactorReading(RipFactor(generation ?? 1), true);
        }

        return origin switch
        {
            VideoOriginRank.HighDefinitionDiscBitstream => new MasterFactorReading(5.0, false),
            VideoOriginRank.StandardDefinitionDiscBitstream => new MasterFactorReading(2.5, false),
            VideoOriginRank.BroadcastBitstream => new MasterFactorReading(1.5, false),

            VideoOriginRank.CameraCapture or VideoOriginRank.Workprint or VideoOriginRank.FilmPrint =>
                new MasterFactorReading(0.5, false),

            // A streaming service is the reference point the bits-per-pixel column is calibrated on, which
            // is exactly why it has no separate bitstream member: holding its transmission and holding an
            // encode of it are the same bitrate class.
            VideoOriginRank.Stream => new MasterFactorReading(
                generation is null or 0 ? 1.0 : RipFactor(generation.Value), false),

            _ => new MasterFactorReading(RipFactor(generation ?? 1), false),
        };
    }

    private static double RipFactor(int generation) => generation >= 2 ? 0.7 : 0.9;

    private static string Basis(
        int width,
        int height,
        double frameRate,
        double bitsPerPixel,
        MasterFactorReading master,
        int audioKilobits,
        TimeSpan duration,
        bool frameRateAssumed,
        bool codecAssumed)
    {
        var basis = new StringBuilder();

        basis.Append(CultureInfo.InvariantCulture, $"{width}x{height}");
        basis.Append(CultureInfo.InvariantCulture, $" at {frameRate:0.##} fps");
        basis.Append(CultureInfo.InvariantCulture, $", {bitsPerPixel:0.000} bits per pixel");
        basis.Append(CultureInfo.InvariantCulture, $", master factor {master.Factor:0.0}");
        basis.Append(CultureInfo.InvariantCulture, $", {audioKilobits} kbit/s of audio");
        basis.Append(CultureInfo.InvariantCulture, $", over {duration:g}");

        if (frameRateAssumed || codecAssumed || master.Assumed)
        {
            basis.Append(" (assumed:");

            if (frameRateAssumed)
            {
                basis.Append(" frame rate");
            }

            if (codecAssumed)
            {
                basis.Append(frameRateAssumed ? ", codec" : " codec");
            }

            if (master.Assumed)
            {
                basis.Append(frameRateAssumed || codecAssumed ? ", master" : " master");
            }

            basis.Append(')');
        }

        return basis.ToString();
    }

    /// <summary>One master factor, and whether it was read or assumed.</summary>
    /// <param name="Factor">The factor.</param>
    /// <param name="Assumed">Whether the master it belongs to was stated.</param>
    private readonly record struct MasterFactorReading(double Factor, bool Assumed);
}

/// <summary>
/// The declared ranks of the standard video family's origin members.
/// </summary>
/// <remarks>
/// A member exists for an untouched master exactly where holding the master is a different bitrate class
/// from holding an encode of it. The three disc and broadcast masters are 5.6, 2.8 and 1.7 times their own
/// rip factor and get one; a streaming service is 1.11 times its own and does not, which is precisely the
/// equivalence the community asserts between a service's transmission and a re-encode of it.
/// </remarks>
internal static class VideoOriginRank
{
    /// <summary>A projection re-photographed with a camera.</summary>
    internal const int CameraCapture = 0;

    /// <summary>An unfinished edit.</summary>
    internal const int Workprint = 1;

    /// <summary>A physical film print, scanned.</summary>
    internal const int FilmPrint = 2;

    /// <summary>A transmission, re-encoded.</summary>
    internal const int Broadcast = 3;

    /// <summary>The transport stream itself, untouched.</summary>
    internal const int BroadcastBitstream = 4;

    /// <summary>A commercial streaming service's transmission.</summary>
    internal const int Stream = 5;

    /// <summary>A standard-definition disc, re-encoded.</summary>
    internal const int StandardDefinitionDisc = 6;

    /// <summary>The standard-definition disc's own program stream, untouched.</summary>
    internal const int StandardDefinitionDiscBitstream = 7;

    /// <summary>A high-definition disc, re-encoded.</summary>
    internal const int HighDefinitionDisc = 8;

    /// <summary>The high-definition disc's own video bitstream, copied.</summary>
    internal const int HighDefinitionDiscBitstream = 9;
}

/// <summary>The declared ranks of the standard video family's codec members, ascending by efficiency.</summary>
/// <remarks>
/// Efficiency is not fidelity: at an equal quality target a more efficient codec produces a smaller file of
/// the same quality, not a better one. That is exactly why the codec belongs to the size model and not to
/// anybody's default ordering.
/// </remarks>
internal static class VideoCodecRank
{
    /// <summary>The broadcast and standard-definition disc codec.</summary>
    internal const int Mpeg2 = 0;

    /// <summary>The standard-definition era's re-encode codec.</summary>
    internal const int Mpeg4Part2 = 1;

    /// <summary>The early high-definition disc codec.</summary>
    internal const int Vc1 = 2;

    /// <summary>The reference codec the bits-per-pixel column is calibrated on.</summary>
    internal const int H264 = 3;

    /// <summary>The open codec of the same generation as its successor.</summary>
    internal const int Vp9 = 4;

    /// <summary>The successor at about half the bitrate for equal quality.</summary>
    internal const int H265 = 5;

    /// <summary>The open codec at about seventy percent of that again.</summary>
    internal const int Av1 = 6;

    /// <summary>The newest standardized codec.</summary>
    internal const int H266 = 7;
}

/// <summary>The declared ranks of the standard video family's audio presentations.</summary>
internal static class AudioPresentationRank
{
    /// <summary>Sound recorded in the room.</summary>
    internal const int RoomCapture = 0;

    /// <summary>A lossy two-channel mix.</summary>
    internal const int LossyStereo = 1;

    /// <summary>A lossy surround mix.</summary>
    internal const int LossySurround = 2;

    /// <summary>A lossy mix carrying positioned objects.</summary>
    internal const int LossyObject = 3;

    /// <summary>A lossless surround mix.</summary>
    internal const int Lossless = 4;

    /// <summary>A lossless mix carrying positioned objects.</summary>
    internal const int LosslessObject = 5;
}
