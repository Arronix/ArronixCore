using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Providers;

namespace Arronix.Abstractions.Wire;

/// <summary>
/// What came of asking the platform to do something.
/// </summary>
/// <param name="Accepted">Whether the platform took the request on.</param>
/// <param name="CorrelationId">The identifier to follow the work by, for anything long-running.</param>
/// <param name="Message">What to tell the user.</param>
/// <param name="Validation">What was wrong with the request, when something was.</param>
/// <remarks>
/// Acceptance and completion are deliberately different things. Most of what a user asks for outlives the
/// request that starts it, and reporting "done" when the work has only been queued is how a consumer ends
/// up showing a stale answer for as long as the work takes.
/// </remarks>
[Experimental(ExperimentalContracts.Wire, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ActionResult(
    bool Accepted,
    string? CorrelationId,
    string? Message,
    ValidationOutcome? Validation);
