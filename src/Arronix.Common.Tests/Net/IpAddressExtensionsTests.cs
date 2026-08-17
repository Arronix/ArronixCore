using System.Net;
using Arronix.Common.Net;

namespace Arronix.Common.Tests.Net;

/// <summary>
/// Covers address classification, including the mapped-form handling that makes the same host classify the
/// same way however the resolver spelled it.
/// </summary>
[TestFixture]
public class IpAddressExtensionsTests
{
    [TestCase("::1")]
    [TestCase("127.0.0.1")]
    [TestCase("10.64.5.1")]
    [TestCase("172.16.0.1")]
    [TestCase("172.31.255.255")]
    [TestCase("192.168.5.1")]
    [TestCase("169.254.1.1")]
    [TestCase("fe80::1")]
    [TestCase("fd00::1")]
    public void IsLocalAddress_AcceptsLoopbackAndPrivateRanges(string address)
    {
        Assert.That(IPAddress.Parse(address).IsLocalAddress(), Is.True);
    }

    [TestCase("1.2.3.4")]
    [TestCase("172.15.0.1")]
    [TestCase("172.32.0.1")]
    [TestCase("192.55.0.1")]
    [TestCase("100.64.0.1")]
    [TestCase("100.127.255.254")]
    [TestCase("2001:db8::1")]
    public void IsLocalAddress_RejectsPublicAddresses(string address)
    {
        Assert.That(IPAddress.Parse(address).IsLocalAddress(), Is.False);
    }

    [Test]
    public void IsLocalAddress_UnwrapsTheMappedForm()
    {
        Assert.That(IPAddress.Parse("::ffff:192.168.1.1").IsLocalAddress(), Is.True);
        Assert.That(IPAddress.Parse("::ffff:1.2.3.4").IsLocalAddress(), Is.False);
    }

    [Test]
    public void IsLocalAddress_RejectsAMissingAddress()
    {
        Assert.That(() => ((IPAddress)null!).IsLocalAddress(), Throws.ArgumentNullException);
    }

    [TestCase("100.64.0.1")]
    [TestCase("100.100.100.100")]
    [TestCase("100.127.255.254")]
    public void IsCarrierGradeNat_AcceptsTheSharedRange(string address)
    {
        Assert.That(IPAddress.Parse(address).IsCarrierGradeNat(), Is.True);
    }

    [TestCase("1.2.3.4")]
    [TestCase("192.168.5.1")]
    [TestCase("100.63.255.255")]
    [TestCase("100.128.0.0")]
    [TestCase("2001:db8::1")]
    public void IsCarrierGradeNat_RejectsEverythingElse(string address)
    {
        Assert.That(IPAddress.Parse(address).IsCarrierGradeNat(), Is.False);
    }

    [Test]
    public void IsCarrierGradeNat_UnwrapsTheMappedForm()
    {
        // The implementation this replaces read the raw bytes, so the sixteen-byte mapped form never
        // matched and the same host was reported as publicly reachable when written the other way.
        Assert.That(IPAddress.Parse("::ffff:100.64.0.1").IsCarrierGradeNat(), Is.True);
    }
}
