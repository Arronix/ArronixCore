using Arronix.Abstractions.Client;
using Arronix.Client.Contracts;
using FluentAssertions;

namespace Arronix.Client.Tests.Contracts;

/// <summary>
/// Which addresses this client writes into a document.
/// </summary>
/// <remarks>
/// A projected address becomes an <c>href</c> or a <c>src</c>, so the schemes are an allowlist and an
/// inline image has to be the format it claims. The controls matter as much as the refusals: a rule that
/// refuses everything is not a rule.
/// </remarks>
[TestFixture]
public sealed class BrowserAddressTests
{
    /// <summary>An 8×12 PNG; the same bytes the published fixture carries.</summary>
    private const string Png =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAgAAAAMCAIAAADQ/GvKAAAAEklEQVR42mNQcGjAihhGJdARABgLVAFPROX0AAAAAElFTkSuQmCC";

    /// <summary>A 1×1 GIF89a, whose base64 ends in no padding.</summary>
    private const string Gif =
        "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    [TestCase("https://example.test/poster.jpg")]
    [TestCase("http://example.test/poster.jpg")]
    public void AnAbsoluteWebAddressIsAccepted(string address)
    {
        using var _ = new FluentAssertions.Execution.AssertionScope();

        BrowserAddress.DescribeLink(new Uri(address)).Should().BeNull();
        BrowserAddress.DescribeArtwork(new Uri(address)).Should().BeNull();
    }

    [TestCase("javascript:alert(1)", "scheme")]
    [TestCase("file:///etc/passwd", "scheme")]
    [TestCase("blob:https://example.test/abc", "scheme")]
    [TestCase("https://user:secret@example.test/x", "user information")]
    [TestCase("data:image/png;base64,iVBORw0KGgo=", "scheme")]
    public void ALinkThatIsNotAnOrdinaryWebAddressIsRefused(string address, string because)
        => BrowserAddress.DescribeLink(new Uri(address))!.Should().Contain(because);

    [Test]
    public void ARelativeAddressIsNotAValueABrowserCanFollow()
    {
        var relative = new Uri("/poster.jpg", UriKind.Relative);

        using var _ = new FluentAssertions.Execution.AssertionScope();

        BrowserAddress.DescribeLink(relative)!.Should().Contain("relative");
        BrowserAddress.DescribeArtwork(relative)!.Should().Contain("relative");
        BrowserAddress.DescribeLink(null)!.Should().Contain("no address");
        BrowserAddress.DescribeArtwork(null)!.Should().Contain("no address");
    }

    /// <remarks>All four offered containers, so the rule admits what it says it admits.</remarks>
    [Test]
    public void AnInlineRasterImageOfEachOfferedTypeIsAccepted()
    {
        using var _ = new FluentAssertions.Execution.AssertionScope();

        BrowserAddress.DescribeArtwork(new Uri(Png)).Should().BeNull();
        BrowserAddress.DescribeArtwork(new Uri(Gif)).Should().BeNull();
        BrowserAddress.DescribeArtwork(new Uri(Jpeg())).Should().BeNull();
        BrowserAddress.DescribeArtwork(new Uri(WebP())).Should().BeNull();
    }

    /// <summary>A JFIF header: the SOI and APP0 markers a JPEG opens with.</summary>
    private static string Jpeg() => "data:image/jpeg;base64," + Convert.ToBase64String(
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
        0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9,
    ]);

    /// <summary>A RIFF container whose form is WEBP, which is the second signature the rule reads.</summary>
    private static string WebP() => "data:image/webp;base64," + Convert.ToBase64String(
        [.. "RIFF"u8.ToArray(), 0x1A, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8.ToArray(), 0x0E, 0x00, 0x00, 0x00]);

    /// <remarks>
    /// The refusals that matter: a document type inlined as an image is a document the browser parses, and a
    /// payload with no base64 marker is read as escaped text by the URL layer.
    /// </remarks>
    [TestCase("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=", "not one of the inline image types")]
    [TestCase("data:text/html;base64,PGgxPmhpPC9oMT4=", "not one of the inline image types")]
    [TestCase("data:image/png,iVBORw0KGgo", "not base64 encoded")]
    [TestCase("data:image/PNG;base64,iVBORw0KGgo=", "not one of the inline image types")]
    public void AnInlineImageThatIsNotAnAllowedRasterIsRefused(string address, string because)
        => BrowserAddress.DescribeArtwork(new Uri(address))!.Should().Contain(because);

    /// <remarks>
    /// A label is not a format. The bytes below decode cleanly and are an SVG document, a text file and a
    /// GIF; none of them is the PNG the address says it is.
    /// </remarks>
    [Test]
    public void AnInlineImageWhoseBytesAreNotTheFormatItClaimsIsRefused()
    {
        string Png64(string content) =>
            "data:image/png;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));

        using var _ = new FluentAssertions.Execution.AssertionScope();

        BrowserAddress.DescribeArtwork(new Uri(Png64("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")))!
            .Should().Contain("decodes to bytes that are not a PNG");

        BrowserAddress.DescribeArtwork(new Uri(Png64("plain text, not an image at all")))!
            .Should().Contain("decodes to bytes that are not a PNG");

        // A real GIF, labeled as a PNG.
        BrowserAddress.DescribeArtwork(new Uri("data:image/png;base64," + Gif["data:image/gif;base64,".Length..]))!
            .Should().Contain("not a PNG");

        // A RIFF container that is not a WebP.
        var riff = Convert.ToBase64String([.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "AVI "u8.ToArray()]);
        BrowserAddress.DescribeArtwork(new Uri("data:image/webp;base64," + riff))!
            .Should().Contain("not a WebP");
    }

    /// <remarks>
    /// Both padded forms are valid base64 and both must be accepted; reading "padding is at the end" as
    /// "the first '=' is the last character" refuses every two-character-padded payload, which is most of
    /// them.
    /// </remarks>
    [Test]
    public void BothPaddedFormsOfAValidPayloadAreAccepted()
    {
        var bytes = GifBytes();
        var paddedTwice = Convert.ToBase64String(bytes[..7]);
        var paddedOnce = Convert.ToBase64String(bytes[..8]);

        using var _ = new FluentAssertions.Execution.AssertionScope();

        paddedTwice.Should().EndWith("==");
        paddedOnce.Should().EndWith("=").And.NotEndWith("==");

        BrowserAddress.DescribeArtwork(new Uri("data:image/gif;base64," + paddedTwice)).Should().BeNull();
        BrowserAddress.DescribeArtwork(new Uri("data:image/gif;base64," + paddedOnce)).Should().BeNull();
    }

    [TestCase("data:image/png;base64,", "carries no payload")]
    [TestCase("data:image/png;base64,iVBOR", "not a whole number of encoded groups")]
    [TestCase("data:image/png;base64,iVB=Rw0K", "somewhere other than at its end")]
    [TestCase("data:image/png;base64,iVBO====", "padding characters")]
    [TestCase("data:image/png;base64,iVBO*w0K", "not a base64 character")]
    public void AMalformedBase64PayloadIsRefused(string address, string because)
        => BrowserAddress.DescribeArtwork(new Uri(address))!.Should().Contain(because);

    [Test]
    public void AnAddressPastTheSizeLimitIsRefused()
    {
        var long64 = new string('A', ClientContractLimits.MaxAddressLength);

        BrowserAddress.DescribeArtwork(new Uri("data:image/png;base64," + long64))!
            .Should().Contain("past the");
    }

    [TestCase("fixtures/g07/movie.json")]
    [TestCase("/fixtures/g07/movie.json")]
    public void APathOnThisHostIsAPayloadAddress(string address)
        => BrowserAddress.DescribeRequest(address).Should().BeNull();

    [TestCase("", "no payload address")]
    [TestCase("   ", "no payload address")]
    [TestCase("//evil.test/movie.json", "names another origin")]
    [TestCase("https://evil.test/movie.json", "not a path relative to this host")]
    [TestCase("..\\..\\movie.json", "backslash")]
    public void AnAddressThatIsNotAPathOnThisHostIsRefused(string address, string because)
        => BrowserAddress.DescribeRequest(address)!.Should().Contain(because);

    /// <summary>The 1×1 GIF's own bytes, for shaping partial payloads out of.</summary>
    private static byte[] GifBytes() =>
        Convert.FromBase64String(Gif["data:image/gif;base64,".Length..]);
}
