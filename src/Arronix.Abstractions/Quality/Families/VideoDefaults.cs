using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Arronix.Abstractions.Quality.Families;

/// <summary>The opinion the video family ships, which a user's own profile replaces entirely.</summary>
/// <remarks>
/// <para>
/// Every line here is a claim this platform is making, not data copied from somewhere. It is a
/// <i>default</i>: a profile that replaces it owes nothing to it, and a user who disagrees with any one
/// line moves one chip rather than accepting a bundle.
/// </para>
/// <para>
/// <b>Resolution first</b>, because it is the one dimension a viewer notices from across the room and the
/// one nearly every release states — capped at 2160 lines, because that is the highest raster consumer
/// displays ship in volume and an uncapped axis makes a larger remux an upgrade nobody can play.
/// </para>
/// <para>
/// <b>Origin second</b>, because that is where the master-against-encode cliff lives. It is what makes a
/// disc bitstream an upgrade over a disc encode while leaving a service's transmission and a re-encode of
/// it equal, and it restores three orderings a single ordered axis loses: an untouched transport stream
/// above a broadcast capture, a disc's own program stream above a rip of it, and a disc rip above a
/// broadcast capture at the same raster.
/// </para>
/// <para>
/// <b>Generation third, tied at one re-encode</b>, which is that equality stated as data rather than as an
/// omission. Dropping the axis instead would also tie a disc encode with a disc bitstream, which is the
/// trap: one axis cannot hold a cliff for discs and an equivalence for streams, so the cliff moved and the
/// ceiling stayed. A user who wants a direct download ranked above a re-encode removes the ceiling — one
/// chip, and the chip does exactly one thing.
/// </para>
/// <para>
/// <b>Mislabel fixes above corrections.</b> A mislabel fix says the previous file was the <i>wrong
/// content</i>; a correction says it was a <i>worse encode of the right content</i>. Wrong content
/// dominates.
/// </para>
/// <para>
/// <b>Dynamic range is a bonus, not an order.</b> It is set-valued and therefore unordered, so it cannot
/// appear in a precedence list at all; scoring the three dynamic-metadata formats equally means the
/// platform does not chase a re-download for a difference a given display may not render, and it stops the
/// axis from outranking a correction.
/// </para>
/// <para>
/// <b>Codec, audio, frame rate and repack are absent from the order, deliberately.</b> Ordering on codec
/// makes a library chase re-encodes, which is a downgrade wearing an upgrade's clothes; audio matters
/// enormously to some listeners and not at all through a television's speakers; a repack is the same
/// encode packaged again and is not a fidelity change at all. Each is one chip away and none ships with
/// points.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class VideoDefaults
{
    /// <summary>Declares the shipped policy on a builder.</summary>
    /// <param name="policy">The builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    public static void Shipped(IQualityPolicyBuilder policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy

            // The core: strictly lexicographic, total, and therefore incapable of cycling.
            .Prefer(Axis(nameof(VideoQuality.Resolution)))
                .UpTo(AxisValue.Quantity(2160))
                .WhenUnknownRanksLowest()
            .Prefer(Axis(nameof(VideoQuality.Origin)))
                .WhenUnknownRanksLowest()
            .Prefer(Axis(nameof(VideoQuality.Generation)))
                .UpTo(AxisValue.Quantity(1))
                .WhenUnknownAssume(AxisValue.Quantity(1))
            .Prefer(Axis(nameof(VideoQuality.Mislabels)))
                .WhenUnknownAssume(AxisValue.Quantity(0))
            .Prefer(Axis(nameof(VideoQuality.Corrections)))
                .WhenUnknownAssume(AxisValue.Quantity(0))

            // The bonus tier, consulted only when everything above returns the same answer.
            .Facet(Axis(nameof(VideoQuality.DynamicRange)))
                .Worth(Member(DynamicRange.HybridLogGamma), 5)
                .Worth(Member(DynamicRange.HighDynamicRange10), 10)
                .Worth(Member(DynamicRange.HighDynamicRange10Plus), 15)
                .Worth(Member(DynamicRange.DolbyVisionWithFallback), 15)
                .Worth(Member(DynamicRange.DolbyVision), 15)

            // Eligibility. A camcorder recording of a projection and an unfinished edit are not the work;
            // most players will not open a whole disc, and a refusal that fires on a guess is the worst
            // thing this model can do, so a refusal acts only on a reading the release actually stated.
            .Refuse(
                Axis(nameof(VideoQuality.Origin)),
                Member(VideoOrigin.CameraCapture),
                Member(VideoOrigin.Workprint))
            .Because("A camera recording of a projection and an unfinished edit are not the film.")
            .Refuse(
                Axis(nameof(VideoQuality.Packaging)),
                Member(Packaging.DiscImage),
                Member(Packaging.DiscFolder))
            .Because("Most players cannot open a whole disc.")

            // Refusing an upscale is withheld rather than shipped present-and-inert: the scanners can now
            // see an upscale marker, but a release that states one states a resolution it does not have,
            // and refusing outright would discard the honest down-converted copies that state the same
            // marker. It is one chip for anyone who wants it.

            // Good enough: the point at which further upgrades cost bandwidth and gain little on a typical
            // display, stated as two independent floors rather than as one cell of a cross-product.
            .GoodEnoughAt(Axis(nameof(VideoQuality.Resolution)), AxisValue.Quantity(1080))
            .GoodEnoughAt(Axis(nameof(VideoQuality.Generation)), AxisValue.Quantity(1));
    }

    /// <summary>Spells one member of a closed video axis as a policy value.</summary>
    /// <typeparam name="TMember">The member type.</typeparam>
    /// <param name="member">The member.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// A value's identity is its declared rank and never its spelling, so the word carried here is for a
    /// reader and a rendered profile; a policy a user composed in an editor names the same member without
    /// the two having to agree on a word.
    /// </remarks>
    public static AxisValue Member<TMember>(TMember member)
        where TMember : struct, Enum =>
        AxisValue.Member(Convert.ToInt32(member, CultureInfo.InvariantCulture), member.ToString());

    private static QualityAxisId Axis(string property) => QualityAxisId.FromProperty(property);
}
