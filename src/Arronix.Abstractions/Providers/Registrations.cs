using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// Registers a release source: what it is, and the implementation that answers for it.
/// </summary>
/// <param name="Descriptor">What the provider is and how it is configured.</param>
/// <param name="Provider">The already-constructed implementation.</param>
/// <remarks>
/// The descriptor and the implementation are registered together so that the host can admit or refuse the
/// pair as a unit. An implementation with no declaration could not be configured, and a declaration with
/// no implementation could not be called.
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record IndexerRegistration(ProviderDescriptor Descriptor, IIndexer Provider);

/// <summary>
/// Registers a transfer client: what it is, and the implementation that answers for it.
/// </summary>
/// <param name="Descriptor">What the provider is and how it is configured.</param>
/// <param name="Provider">The already-constructed implementation.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record DownloaderRegistration(ProviderDescriptor Descriptor, IDownloader Provider);

/// <summary>
/// Registers a notification destination: what it is, and the implementation that answers for it.
/// </summary>
/// <param name="Descriptor">What the provider is and how it is configured.</param>
/// <param name="Provider">The already-constructed implementation.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record NotifierRegistration(ProviderDescriptor Descriptor, INotifier Provider);

/// <summary>
/// Registers a cataloger: what it is, and the implementation that answers for it.
/// </summary>
/// <param name="Descriptor">What the provider is and how it is configured.</param>
/// <param name="Provider">The already-constructed implementation.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record CatalogerRegistration(ProviderDescriptor Descriptor, ICataloger Provider);

/// <summary>
/// Registers a curator: what it is, and the implementation that answers for it.
/// </summary>
/// <param name="Descriptor">What the provider is and how it is configured.</param>
/// <param name="Provider">The already-constructed implementation.</param>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record CuratorRegistration(ProviderDescriptor Descriptor, ICurator Provider);
