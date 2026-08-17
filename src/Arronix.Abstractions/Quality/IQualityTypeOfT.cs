using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>The authoring seam: one type per format family, declaring how evidence becomes facts.</summary>
/// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityType<TFacts>
    where TFacts : IQualityFacts
{
    /// <summary>Gets the format family these facts describe.</summary>
    static abstract FormatFamilyId Family { get; }

    /// <summary>Declares what the axis attributes cannot: labels, the size model, the stated default.</summary>
    /// <param name="builder">The builder.</param>
    static abstract void Configure(IQualityTypeBuilder<TFacts> builder);

    /// <summary>Reads release and file evidence onto the axes.</summary>
    /// <param name="evidence">What the parser and any probe produced.</param>
    /// <returns>The facts.</returns>
    /// <remarks>
    /// Ordinary code. There is nothing for evidence to be collapsed <i>to</i>, so the whole <i>ranking</i>
    /// cascade a rung table performs disappears. The <i>inference</i> cascade does not — it moves in here,
    /// smaller and local to one function per axis.
    /// </remarks>
    static abstract TFacts Read(ReleaseEvidence evidence);
}
