using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// The kinds of external service the platform integrates with.
/// </summary>
/// <remarks>
/// Closed, because the host dispatches on it: each family has its own registration method, its own status
/// policy and its own configuration surface. It is a value rather than a generic type argument
/// specifically so that a provider event can cross an extension boundary — a generic event cannot.
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum ProviderFamily
{
    /// <summary>A source of release candidates.</summary>
    Indexer = 0,

    /// <summary>A client that transfers releases.</summary>
    Downloader = 1,

    /// <summary>A destination for outbound notifications.</summary>
    Notifier = 2,

    /// <summary>
    /// An external authority that answers what a thing <i>is</i>: canonical facts that populate the
    /// catalog record.
    /// </summary>
    Cataloger = 3,

    /// <summary>
    /// An external list that answers which things you <i>want</i>: it selects what belongs in the library.
    /// </summary>
    Curator = 4
}
