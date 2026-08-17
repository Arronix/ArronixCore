using Arronix.Abstractions.Quality;

// Reads and produces the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// The format families the host knows how to read quality for, and each kind's contribution to them.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by format family rather than by media kind, which is the whole placement decision: quality is a
/// property of a file, a file belongs to a family, and two kinds whose files are the same family share one
/// model with no duplication and therefore no drift. A kind that genuinely differs declares a family of its
/// own; a kind whose releases carry a naming dialect declares a refinement instead of a second model.
/// </para>
/// <para>
/// A family registers once. A second registration of the same family is refused rather than merged: two
/// declarations for one family are two opinions about what its files are, and silently keeping one of them
/// is how two kinds come to disagree about a shared taxonomy without anybody noticing.
/// </para>
/// </remarks>
internal sealed class QualityFamilyRegistry
{
    private readonly Dictionary<FormatFamilyId, IQualityType> families = [];

    /// <summary>Gets the families registered, in registration order.</summary>
    internal IReadOnlyCollection<IQualityType> Families => [.. families.Values];

    /// <summary>Registers one family's quality model.</summary>
    /// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
    /// <typeparam name="TType">The type declaring it.</typeparam>
    /// <returns>The registered model.</returns>
    /// <exception cref="ArgumentException">The family is already registered.</exception>
    internal IQualityType Add<TFacts, TType>()
        where TFacts : IQualityFacts
        where TType : IQualityType<TFacts>
    {
        var type = QualityTypeFactory.Create<TFacts, TType>();

        if (!families.TryAdd(type.Family, type))
        {
            throw new ArgumentException(
                $"'{type.Family}' already has a quality model. A format family has exactly one, because two "
                + "declarations for one family are two opinions about what its files are.",
                nameof(TType));
        }

        return type;
    }

    /// <summary>Adds one media kind's contribution to a family it does not own.</summary>
    /// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
    /// <typeparam name="TRefinement">The kind's refinement.</typeparam>
    /// <exception cref="ArgumentException">The family is not registered, or reads other facts.</exception>
    internal void RefineWith<TFacts, TRefinement>()
        where TFacts : IQualityFacts
        where TRefinement : IQualityRefinement<TFacts>
    {
        var family = TRefinement.Family;

        if (!families.TryGetValue(family, out var registered))
        {
            throw new ArgumentException(
                $"'{family}' has no quality model to refine. A kind contributes to a family that is already "
                + "declared; it does not bring one into existence by refining it.",
                nameof(TRefinement));
        }

        if (registered is not HostQualityType<TFacts> host)
        {
            throw new ArgumentException(
                $"'{family}' reads '{registered.FactsType.Name}' and the refinement contributes "
                + $"'{typeof(TFacts).Name}'.",
                nameof(TRefinement));
        }

        host.RefinedBy(TRefinement.Refine);
    }

    /// <summary>Finds a family's quality model.</summary>
    /// <param name="family">The family.</param>
    /// <param name="type">Receives the model.</param>
    /// <returns><see langword="true"/> when the family is registered.</returns>
    internal bool TryGet(FormatFamilyId family, out IQualityType type) => families.TryGetValue(family, out type!);

    /// <summary>Reads a family's quality model.</summary>
    /// <param name="family">The family.</param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentException">The family is not registered.</exception>
    internal IQualityType Get(FormatFamilyId family) =>
        families.TryGetValue(family, out var type)
            ? type
            : throw new ArgumentException(
                $"No quality model is registered for '{family}'.",
                nameof(family));
}
