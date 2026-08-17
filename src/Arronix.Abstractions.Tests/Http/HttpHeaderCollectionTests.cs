// Opts this file in to the experimental outbound HTTP contracts.
#pragma warning disable ARX0008

using System;
using System.Linq;
using System.Net;
using Arronix.Abstractions.Http;

namespace Arronix.Abstractions.Tests.Http;

[TestFixture]
public class HttpHeaderCollectionTests
{
    [Test]
    public void NamesAreMatchedWithoutRegardToCase()
    {
        var headers = new HttpHeaderCollection();
        headers.Set("Content-Type", "application/json");

        Assert.That(headers.Contains("content-type"), Is.True);
        Assert.That(headers.GetSingleValue("CONTENT-TYPE"), Is.EqualTo("application/json"));
    }

    [Test]
    public void RepeatedValuesArePreservedRatherThanJoined()
    {
        var headers = new HttpHeaderCollection();
        headers.Add("Set-Cookie", "a=1; Path=/");
        headers.Add("Set-Cookie", "b=2; Path=/");

        Assert.That(headers.GetValues("Set-Cookie"), Has.Count.EqualTo(2));
        Assert.That(headers.Count, Is.EqualTo(1));
    }

    [Test]
    public void SetReplacesEveryValue()
    {
        var headers = new HttpHeaderCollection();
        headers.Add("Accept", "text/xml");
        headers.Add("Accept", "application/json");
        headers.Set("Accept", "application/json");

        Assert.That(headers.GetValues("Accept"), Is.EqualTo(new[] { "application/json" }));
    }

    [Test]
    public void GetSingleValueRejectsARepeatedHeaderInsteadOfPickingOne()
    {
        var headers = new HttpHeaderCollection();
        headers.Add("Warning", "one");
        headers.Add("Warning", "two");

        Assert.That(() => headers.GetSingleValue("Warning"), Throws.InvalidOperationException);
    }

    [Test]
    public void MissingHeadersReadAsNullOrEmptyRatherThanThrowing()
    {
        var headers = new HttpHeaderCollection();

        Assert.That(headers.GetSingleValue("Accept"), Is.Null);
        Assert.That(headers.GetValues("Accept"), Is.Empty);
        Assert.That(headers.ContentLength, Is.Null);
    }

    [Test]
    public void ContentLengthParsesInvariantlyAndRemovesOnNull()
    {
        var headers = new HttpHeaderCollection
        {
            ContentLength = 1234
        };

        Assert.That(headers.GetSingleValue("Content-Length"), Is.EqualTo("1234"));
        Assert.That(headers.ContentLength, Is.EqualTo(1234));

        headers.ContentLength = null;

        Assert.That(headers.Contains("Content-Length"), Is.False);
    }

    [Test]
    public void EnumerationYieldsOneEntryPerNameWithAllItsValues()
    {
        var headers = new HttpHeaderCollection();
        headers.Add("Set-Cookie", "a=1");
        headers.Add("Set-Cookie", "b=2");
        headers.Set("Accept", "application/json");

        var entries = headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        Assert.That(entries, Has.Count.EqualTo(2));
        Assert.That(entries["Set-Cookie"], Has.Count.EqualTo(2));
    }
}

[TestFixture]
public class OutboundHttpResponseTests
{
    [Test]
    public void CookiesAreParsedByTheFrameworkSoAttributesAreNotMistakenForCookies()
    {
        var request = new OutboundHttpRequest(new Uri("https://example.invalid/api"));
        var headers = new HttpHeaderCollection();
        headers.Add("Set-Cookie", "session=abc123; Path=/; HttpOnly");
        headers.Add("Set-Cookie", "theme=dark; Path=/");

        var response = new OutboundHttpResponse(request, headers, HttpStatusCode.OK, ReadOnlyMemory<byte>.Empty);
        var cookies = response.GetCookies();

        Assert.That(cookies["session"], Is.EqualTo("abc123"));
        Assert.That(cookies["theme"], Is.EqualTo("dark"));
        Assert.That(cookies.ContainsKey("Path"), Is.False);
        Assert.That(cookies.ContainsKey("HttpOnly"), Is.False);
    }

    [Test]
    public void BodyIsDecodedUsingTheCharsetFromContentType()
    {
        var request = new OutboundHttpRequest(new Uri("https://example.invalid/api"));
        var headers = new HttpHeaderCollection
        {
            ContentType = "application/json; charset=utf-8"
        };

        var response = new OutboundHttpResponse(
            request,
            headers,
            HttpStatusCode.OK,
            System.Text.Encoding.UTF8.GetBytes("{\"value\":\"café\"}"));

        Assert.That(response.Content, Is.EqualTo("{\"value\":\"café\"}"));
    }

    [Test]
    public void AnUnknownCharsetFallsBackToUtf8InsteadOfThrowing()
    {
        var request = new OutboundHttpRequest(new Uri("https://example.invalid/api"));
        var headers = new HttpHeaderCollection
        {
            ContentType = "text/plain; charset=not-a-real-charset"
        };

        var response = new OutboundHttpResponse(
            request,
            headers,
            HttpStatusCode.OK,
            System.Text.Encoding.UTF8.GetBytes("ok"));

        Assert.That(response.Content, Is.EqualTo("ok"));
    }
}

[TestFixture]
public class HttpRateLimitedExceptionTests
{
    [Test]
    public void RetryAfterInSecondsIsRead()
    {
        var headers = new HttpHeaderCollection();
        headers.Set("Retry-After", "120");

        var exception = new HttpRateLimitedException(BuildResponse(headers));

        Assert.That(exception.RetryAfterDelta, Is.EqualTo(TimeSpan.FromSeconds(120)));
        Assert.That(exception.GetRetryAfter(DateTimeOffset.UnixEpoch), Is.EqualTo(TimeSpan.FromSeconds(120)));
    }

    [Test]
    public void RetryAfterAsAnHttpDateIsReadWithoutConsultingTheAmbientCulture()
    {
        var headers = new HttpHeaderCollection();
        headers.Set("Retry-After", "Wed, 21 Oct 2026 07:28:00 GMT");

        var exception = new HttpRateLimitedException(BuildResponse(headers));
        var now = new DateTimeOffset(2026, 10, 21, 7, 27, 0, TimeSpan.Zero);

        Assert.That(exception.RetryAfterDate, Is.Not.Null);
        Assert.That(exception.GetRetryAfter(now), Is.EqualTo(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public void APastRetryAfterDateClampsToZeroRatherThanGoingNegative()
    {
        var headers = new HttpHeaderCollection();
        headers.Set("Retry-After", "Wed, 21 Oct 2026 07:28:00 GMT");

        var exception = new HttpRateLimitedException(BuildResponse(headers));
        var now = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

        Assert.That(exception.GetRetryAfter(now), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void NoRetryAfterLeavesTheDecisionToTheCaller()
    {
        var exception = new HttpRateLimitedException(BuildResponse(new HttpHeaderCollection()));

        Assert.That(exception.GetRetryAfter(DateTimeOffset.UnixEpoch), Is.Null);
        Assert.That(exception.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    private static OutboundHttpResponse BuildResponse(HttpHeaderCollection headers) => new(
        new OutboundHttpRequest(new Uri("https://example.invalid/api")),
        headers,
        HttpStatusCode.TooManyRequests,
        ReadOnlyMemory<byte>.Empty);
}
