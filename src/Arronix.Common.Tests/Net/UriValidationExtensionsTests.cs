using Arronix.Common.Net;

namespace Arronix.Common.Tests.Net;

/// <summary>
/// Covers URL validation, whose reason to exist is that it refuses the surrounding whitespace the framework
/// silently repairs.
/// </summary>
[TestFixture]
public class UriValidationExtensionsTests
{
    [TestCase("http://host.invalid/path")]
    [TestCase("https://host.invalid/path")]
    [TestCase("https://host.invalid:8443/path?query=1")]
    public void IsValidUrl_AcceptsAnAbsoluteUrl(string input)
    {
        Assert.That(input.IsValidUrl(), Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("/relative/path")]
    [TestCase("not a url")]
    [TestCase(null)]
    public void IsValidUrl_RejectsAnythingThatIsNotAnAbsoluteUrl(string? input)
    {
        Assert.That(input.IsValidUrl(), Is.False);
    }

    [TestCase(" https://host.invalid/path")]
    [TestCase("https://host.invalid/path ")]
    [TestCase("\thttps://host.invalid/path")]
    [TestCase("https://host.invalid/path\n")]
    public void IsValidUrl_RejectsSurroundingWhitespaceRatherThanTrimmingIt(string input)
    {
        // The framework's own parser trims and accepts, so a value pasted with a trailing space would be
        // stored as typed and then fail at connect time, with an error naming neither the setting nor the
        // space. Rejecting it here puts the error where it can be corrected.
        Assert.That(input.IsValidUrl(), Is.False);
    }
}
