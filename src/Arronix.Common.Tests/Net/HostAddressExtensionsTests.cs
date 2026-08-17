using Arronix.Common.Net;

namespace Arronix.Common.Tests.Net;

/// <summary>
/// Covers the textual host helpers, including the idempotence of authority bracketing.
/// </summary>
[TestFixture]
public class HostAddressExtensionsTests
{
    [TestCase("192.168.0.1")]
    [TestCase("::1")]
    [TestCase("2001:db8:4006:812::200e")]
    public void IsValidIpAddress_AcceptsAUnicastAddress(string input)
    {
        Assert.That(input.IsValidIpAddress(), Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not.an.address")]
    [TestCase("255.255.255.255")]
    [TestCase("ff02::1")]
    [TestCase(null)]
    public void IsValidIpAddress_RejectsAnythingElse(string? input)
    {
        Assert.That(input.IsValidIpAddress(), Is.False);
    }

    [TestCase("example.invalid", "example.invalid")]
    [TestCase("192.168.0.1", "192.168.0.1")]
    [TestCase("::1", "[::1]")]
    [TestCase("2001:db8::1", "[2001:db8::1]")]
    public void ToUrlHost_BracketsOnlyAnAddressThatNeedsIt(string input, string expected)
    {
        Assert.That(input.ToUrlHost(), Is.EqualTo(expected));
    }

    [Test]
    public void ToUrlHost_LeavesAnAlreadyBracketedAddressAlone()
    {
        // The implementation this replaces bracketed anything containing a colon unconditionally, so a
        // value that had passed through two layers of normalization came out as "[[::1]]".
        Assert.That("[::1]".ToUrlHost(), Is.EqualTo("[::1]"));
    }

    [Test]
    public void ToUrlHost_RejectsAMissingHost()
    {
        Assert.That(() => ((string)null!).ToUrlHost(), Throws.ArgumentNullException);
    }
}
