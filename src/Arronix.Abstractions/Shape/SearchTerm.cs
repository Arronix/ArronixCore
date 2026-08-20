using System.Globalization;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// A kind of argument a release search can carry.
/// </summary>
/// <remarks>
/// <para>
/// The <i>intersection</i> of the parallel per-domain parameter vocabularies the surveyed aggregator
/// carries, not their union: the union is roughly forty members, most of them one domain's spelling of
/// another's. Every external catalog identifier collapses into
/// <see cref="ExternalIdentifier"/> because they are all namespaced external identifiers, and every
/// intra-work position collapses into <see cref="Ordinal"/> because they are all coordinates.
/// </para>
/// <para>
/// Nine members, closed, and not one of them names a media noun — which is what lets eligibility between
/// a kind and a source be plain set intersection, with neither side knowing the other's vocabulary.
/// </para>
/// </remarks>
public enum SearchTerm
{
    /// <summary>Unstructured query text. Universally supported.</summary>
    FreeText = 0,

    /// <summary>The title of the work being sought.</summary>
    WorkTitle = 1,

    /// <summary>The person or group responsible for the work.</summary>
    Creator = 2,

    /// <summary>The organization that published or released the work.</summary>
    Issuer = 3,

    /// <summary>A genre or category label.</summary>
    Genre = 4,

    /// <summary>A year of release.</summary>
    Year = 5,

    /// <summary>An identifier assigned by an external catalog.</summary>
    ExternalIdentifier = 6,

    /// <summary>A position within the work, expressed as an ordinal component.</summary>
    Ordinal = 7,

    /// <summary>A calendar date of release.</summary>
    Date = 8
}

/// <summary>
/// A release-category identifier, carried verbatim as an interoperability value.
/// </summary>
/// <param name="Value">The category number.</param>
/// <remarks>
/// A working, deployed cross-media taxonomy that every release source already speaks. Replacing it would
/// buy nothing and cost a mapping table per source, so the platform carries it, compares it, and never
/// interprets it. Values at or above one hundred thousand are private to one source and are excluded from
/// aggregation.
/// </remarks>
public readonly record struct CategoryId(int Value)
{
    /// <summary>
    /// The lowest value in the band reserved for source-private categories.
    /// </summary>
    public const int ProviderSpecificFloor = 100_000;

    /// <summary>
    /// Gets a value indicating whether the category is private to one source and therefore not
    /// comparable across sources.
    /// </summary>
    public bool IsProviderSpecific => Value >= ProviderSpecificFloor;

    /// <summary>
    /// Creates a category identifier.
    /// </summary>
    /// <param name="value">The category number.</param>
    /// <returns>The identifier.</returns>
    public static CategoryId FromInt(int value) => new(value);

    /// <summary>
    /// Gets the category's decimal form.
    /// </summary>
    /// <returns>The category text.</returns>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
