using System.Net;
using System.Net.Sockets;

namespace Arronix.Common.Net;

/// <summary>
/// Operations on host names and addresses in their textual form.
/// </summary>
/// <remarks>
/// These are networking questions that happen to be asked of a string, and they live with the rest of the
/// networking code rather than among the text helpers, where the platform used to keep them. Filed under
/// text, an address validator sits beside case conversion and quoting and is found by nobody looking for it.
/// </remarks>
public static class HostAddressExtensions
{
    /// <summary>
    /// Determines whether the text is an address the platform can bind to or connect to.
    /// </summary>
    /// <param name="value">The text to test. May be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the text is a well-formed unicast IPv4 or IPv6 address; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// The broadcast address and IPv6 multicast addresses are rejected: they parse, but neither is something
    /// a caller configuring an endpoint can have meant, and accepting them turns a configuration mistake into
    /// a runtime failure much further from its cause.
    /// </remarks>
    public static bool IsValidIpAddress(this string? value)
    {
        if (!IPAddress.TryParse(value, out var address))
        {
            return false;
        }

        if (address.Equals(IPAddress.Broadcast) || address.IsIPv6Multicast)
        {
            return false;
        }

        return address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6;
    }

    /// <summary>
    /// Renders a host for use in the authority part of a URL, bracketing an IPv6 literal.
    /// </summary>
    /// <param name="host">The host name or address.</param>
    /// <returns>
    /// The host unchanged, or wrapped in square brackets when it is an IPv6 literal that is not already
    /// bracketed.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Without the brackets an IPv6 literal and its port cannot be told apart, because both use a colon. The
    /// already-bracketed case is checked as well as the bare one: the implementation this replaces bracketed
    /// anything containing a colon unconditionally, so a host that had been through it twice — which happens
    /// as soon as one layer normalizes a value another layer already normalized — came out doubly bracketed
    /// and unparseable.
    /// </remarks>
    public static string ToUrlHost(this string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!host.Contains(':'))
        {
            return host;
        }

        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            return host;
        }

        return string.Concat("[", host, "]");
    }
}
