using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Scheduling;
using Arronix.Host.Scheduling;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

// Error, HTTP and plugin contracts are experimental.
#pragma warning disable ARX0003
#pragma warning disable ARX0008
#pragma warning disable ARX0014

namespace Arronix.Host.Tests.Scheduling;

/// <summary>
/// What a failure was, and therefore whether it is worth trying again.
/// </summary>
/// <remarks>
/// The precedence assertion is the one that matters: what a job says about its own failure beats what the
/// host can infer from an exception, because the job knows whether a remote's five hundred meant "come back
/// later" or "this will never work".
/// </remarks>
[TestFixture]
internal sealed class FailureClassifierTests
{
    private static FailureClassifier Classifier() => new(new FakeTimeProvider(DateTimeOffset.UnixEpoch));

    private static JobExecutionResult Declaring(string failureClass, string? retryAfter = null)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [WellKnownJobResults.FailureClass] = failureClass,
        };

        if (retryAfter is not null)
        {
            data[WellKnownJobResults.RetryAfter] = retryAfter;
        }

        return new JobExecutionResult(false, "it failed", data);
    }

    [TestCase("permanent", FailureClass.Permanent)]
    [TestCase("transient", FailureClass.Transient)]
    [TestCase("rate-limited", FailureClass.RateLimited)]
    [TestCase("configuration", FailureClass.Configuration)]
    [TestCase("canceled", FailureClass.Canceled)]
    [TestCase("  TRANSIENT  ", FailureClass.Transient)]
    public void AJobClassifiesItsOwnFailureThroughThePublishedKey(string declared, FailureClass expected)
        => Classifier().Classify(Declaring(declared), null).Class.Should().Be(expected);

    [Test]
    public void AJobsOwnClassificationBeatsTheExceptionItAlsoThrew()
        => Classifier()
            .Classify(Declaring("transient"), new ArronixException(CoreErrorCode.Unknown, "boom"))
            .Class.Should().Be(FailureClass.Transient);

    [Test]
    public void AnUnrecognizedDeclarationFallsThroughToTheException()
        => Classifier()
            .Classify(Declaring("catastrophic"), new OperationCanceledException())
            .Class.Should().Be(FailureClass.Canceled);

    [TestCase("PT30S", 30)]
    [TestCase("PT2M", 120)]
    [TestCase("00:00:45", 45)]
    public void AJobMayAlsoStateHowLongToWait(string declared, int expectedSeconds)
        => Classifier()
            .Classify(Declaring("transient", declared), null)
            .RetryAfter.Should().Be(TimeSpan.FromSeconds(expectedSeconds));

    [Test]
    public void AMalformedWaitIsIgnoredRatherThanRefused()
        => Classifier().Classify(Declaring("transient", "soon"), null).RetryAfter.Should().BeNull();

    [Test]
    public void ARemotesOwnRetryDeadlineIsHonoredOverTheLadder()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var classifier = new FailureClassifier(clock);
        var response = OutboundResponse(System.Net.HttpStatusCode.TooManyRequests, retryAfterSeconds: 90);

        var outcome = classifier.Classify(null, new HttpRateLimitedException(response));

        outcome.Class.Should().Be(FailureClass.RateLimited);
        outcome.RetryAfter.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Test]
    public void AServerErrorIsTransientAndAClientErrorIsPermanent()
    {
        var classifier = Classifier();

        classifier.Classify(null, new HttpGatewayException(
            OutboundResponse(System.Net.HttpStatusCode.InternalServerError))).Class
            .Should().Be(FailureClass.Transient);

        classifier.Classify(null, new HttpGatewayException(
            OutboundResponse(System.Net.HttpStatusCode.NotFound))).Class
            .Should().Be(FailureClass.Permanent);
    }

    [Test]
    public void CancellationIsNotAFailureOfTheWork()
        => Classifier().Classify(null, new OperationCanceledException()).Class
            .Should().Be(FailureClass.Canceled);

    [TestCase(CoreErrorCode.InvalidConfiguration, FailureClass.Configuration)]
    [TestCase(CoreErrorCode.MissingConfiguration, FailureClass.Configuration)]
    [TestCase(CoreErrorCode.IndexerConnectionFailed, FailureClass.Transient)]
    [TestCase(CoreErrorCode.DownloaderConnectionFailed, FailureClass.Transient)]
    [TestCase(CoreErrorCode.CatalogerConnectionFailed, FailureClass.Transient)]
    [TestCase(CoreErrorCode.PluginLoadFailure, FailureClass.Configuration)]
    [TestCase(CoreErrorCode.PluginDisabled, FailureClass.Configuration)]
    [TestCase(CoreErrorCode.ParsingFailed, FailureClass.Permanent)]
    public void APlatformFailureIsClassifiedByItsCode(CoreErrorCode code, FailureClass expected)
        => Classifier().Classify(null, new ArronixException(code, "failed")).Class.Should().Be(expected);

    [Test]
    public void AnythingElseIsPermanentBecauseRetryingWhatIsNotUnderstoodIsGuessing()
        => Classifier().Classify(null, new InvalidOperationException("who knows")).Class
            .Should().Be(FailureClass.Permanent);

    [Test]
    public void AFailureWithNeitherResultNorExceptionIsPermanent()
        => Classifier().Classify(null, null).Class.Should().Be(FailureClass.Permanent);

    private static OutboundHttpResponse OutboundResponse(
        System.Net.HttpStatusCode status,
        int? retryAfterSeconds = null)
    {
        var headers = new HttpHeaderCollection();

        if (retryAfterSeconds is { } seconds)
        {
            headers.Set("Retry-After", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var request = new OutboundHttpRequest(new Uri("https://example.invalid/thing"));

        return new OutboundHttpResponse(request, headers, status, ReadOnlyMemory<byte>.Empty);
    }
}
