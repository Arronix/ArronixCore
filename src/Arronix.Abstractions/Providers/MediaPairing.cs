namespace Arronix.Abstractions.Providers;

/// <summary>The closed media relationship a cataloger contract already states.</summary>
/// <remarks>
/// <para>
/// Host binding SPI, not authoring vocabulary. <see cref="ICataloger{TItem}"/> answers both members from its
/// own type argument, so a cataloger author names the item type once — in the contract the implementation
/// closes — and never again in a registration argument that has to be kept in step by hand. An author
/// neither implements this interface nor names it.
/// </para>
/// <para>
/// It is public only because an interface cannot inherit a less accessible one, and it carries
/// <see cref="Type"/> values because registration is where the compile-time relationship becomes the host's
/// kind-blind projection. That erasure is one-way.
/// </para>
/// <para>
/// It deliberately shares no base interface with <see cref="ICuratorPairing"/>. A common base would make the
/// two implementations ambiguous on one class that serves both families, and it would let a curator satisfy
/// a cataloger registration.
/// </para>
/// </remarks>
public interface ICatalogerPairing
{
    /// <summary>Gets the media-owned item type the cataloger contract is closed over.</summary>
    static abstract Type PairedItemType { get; }

    /// <summary>Gets the closed cataloger contract the implementation is registered under.</summary>
    static abstract Type PairedContractType { get; }
}

/// <summary>The closed media relationship a curator contract already states.</summary>
/// <remarks>
/// The curator half of the same binding SPI, supplied by <see cref="ICurator{TItem}"/> and never written by
/// an author. See <see cref="ICatalogerPairing"/> for why the two are separate interfaces.
/// </remarks>
public interface ICuratorPairing
{
    /// <summary>Gets the media-owned item type the curator contract is closed over.</summary>
    static abstract Type PairedItemType { get; }

    /// <summary>Gets the closed curator contract the implementation is registered under.</summary>
    static abstract Type PairedContractType { get; }
}
