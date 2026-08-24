namespace Arronix.Abstractions.Providers;

/// <summary>One installed provider implementation, as a consumer configuring it sees it.</summary>
/// <param name="Provider">
/// The host-minted qualified identifier. A configuration must carry this exact value, so it is served with
/// the declaration rather than left for a consumer to reconstruct from a local name.
/// </param>
/// <param name="Family">The kind of external service, from the registration the provider was admitted through.</param>
/// <param name="Descriptor">What the provider declared about itself and the settings a definition carries.</param>
/// <remarks>
/// The host builds this; nothing implements it. It exists because identity and family are host-owned facts
/// and the declaration is the extension's, and pairing them here is what lets a consumer render and
/// configure any family without an extension restating either fact.
/// </remarks>
public sealed record ProviderCatalogEntry(
    ProviderId Provider,
    ProviderFamily Family,
    ProviderDescriptor Descriptor);
