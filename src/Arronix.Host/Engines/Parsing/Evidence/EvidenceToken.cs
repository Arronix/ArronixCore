using Arronix.Abstractions.Quality;

// Quality contracts are experimental; the scan reports its resolution claims in their vocabulary.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Parsing.Evidence;

/// <summary>
/// One functional category of release-title vocabulary.
/// </summary>
/// <remarks>
/// The classes exist so that an ambiguity rule can be written about a category rather than about a
/// spelling: "a two-letter segment is claimed only beside a resolution or a codec" is one rule over one
/// list, where the same statement written per spelling is thirty rules that drift apart.
/// </remarks>
internal enum EvidenceTokenClass
{
    /// <summary>The segment matched nothing.</summary>
    Unknown = 0,

    /// <summary>The signal the file descends from.</summary>
    Source = 1,

    /// <summary>A statement that the file carries a master's own bitstream.</summary>
    Remux = 2,

    /// <summary>A statement about the raster.</summary>
    Resolution = 3,

    /// <summary>The video codec.</summary>
    VideoCodec = 4,

    /// <summary>The audio presentation.</summary>
    AudioFormat = 5,

    /// <summary>A dynamic-range format.</summary>
    DynamicRange = 6,

    /// <summary>A streaming service.</summary>
    Distributor = 7,

    /// <summary>A defect the release states it carries.</summary>
    Flaw = 8,

    /// <summary>How the release is packaged.</summary>
    Packaging = 9,

    /// <summary>The container the release names.</summary>
    Container = 10,

    /// <summary>A named language.</summary>
    Language = 11,

    /// <summary>A marker stating that more than one language is carried, without naming the second.</summary>
    LanguageMarker = 12,

    /// <summary>A re-issue statement.</summary>
    Revision = 13,

    /// <summary>A stated frame rate.</summary>
    FrameRate = 14,
}

/// <summary>
/// One recognized token, the normalized value it carries, and where in the title it sat.
/// </summary>
/// <remarks>
/// <para>
/// Position is carried because several of the ambiguity rules are about arrangement rather than about
/// spelling, and a rule about arrangement cannot be written against a bag of strings.
/// </para>
/// <para>
/// <see cref="Magnitude"/>, <see cref="Form"/> and <see cref="Scan"/> are meaningful only for the classes
/// that produce them — a line count, a re-issue number, a frame rate — and are left at their defaults
/// everywhere else. That is a deliberate flat shape rather than a class hierarchy: there are three
/// numeric classes out of fifteen, and a hierarchy would cost every consumer a type test to read a
/// number.
/// </para>
/// </remarks>
/// <param name="Class">The functional category.</param>
/// <param name="Value">The normalized value, drawn from the token vocabularies.</param>
/// <param name="Index">The index of the first segment the token was read from.</param>
/// <param name="SegmentCount">How many adjacent segments the token consumed.</param>
/// <param name="Magnitude">The number the token states, for the classes that state one.</param>
/// <param name="Form">How a resolution claim was stated.</param>
/// <param name="Scan">The scan type a resolution claim carried, when it carried one.</param>
internal readonly record struct EvidenceToken(
    EvidenceTokenClass Class,
    string Value,
    int Index,
    int SegmentCount,
    double Magnitude,
    ResolutionClaimForm Form,
    ScanType? Scan)
{
    /// <summary>Creates a token that states no number.</summary>
    /// <param name="tokenClass">The functional category.</param>
    /// <param name="value">The normalized value.</param>
    /// <param name="index">The index of the first segment.</param>
    /// <param name="segmentCount">How many segments were consumed.</param>
    /// <returns>The token.</returns>
    internal static EvidenceToken Of(EvidenceTokenClass tokenClass, string value, int index, int segmentCount) =>
        new(tokenClass, value, index, segmentCount, 0d, ResolutionClaimForm.LineCount, null);

    /// <summary>Creates a token that states a number.</summary>
    /// <param name="tokenClass">The functional category.</param>
    /// <param name="value">The normalized value.</param>
    /// <param name="index">The index of the first segment.</param>
    /// <param name="segmentCount">How many segments were consumed.</param>
    /// <param name="magnitude">The number stated.</param>
    /// <returns>The token.</returns>
    internal static EvidenceToken Number(
        EvidenceTokenClass tokenClass,
        string value,
        int index,
        int segmentCount,
        double magnitude) =>
        new(tokenClass, value, index, segmentCount, magnitude, ResolutionClaimForm.LineCount, null);

    /// <summary>Creates a resolution claim.</summary>
    /// <param name="index">The index of the first segment.</param>
    /// <param name="segmentCount">How many segments were consumed.</param>
    /// <param name="lines">The vertical resolution claimed, in lines.</param>
    /// <param name="form">How the claim was stated.</param>
    /// <param name="scan">The scan type the claim carried, when it carried one.</param>
    /// <returns>The token.</returns>
    internal static EvidenceToken Lines(
        int index,
        int segmentCount,
        int lines,
        ResolutionClaimForm form,
        ScanType? scan) =>
        new(EvidenceTokenClass.Resolution, "lines", index, segmentCount, lines, form, scan);
}
