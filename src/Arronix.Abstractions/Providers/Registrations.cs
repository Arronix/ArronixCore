using System.ComponentModel;
using System.Linq;

namespace Arronix.Abstractions.Providers;

/// <summary>A provider declaration paired with implementation and contract types for Host activation.</summary>
/// <remarks>
/// Registration carries types, never an already-constructed implementation. The host activates the type
/// only after the plugin and capability declaration have been admitted. It supplies the scoped plugin
/// context through an exact public <c>(IPluginContext)</c> constructor, or uses a public parameterless
/// constructor when no context is needed. Host DI is never consulted.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
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

    /// <summary>Records a cataloger, deriving its pairing from the contract it actually implements.</summary>
    /// <typeparam name="TImplementation">The cataloger implementation.</typeparam>
    /// <param name="descriptor">The provider declaration.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TImplementation"/> implements no closed <see cref="ICataloger{TItem}"/>, or more
    /// than one.
    /// </exception>
    /// <remarks>
    /// No type is passed in. The implementation closed <see cref="ICataloger{TItem}"/> over its item type at
    /// its own compile time, and that closed interface — read from the implementation's own interface list,
    /// once, here — is the only authority for both the contract and the item type. The family is fixed
    /// because this method <i>is</i> the cataloger registration.
    /// </remarks>
    public static ProviderTypeRegistration ForCataloger<TImplementation>(ProviderDescriptor descriptor)
        where TImplementation : class, IClosedCataloger
        => ForClosedContract<TImplementation>(descriptor, typeof(ICataloger<>), ProviderFamily.Cataloger);

    /// <summary>Records a curator, deriving its pairing from the contract it actually implements.</summary>
    /// <typeparam name="TImplementation">The curator implementation.</typeparam>
    /// <param name="descriptor">The provider declaration.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TImplementation"/> implements no closed <see cref="ICurator{TItem}"/>, or more
    /// than one.
    /// </exception>
    /// <remarks>
    /// The curator half of <see cref="ForCataloger{TImplementation}"/>. One class may serve both families;
    /// each registration reads only its own family's closed contract, so the two never collapse into one.
    /// </remarks>
    public static ProviderTypeRegistration ForCurator<TImplementation>(ProviderDescriptor descriptor)
        where TImplementation : class, IClosedCurator
        => ForClosedContract<TImplementation>(descriptor, typeof(ICurator<>), ProviderFamily.Curator);

    /// <summary>
    /// Finds the one closed contract of a family that an implementation implements, and records it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is one-time type inspection, and it is stated rather than hidden. The generic constraint is an
    /// ergonomic gate: it names the family so the ordinary mistakes are compiler errors, but a marker
    /// interface can be implemented directly, so it cannot by itself prove that a closed contract exists.
    /// Reading the implementation's own interface list is what proves it, and it happens once, while the
    /// extension is registering, long before Host would construct anything.
    /// </para>
    /// <para>
    /// Zero and several are both refused. Zero means the implementation claimed a family it never closed a
    /// contract for. Several means the pairing is ambiguous, and choosing one of them here would make the
    /// erased registration disagree with a contract the author actually wrote.
    /// </para>
    /// </remarks>
    private static ProviderTypeRegistration ForClosedContract<TImplementation>(
        ProviderDescriptor descriptor,
        Type openContract,
        ProviderFamily family)
        where TImplementation : class, IProvider
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var implementation = typeof(TImplementation);
        var closed = implementation
            .GetInterfaces()
            .Where(contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == openContract)
            .OrderBy(contract => contract.AssemblyQualifiedName, StringComparer.Ordinal)
            .ToArray();

        if (closed.Length == 0)
        {
            throw new InvalidOperationException(
                $"'{implementation.FullName}' is registered as a {family} but implements no "
                + $"'{openContract.Name}'. Implement the closed contract — the item type is read from it, "
                + "and a marker interface alone states nothing the platform can check.");
        }

        if (closed.Length > 1)
        {
            var named = string.Join(", ", closed.Select(contract => contract.FullName));
            throw new InvalidOperationException(
                $"'{implementation.FullName}' implements {closed.Length} {family} contracts ({named}). "
                + "One registration pairs one implementation with one item type; register a separate "
                + "implementation per media kind rather than leaving the platform to choose between them.");
        }

        var contract = closed[0];
        return new ProviderTypeRegistration
        {
            Descriptor = descriptor,
            Family = family,
            ContractType = contract,
            ImplementationType = implementation,
            MediaItemType = contract.GetGenericArguments()[0],
        };
    }
}
