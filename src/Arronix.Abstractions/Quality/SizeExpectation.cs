using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>What size a file at one point is expected to be.</summary>
/// <param name="ExpectedBytes">The center of the expectation.</param>
/// <param name="FloorBytes">Below this, the file is implausibly small for what it claims.</param>
/// <param name="CeilingBytes">Above this, implausibly large.</param>
/// <param name="Basis">The computation, rendered, so a health check can explain a rejection.</param>
/// <remarks>
/// A size expectation is computed from the point's own axes rather than stored per rung, which is what
/// lets it answer for combinations no hand-written table has a row for. Where an input is missing and no
/// defensible center exists, the answer is <see cref="NotAssessable"/> rather than a band so wide it
/// asserts nothing.
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct SizeExpectation(
    long ExpectedBytes,
    long FloorBytes,
    long CeilingBytes,
    string Basis)
{
    /// <summary>Gets the expectation meaning "nothing can be said about the size of this point".</summary>
    public static SizeExpectation NotAssessable => default;

    /// <summary>Gets whether the expectation says anything at all.</summary>
    public bool IsAssessable => !string.IsNullOrEmpty(Basis) && CeilingBytes >= FloorBytes && CeilingBytes > 0;

    /// <summary>Assesses an actual size.</summary>
    /// <param name="sizeInBytes">The file's size.</param>
    /// <returns>The verdict.</returns>
    public SizeVerdict Assess(long sizeInBytes) =>
        !IsAssessable ? SizeVerdict.NotAssessable
        : sizeInBytes < FloorBytes ? SizeVerdict.ImplausiblySmall
        : sizeInBytes > CeilingBytes ? SizeVerdict.ImplausiblyLarge
        : SizeVerdict.Plausible;
}
