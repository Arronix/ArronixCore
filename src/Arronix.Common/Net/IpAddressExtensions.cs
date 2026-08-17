using System.Net;
using System.Net.Sockets;

namespace Arronix.Common.Net;

/// <summary>
/// Classification predicates for IP addresses that the base class library does not provide.
/// </summary>
/// <remarks>
/// The framework answers whether an address is a loopback, a link-local or a unique-local address, but it has
/// never had a predicate for the private IPv4 ranges or for carrier-grade network address translation. Both
/// matter to the platform: whether a remote host is on the operator's own network decides whether a
/// self-signed certificate is plausible and whether a slow response is worth reporting, and an address behind
/// carrier-grade translation is one an operator cannot reach from outside no matter what they configure.
/// </remarks>
public static class IpAddressExtensions
{
    /// <summary>
    /// Number of bytes in an IPv4 address.
    /// </summary>
    private const int IPv4ByteCount = 4;

    /// <summary>
    /// Determines whether the address belongs to the machine itself or to a private network.
    /// </summary>
    /// <param name="address">The address to classify.</param>
    /// <returns>
    /// <see langword="true"/> for loopback, link-local and private-range addresses in either address family;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// An address written in the IPv4-mapped IPv6 form is unwrapped first, so the same host classifies the
    /// same way however it was spelled by the resolver that produced it.
    /// </remarks>
    public static bool IsLocalAddress(this IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        // Covers 127.0.0.0/8 and ::1 in one call.
        if (IPAddress.IsLoopback(candidate))
        {
            return true;
        }

        return candidate.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPrivateIPv4(candidate),
            AddressFamily.InterNetworkV6 =>
                candidate.IsIPv6LinkLocal || candidate.IsIPv6UniqueLocal || candidate.IsIPv6SiteLocal,
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether the address falls in the carrier-grade network address translation range,
    /// 100.64.0.0/10.
    /// </summary>
    /// <param name="address">The address to classify.</param>
    /// <returns>
    /// <see langword="true"/> when the address is in the shared translation range; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// An address in this range is neither private nor reachable from the public internet, so it is the one
    /// case where telling an operator to open a port cannot possibly help. As with
    /// <see cref="IsLocalAddress"/>, the IPv4-mapped IPv6 form is unwrapped first — the implementation this
    /// replaces did not, and so reported <see langword="false"/> for exactly the same host written the other
    /// way.
    /// </remarks>
    public static bool IsCarrierGradeNat(this IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (candidate.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        Span<byte> octets = stackalloc byte[IPv4ByteCount];

        if (!candidate.TryWriteBytes(octets, out var written) || written != IPv4ByteCount)
        {
            return false;
        }

        return octets[0] == 100 && octets[1] >= 64 && octets[1] <= 127;
    }

    /// <summary>
    /// Determines whether an IPv4 address is in one of the ranges reserved for private use or for
    /// self-assignment when no address server answered.
    /// </summary>
    /// <param name="address">The IPv4 address to classify.</param>
    /// <returns><see langword="true"/> when the address is not routable on the public internet.</returns>
    private static bool IsPrivateIPv4(IPAddress address)
    {
        Span<byte> octets = stackalloc byte[IPv4ByteCount];

        if (!address.TryWriteBytes(octets, out var written) || written != IPv4ByteCount)
        {
            return false;
        }

        // 10.0.0.0/8
        if (octets[0] == 10)
        {
            return true;
        }

        // 172.16.0.0/12
        if (octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31)
        {
            return true;
        }

        // 192.168.0.0/16
        if (octets[0] == 192 && octets[1] == 168)
        {
            return true;
        }

        // 169.254.0.0/16 — self-assigned because no address server answered.
        return octets[0] == 169 && octets[1] == 254;
    }
}
