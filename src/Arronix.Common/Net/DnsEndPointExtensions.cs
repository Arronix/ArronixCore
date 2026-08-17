using System.Globalization;
using System.Net;

namespace Arronix.Common.Net;

/// <summary>
/// Presentation helpers for <see cref="DnsEndPoint"/>.
/// </summary>
/// <remarks>
/// Written with C# extension members rather than extension methods, which is the house style for new
/// extensions in this assembly: the member reads as a property of the endpoint at the call site, which is
/// what it is, instead of as a function that happens to take one.
/// </remarks>
public static class DnsEndPointExtensions
{
    extension(DnsEndPoint endPoint)
    {
        /// <summary>
        /// Gets the endpoint rendered as an authority — host and port separated by a colon, with an IPv6
        /// literal bracketed.
        /// </summary>
        /// <remarks>
        /// The bracketing is what makes the result unambiguous. An IPv6 host already contains colons, so
        /// without it the rendered form cannot be read back, and a connection log line becomes guesswork
        /// exactly when someone is trying to work out which address was tried.
        /// </remarks>
        public string HostAndPort
        {
            get
            {
                ArgumentNullException.ThrowIfNull(endPoint);

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1}",
                    endPoint.Host.ToUrlHost(),
                    endPoint.Port);
            }
        }
    }
}
