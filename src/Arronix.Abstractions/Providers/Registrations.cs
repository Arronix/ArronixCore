using Arronix.Abstractions.Media;

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

    public static ProviderTypeRegistration ForCataloger<TItem, TImplementation>(ProviderDescriptor descriptor)
        where TItem : class, IMediaItem
        where TImplementation : class, ICataloger<TItem>
    {
        var registration = For<ICataloger<TItem>, TImplementation>(descriptor, ProviderFamily.Cataloger);
        return registration with { MediaItemType = typeof(TItem) };
    }

    public static ProviderTypeRegistration ForCurator<TItem, TImplementation>(ProviderDescriptor descriptor)
        where TItem : class, IMediaItem
        where TImplementation : class, ICurator<TItem>
    {
        var registration = For<ICurator<TItem>, TImplementation>(descriptor, ProviderFamily.Curator);
        return registration with { MediaItemType = typeof(TItem) };
    }
}
