using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Providers;

/// <summary>One installed provider implementation, as a consumer configuring it sees it.</summary>
/// <param name="Provider">
/// The host-minted qualified identifier. A configuration must carry this exact value, so it is served with
/// the declaration rather than left for a consumer to reconstruct from a local name.
/// </param>
/// <param name="Family">The kind of external service, from the registration the provider was admitted through.</param>
/// <param name="Descriptor">What the provider declared about itself and the settings a definition carries.</param>
/// <param name="PairedMediaKind">
/// The one media kind this provider's closed media contract was admitted for, or <see langword="null"/>
/// when its provider family has no media pairing.
/// </param>
/// <param name="CatalogScheme">
/// The canonical external identifier scheme declared by an admitted cataloger, or <see langword="null"/>
/// for every other provider family.
/// </param>
/// <remarks>
/// The host builds this; nothing implements it. It exists because identity and family are host-owned facts
/// and the declaration is the extension's, and pairing them here is what lets a consumer render and
/// configure any family without an extension restating either fact. <paramref name="PairedMediaKind"/> is
/// deliberately singular: one registration closes one media-item contract, and admission has already
/// established that the closed item type belongs to at most one kind.
/// </remarks>
public sealed record ProviderCatalogEntry(
    ProviderId Provider,
    ProviderFamily Family,
    ProviderDescriptor Descriptor,
    MediaKindId? PairedMediaKind,
    string? CatalogScheme);
