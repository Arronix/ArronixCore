using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Xml;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Http;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Scheduling;

// Error, HTTP and plugin contracts are experimental; classification reads all three.
#pragma warning disable ARX0003
#pragma warning disable ARX0008
#pragma warning disable ARX0014

namespace Arronix.Host.Scheduling;

/// <summary>
/// Decides what a failed piece of work was, so the scheduler knows whether to try it again.
/// </summary>
/// <remarks>
/// <para>
/// A job classifies its own failure by writing a published key into the result record's bag rather than
/// setting a typed member, and that is a statement about the vocabulary rather than about the record. The
/// five classes below are inferred from four surveyed applications; putting them in the contract assembly
/// would fix a guess in the place it is most expensive to be wrong, so this type owns them until a real job
/// disagrees with them. Everything the platform itself supplies to a run is a typed member of the run's
/// context — nothing rides a string key because a record could not be changed.
/// </para>
/// <para>
/// A job that says nothing is classified from the exception instead, which covers every case a first-party
/// job actually produces. The two paths are ordered rather than merged: what a job says about its own
/// failure beats what the host can infer, because the job knows whether the remote's five hundred meant
/// "come back later" or "this will never work".
/// </para>
/// </remarks>
/// <param name="clock">The clock a remote's absolute retry deadline is measured against.</param>
public sealed class FailureClassifier(TimeProvider clock)
{
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Classifies a failure.
    /// </summary>
    /// <param name="result">The job's own result, when it returned one.</param>
    /// <param name="exception">The exception it threw, when it threw one.</param>
    /// <returns>The classification and any wait the failure itself asked for.</returns>
    public FailureOutcome Classify(JobExecutionResult? result, Exception? exception)
    {
        if (result is not null && TryReadDeclared(result, out var declared))
        {
            return declared;
        }

        return exception switch
        {
            null => new FailureOutcome(FailureClass.Permanent, null),

            // The remote's own answer, in preference to the ladder.
            HttpRateLimitedException limited => new FailureOutcome(
                FailureClass.RateLimited,
                limited.GetRetryAfter(_clock.GetUtcNow())),

            HttpGatewayException gateway => new FailureOutcome(ClassifyStatus(gateway.StatusCode), null),

            OperationCanceledException => new FailureOutcome(FailureClass.Canceled, null),

            ArronixException arronix => new FailureOutcome(ClassifyCode(arronix.ErrorCode), null),

            _ => new FailureOutcome(FailureClass.Permanent, null),
        };
    }

    private static FailureClass ClassifyStatus(HttpStatusCode? status)
    {
        var code = status is null ? 0 : (int)status;

        // A server error is the remote's problem and usually passes; a client error is the request's problem
        // and never does. The one exception, too-many-requests, is a separate exception type.
        return code >= 500 ? FailureClass.Transient
            : code >= 400 ? FailureClass.Permanent
            : FailureClass.Transient;
    }

    private static FailureClass ClassifyCode(CoreErrorCode code) => code switch
    {
        CoreErrorCode.InvalidConfiguration or CoreErrorCode.MissingConfiguration => FailureClass.Configuration,

        // Connection failures against a release source, a transfer client or a catalog are the archetypal
        // transient fault, and they are exactly what the surveyed escalation ladders were built for.
        CoreErrorCode.IndexerConnectionFailed
            or CoreErrorCode.DownloaderConnectionFailed
            or CoreErrorCode.CatalogerConnectionFailed => FailureClass.Transient,

        // Anything in the extension-loading band means the extension is not going to start working on its
        // own, so retrying its job is pointless until an operator intervenes.
        >= CoreErrorCode.PluginLoadFailure and <= CoreErrorCode.PluginDisabled => FailureClass.Configuration,

        _ => FailureClass.Permanent,
    };

    private static bool TryReadDeclared(JobExecutionResult result, out FailureOutcome outcome)
    {
        outcome = default;

        if (result.ResultData is not { } data
            || !data.TryGetValue(WellKnownJobResults.FailureClass, out var raw)
            || raw is not string text
            || !TryParseClass(text, out var failureClass))
        {
            return false;
        }

        TimeSpan? retryAfter = null;

        if (data.TryGetValue(WellKnownJobResults.RetryAfter, out var rawRetry)
            && rawRetry is string retryText
            && TryParseDuration(retryText, out var parsed))
        {
            retryAfter = parsed;
        }

        outcome = new FailureOutcome(failureClass, retryAfter);
        return true;
    }

    private static bool TryParseClass(string text, out FailureClass failureClass)
    {
        switch (text.Trim().ToUpperInvariant())
        {
            case "PERMANENT":
                failureClass = FailureClass.Permanent;
                return true;
            case "TRANSIENT":
                failureClass = FailureClass.Transient;
                return true;
            case "RATE-LIMITED":
                failureClass = FailureClass.RateLimited;
                return true;
            case "CONFIGURATION":
                failureClass = FailureClass.Configuration;
                return true;
            case "CANCELED":
                failureClass = FailureClass.Canceled;
                return true;
            default:
                failureClass = default;
                return false;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The ISO-8601 duration reader signals every malformed input by throwing several different types, all of which mean the same thing here: the job did not state a usable wait.")]
    private static bool TryParseDuration(string text, out TimeSpan duration)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith('P') || trimmed.StartsWith('p'))
        {
            try
            {
                duration = XmlConvert.ToTimeSpan(trimmed.ToUpperInvariant());
                return duration >= TimeSpan.Zero;
            }
            catch (Exception)
            {
                duration = default;
                return false;
            }
        }

        return TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out duration);
    }
}
