using System.Linq;

namespace Arronix.Abstractions.Plugins;

/// <summary>
/// A set of capabilities, held as a bitmask.
/// </summary>
/// <remarks>
/// Value-equal and allocation-free, which matters because the grant check runs on every gated
/// registration and every gated dependency read. Holding the set as a bitmask also turns the implication
/// rule and the "does this grant cover that requirement" test into single operations rather than string
/// arithmetic over a list.
/// </remarks>
public readonly record struct CapabilitySet
{
    private const ushort NetworkImplyingMask =
        (1 << (int)Capability.Indexing)
        | (1 << (int)Capability.Metadata)
        | (1 << (int)Capability.Download)
        | (1 << (int)Capability.Notification)
        | (1 << (int)Capability.Curation);

    private readonly ushort _mask;

    private CapabilitySet(ushort mask) => _mask = mask;

    /// <summary>
    /// Gets the empty set.
    /// </summary>
    public static CapabilitySet None => default;

    /// <summary>
    /// Creates a set.
    /// </summary>
    /// <param name="capabilities">The capabilities the set contains.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="capabilities"/> is <see langword="null"/>.</exception>
    public static CapabilitySet Of(params Capability[] capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var mask = 0;
        foreach (var capability in capabilities)
        {
            mask |= 1 << (int)capability;
        }

        return new CapabilitySet((ushort)mask);
    }

    /// <summary>
    /// Determines whether the set contains a capability.
    /// </summary>
    /// <param name="capability">The capability to test for.</param>
    /// <returns><see langword="true"/> when the set contains it; otherwise <see langword="false"/>.</returns>
    public bool Has(Capability capability) => (_mask & (1 << (int)capability)) != 0;

    /// <summary>
    /// Determines whether this set contains everything another does.
    /// </summary>
    /// <param name="other">The set that must be covered.</param>
    /// <returns><see langword="true"/> when this set covers <paramref name="other"/>.</returns>
    public bool IsSupersetOf(CapabilitySet other) => (_mask & other._mask) == other._mask;

    /// <summary>
    /// Returns the set containing everything in either set.
    /// </summary>
    /// <param name="other">The set to combine with.</param>
    /// <returns>The combined set.</returns>
    public CapabilitySet Union(CapabilitySet other) => new((ushort)(_mask | other._mask));

    /// <summary>
    /// Returns the set with the implied capabilities added.
    /// </summary>
    /// <returns>The expanded set.</returns>
    /// <remarks>
    /// The network privilege is implied by every privilege that is an outbound network consumer by
    /// construction. Nothing else is implied — in particular, an importer that also needs to read the
    /// filesystem declares that separately, because silently widening least privilege to save an
    /// extension author one line is the wrong trade.
    /// </remarks>
    public CapabilitySet WithImplied()
        => (_mask & NetworkImplyingMask) != 0
            ? new CapabilitySet((ushort)(_mask | (1 << (int)Capability.Network)))
            : this;

    /// <summary>
    /// Lists the capabilities in the set, in declaration order.
    /// </summary>
    /// <returns>The capabilities.</returns>
    public IEnumerable<Capability> Enumerate()
    {
        var mask = _mask;
        for (var ordinal = 0; ordinal < 16; ordinal++)
        {
            if ((mask & (1 << ordinal)) != 0)
            {
                yield return (Capability)ordinal;
            }
        }
    }

    /// <summary>
    /// Gets the wire names of the set's capabilities, comma-separated.
    /// </summary>
    /// <returns>The set's text form.</returns>
    public override string ToString()
        => string.Join(", ", Enumerate().Select(CapabilityNames.ToWireName));
}
