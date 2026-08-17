using System.Linq;
using Arronix.Abstractions.Quality;

// Reads and produces the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// Turns a family's authoring seam into the runtime model the host holds and the client is served.
/// </summary>
/// <remarks>
/// Three reads and no more: the facts type's properties, which are the axes; the family's own
/// configuration, which is everything an attribute cannot state; and its reading of evidence, which is
/// ordinary code. Nothing is registered twice and nothing is declared in two places, so a family cannot end
/// up holding two answers to one question.
/// </remarks>
internal static class QualityTypeFactory
{
    /// <summary>Builds one family's runtime quality model.</summary>
    /// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
    /// <typeparam name="TType">The type declaring it.</typeparam>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentException">
    /// The facts type declares no valid axis, or the family declares a rendering rule outside the rendering
    /// grammar, or its stated policy does not compile against its own axes.
    /// </exception>
    internal static HostQualityType<TFacts> Create<TFacts, TType>()
        where TFacts : IQualityFacts
        where TType : IQualityType<TFacts>
    {
        var axes = QualityAxisReader.Read(typeof(TFacts));
        var byProperty = axes.ToDictionary(
            static axis => axis.Property.Name,
            static axis => axis,
            StringComparer.Ordinal);

        var builder = new QualityTypeBuilder<TFacts>(byProperty);

        TType.Configure(builder);

        var type = new HostQualityType<TFacts>(TType.Family, TType.Read, axes, builder);

        // Compiling the stated policy here rather than on first use turns a family that mis-states its own
        // opinion into a failure at registration, which is where the person who wrote it is standing.
        _ = type.DefaultPolicy;

        return type;
    }
}
