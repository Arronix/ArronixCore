using System.Globalization;
using System.Net;
using System.Threading;
using Arronix.Common.Net;

namespace Arronix.Common.Tests.Net;

/// <summary>
/// Covers endpoint rendering, including the bracketing that makes an IPv6 authority readable back.
/// </summary>
[TestFixture]
public class DnsEndPointExtensionsTests
{
    [Test]
    public void HostAndPort_RendersANamedHost()
    {
        var endPoint = new DnsEndPoint("host.invalid", 8080);

        Assert.That(endPoint.HostAndPort, Is.EqualTo("host.invalid:8080"));
    }

    [Test]
    public void HostAndPort_RendersAnIPv4Literal()
    {
        var endPoint = new DnsEndPoint("192.168.0.1", 443);

        Assert.That(endPoint.HostAndPort, Is.EqualTo("192.168.0.1:443"));
    }

    [Test]
    public void HostAndPort_BracketsAnIPv6Literal()
    {
        // Without the brackets the host's own colons and the port separator are indistinguishable, so a
        // connection log line records something nobody can read back.
        var endPoint = new DnsEndPoint("2001:db8::1", 443);

        Assert.That(endPoint.HostAndPort, Is.EqualTo("[2001:db8::1]:443"));
    }

    [Test]
    public void HostAndPort_FormatsThePortInvariantlyRegardlessOfTheHostCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var endPoint = new DnsEndPoint("host.invalid", 65535);

            Assert.That(endPoint.HostAndPort, Is.EqualTo("host.invalid:65535"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
