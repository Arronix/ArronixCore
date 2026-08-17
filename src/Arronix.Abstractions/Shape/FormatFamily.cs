using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.DTOs;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// A family of file formats with a quality ladder of its own.
/// </summary>
/// <remarks>
/// <para>
/// One surveyed application needed two incomparable format dimensions, did not have them, and simulated
/// the second with reserved bands inside a single ordered ladder — with two "unknown" sentinels
/// interleaved. The consequence is that any file of the second kind ranks as an upgrade over any file of
/// the first. One ladder per family makes that unrepresentable.
/// </para>
/// <para>
/// The extension set is the declared discriminator because it is empirically what every implementation
/// actually branches on, and because the validator can then require the sets to be disjoint — an
/// overlapping extension makes the family of a file undecidable.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record FormatFamily
{
    /// <summary>
    /// Gets the identifier levels and files reference this family by.
    /// </summary>
    public required string FamilyId { get; init; }

    /// <summary>
    /// Gets the family's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the file extensions that identify the family, leading dot included. Disjoint across the
    /// families of one shape.
    /// </summary>
    public required IReadOnlyList<string> FileExtensions { get; init; }

    /// <summary>
    /// Gets the ordered quality ladder for this family. Reuses the stable quality tier, so cutoff
    /// policies and quality models keep working unchanged.
    /// </summary>
    public required IReadOnlyList<QualityTier> Ladder { get; init; }

    /// <summary>
    /// Gets the tier assigned to a file whose quality could not be determined. Not a member of
    /// <see cref="Ladder"/>.
    /// </summary>
    public required QualityTier Unknown { get; init; }

    /// <summary>
    /// Gets a value indicating whether an item may hold files of this family and of another at the same
    /// time, rather than the families being alternatives.
    /// </summary>
    public bool CoexistsWithOtherFamilies { get; init; }

    /// <summary>
    /// Gets a value indicating whether files of this family carry embedded metadata that can be read and
    /// written.
    /// </summary>
    public bool SupportsEmbeddedMetadata { get; init; }

    /// <summary>
    /// Gets the per-file, kind-meaningful facts files of this family may carry. Declared here because
    /// they belong to a file rather than an item and to one kind rather than the platform, so neither a
    /// level's field list nor the host-global probe vocabulary can own them.
    /// </summary>
    public IReadOnlyList<TechnicalFacet> TechnicalFacets { get; init; } = [];
}
