using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>
/// The external identifiers one entity is known by, carried as one property rather than one property per
/// catalog.
/// </summary>
/// <remarks>
/// <para>
/// A property per catalog is the shape that puts vendor names inside a media kind: adding a catalog then
/// means adding a field, a descriptor, a filter and a naming token, none of which the kind's domain
/// actually gained. One set-valued property says the same thing without naming anybody.
/// </para>
/// <para>
/// The set is scoped by the entity that declares it, and that is load-bearing: a group's identifiers and
/// an item's identifiers live in different key spaces and must never be compared. Under a string surface
/// that distinction had to be smuggled into a scheme name; here it is the difference between two
/// properties on two types.
/// </para>
/// </remarks>
public sealed record ExternalIdSet
{
    /// <summary>
    /// Gets the set an entity no cataloger has spoken for carries.
    /// </summary>
    public static ExternalIdSet Empty { get; } = new();

    /// <summary>
    /// Gets the identifiers, in the order the catalogers that supplied them were consulted.
    /// </summary>
    public IReadOnlyList<ExternalId> Values { get; init; } = [];

    /// <summary>
    /// Creates a set from identifiers.
    /// </summary>
    /// <param name="ids">The identifiers.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ids"/> is <see langword="null"/>.</exception>
    public static ExternalIdSet Of(params ExternalId[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Length == 0 ? Empty : new ExternalIdSet { Values = [.. ids] };
    }

    /// <summary>
    /// Creates a set from a sequence of identifiers.
    /// </summary>
    /// <param name="ids">The identifiers.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ids"/> is <see langword="null"/>.</exception>
    public static ExternalIdSet From(IEnumerable<ExternalId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var values = ids.ToArray();
        return values.Length == 0 ? Empty : new ExternalIdSet { Values = values };
    }

    /// <summary>
    /// Attempts to read the identifier assigned by one catalog.
    /// </summary>
    /// <param name="scheme">The assigning catalog, compared ordinally in its canonical lower-case form.</param>
    /// <param name="id">The identifier when one was carried; otherwise the default value.</param>
    /// <returns><see langword="true"/> when one was carried; otherwise <see langword="false"/>.</returns>
    public bool TryGet(string scheme, out ExternalId id)
    {
        foreach (var candidate in Values)
        {
            if (string.Equals(candidate.Scheme, scheme, StringComparison.Ordinal))
            {
                id = candidate;
                return true;
            }
        }

        id = default;
        return false;
    }
}
