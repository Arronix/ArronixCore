using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// The transfer protocol a release is obtained over.
/// </summary>
/// <param name="Value">The protocol token, lower-case.</param>
/// <remarks>
/// An open token rather than a closed enumeration, on evidence: one surveyed application shipped the
/// closed form, ran into a protocol it could not name, and had to migrate its stored data to widen it.
/// The two the platform knows by name are provided as constants; anything else is just a token.
/// </remarks>
[Experimental(ExperimentalContracts.Providers, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct DownloadProtocol(string Value)
{
    /// <summary>
    /// The peer-to-peer transfer protocol.
    /// </summary>
    public static readonly DownloadProtocol Torrent = new("torrent");

    /// <summary>
    /// The store-and-forward article protocol.
    /// </summary>
    public static readonly DownloadProtocol Usenet = new("usenet");

    /// <summary>
    /// Gets the protocol token.
    /// </summary>
    /// <returns>The token.</returns>
    public override string ToString() => Value ?? string.Empty;
}
