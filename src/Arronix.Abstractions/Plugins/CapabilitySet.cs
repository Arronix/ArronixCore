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
    private const uint NetworkImplyingMask =
        (1u << (int)Capability.Indexing)
        | (1u << (int)Capability.Metadata)
        | (1u << (int)Capability.Download)
        | (1u << (int)Capability.Notification)
        | (1u << (int)Capability.Curation)
        | (1u << (int)Capability.TelemetrySink);

    /// <summary>
    /// Exactly the bits the vocabulary declares, read from it rather than written down beside it. The mask
    /// is wider than the vocabulary on purpose: a capability added later is additive, and it must not be
    /// the thing that forces this public value type to change shape.
    /// </summary>
    private static readonly uint DeclaredBits = Declared();

    private readonly uint _mask;

    private CapabilitySet(uint mask) => _mask = mask;

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

        var mask = 0u;

        foreach (var capability in capabilities)
        {
            // Exactly the declared values, not a range: a vocabulary with a gap in it would otherwise admit
            // a value no name answers for. C# masks a shift count, so an undeclared value is not merely
            // meaningless here - it would set the bit of a real privilege 32 places below it.
            if (!IsDeclared(capability))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capabilities),
                    capability,
                    "Not a declared capability.");
            }

            mask |= 1u << (int)capability;
        }

        return new CapabilitySet(mask);
    }

    /// <summary>
    /// Determines whether the set contains a capability.
    /// </summary>
    /// <param name="capability">The capability to test for.</param>
    /// <returns><see langword="true"/> when the set contains it; otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// A value the vocabulary does not declare is not in any set, and is answered rather than thrown at:
    /// this is a gate's question, and the fail-closed answer to "may it?" about a privilege that does not
    /// exist is no. Checking it here is what stops a shift count C# masks aliasing a real privilege.
    /// </remarks>
    public bool Has(Capability capability)
        => IsDeclared(capability) && (_mask & (1u << (int)capability)) != 0;

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
    public CapabilitySet Union(CapabilitySet other) => new(_mask | other._mask);

    /// <summary>
    /// Returns the set with the implied capabilities added.
    /// </summary>
    /// <returns>The expanded set.</returns>
    /// <remarks>
    /// The network privilege is implied by every privilege that is an outbound network consumer by
    /// construction — a telemetry sink among them, because reading the whole stream and being able to send
    /// it are one decision. Nothing else is implied: the telemetry-processing seam exposes no sink, so it
    /// structurally requires neither that privilege nor the network one, and an importer that also needs to
    /// read the filesystem declares that separately, because silently widening least privilege to save an
    /// author one line is the wrong trade.
    /// </remarks>
    public CapabilitySet WithImplied()
        => (_mask & NetworkImplyingMask) != 0
            ? new CapabilitySet(_mask | (1u << (int)Capability.Network))
            : this;

    /// <summary>
    /// Lists the capabilities in the set, in declaration order.
    /// </summary>
    /// <returns>The capabilities.</returns>
    public IEnumerable<Capability> Enumerate()
    {
        var mask = _mask & DeclaredBits;
        for (var ordinal = 0; ordinal < 32; ordinal++)
        {
            if ((mask & (1u << ordinal)) != 0)
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

    private static bool IsDeclared(Capability capability)
        => (uint)capability < 32 && (DeclaredBits & (1u << (int)capability)) != 0;

    private static uint Declared()
    {
        var mask = 0u;

        foreach (var capability in Enum.GetValues<Capability>())
        {
            var ordinal = (int)capability;

            // Thirty-two privileges is a lot of privileges, and this is a public value type. A vocabulary
            // that outgrows it must widen this deliberately rather than have its newest capabilities
            // silently alias its oldest.
            if (ordinal is < 0 or >= 32)
            {
                throw new InvalidOperationException(
                    $"Capability '{capability}' has ordinal {ordinal}, which no 32-bit set can hold.");
            }

            mask |= 1u << ordinal;
        }

        return mask;
    }
}
