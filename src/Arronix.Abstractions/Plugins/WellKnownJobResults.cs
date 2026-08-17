using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Plugins;

/// <summary>
/// The keys the platform reserves in a job's result bag.
/// </summary>
/// <remarks>
/// <para>
/// A job that knows why it failed says so here, and the platform's retry policy believes it. A job that
/// says nothing is classified by the platform from the failure it threw.
/// </para>
/// <para>
/// These are string keys because the vocabulary is not settled, not because the contract cannot change.
/// The five classes are inferred from four surveyed applications and no Arronix job has yet disagreed
/// with them; promoting the enumeration to the contract assembly would fix a guess in the one place it is
/// most expensive to be wrong. The classifier that reads these keys owns the vocabulary until a job
/// exists that the vocabulary fails, at which point the enumeration is promoted and these keys are
/// deleted.
/// </para>
/// <para>
/// Everything the *platform* supplies to or learns from a run is a typed member on
/// <see cref="Arronix.Abstractions.Scheduling.JobExecutionContext"/>. Nothing rides a published string key
/// because a record could not be changed.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Plugins, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class WellKnownJobResults
{
    /// <summary>
    /// How the failure should be classified. The value is one of <c>permanent</c>, <c>transient</c>,
    /// <c>rate-limited</c>, <c>configuration</c> or <c>canceled</c>.
    /// </summary>
    public const string FailureClass = "arronix.failure-class";

    /// <summary>
    /// The earliest the work should be retried, as a duration. The value is an ISO-8601 duration string.
    /// </summary>
    public const string RetryAfter = "arronix.retry-after";
}
