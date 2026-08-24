namespace Arronix.Abstractions.Providers;

/// <summary>A provider declaration paired with implementation and contract types for Host activation.</summary>
/// <remarks>
/// Registration carries types, never an already-constructed implementation. The host activates the type
/// only after the plugin and capability declaration have been admitted. It supplies the scoped plugin
/// context through an exact public <c>(IPluginContext)</c> constructor, or uses a public parameterless
/// constructor when no context is needed. Host DI is never consulted.
/// </remarks>
public sealed record ProviderTypeRegistration
{
    public required ProviderDescriptor Descriptor { get; init; }

    public required ProviderFamily Family { get; init; }

    public required Type ContractType { get; init; }

    public required Type ImplementationType { get; init; }

    /// <summary>Gets the media item type a paired provider closed its contract over.</summary>
    /// <remarks>
    /// Read from the contract rather than declared beside it, and <see langword="null"/> for the families
    /// that have no media pairing. Admission compares it with the item type of an active media kind before
    /// the implementation is constructed.
    /// </remarks>
    public Type? MediaItemType { get; init; }

    public static ProviderTypeRegistration For<TContract, TImplementation>(
        ProviderDescriptor descriptor,
        ProviderFamily family)
        where TContract : class, IProvider
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new ProviderTypeRegistration
        {
            Descriptor = descriptor,
            Family = family,
            ContractType = typeof(TContract),
            ImplementationType = typeof(TImplementation)
        };
    }

    /// <summary>Records a cataloger, reading its item and contract types off the contract it closed.</summary>
    /// <typeparam name="TImplementation">The cataloger implementation.</typeparam>
    /// <param name="descriptor">The provider declaration.</param>
    /// <returns>The registration.</returns>
    /// <remarks>
    /// Neither type is passed in. The implementation closed <see cref="ICataloger{TItem}"/> over its item
    /// type at its own compile time and <see cref="ICatalogerPairing"/> reads exactly that back, so there is
    /// no second type argument to keep in step and no way for the erased pairing to disagree with the
    /// contract. The family is fixed here because this method <i>is</i> the cataloger registration.
    /// </remarks>
    public static ProviderTypeRegistration ForCataloger<TImplementation>(ProviderDescriptor descriptor)
        where TImplementation : class, ICataloger, ICatalogerPairing
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new ProviderTypeRegistration
        {
            Descriptor = descriptor,
            Family = ProviderFamily.Cataloger,
            ContractType = TImplementation.PairedContractType,
            ImplementationType = typeof(TImplementation),
            MediaItemType = TImplementation.PairedItemType,
        };
    }

    /// <summary>Records a curator, reading its item and contract types off the contract it closed.</summary>
    /// <typeparam name="TImplementation">The curator implementation.</typeparam>
    /// <param name="descriptor">The provider declaration.</param>
    /// <returns>The registration.</returns>
    /// <remarks>
    /// The curator half of <see cref="ForCataloger{TImplementation}"/>. One class may serve both families;
    /// each registration reads its own family's pairing, so the two never collapse into one.
    /// </remarks>
    public static ProviderTypeRegistration ForCurator<TImplementation>(ProviderDescriptor descriptor)
        where TImplementation : class, IProvider, ICuratorPairing
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new ProviderTypeRegistration
        {
            Descriptor = descriptor,
            Family = ProviderFamily.Curator,
            ContractType = TImplementation.PairedContractType,
            ImplementationType = typeof(TImplementation),
            MediaItemType = TImplementation.PairedItemType,
        };
    }
}
