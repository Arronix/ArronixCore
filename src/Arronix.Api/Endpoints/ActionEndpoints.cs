using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Host.Intent;
using Arronix.Host.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;


namespace Arronix.Api.Endpoints;

/// <summary>
/// Doing the things a media kind said could be done to it.
/// </summary>
/// <remarks>
/// <para>
/// There is one route for every action of every kind, because the set of actions is not known at build
/// time — it is whatever the loaded extensions declared. What this file contributes is the part that is
/// the same for all of them: check that the action exists, check that the caller supplied what the
/// declaration says is required, and answer differently depending on whether the work finishes inside the
/// request or outlives it.
/// </para>
/// <para>
/// That last distinction is taken from the declaration rather than guessed. An action that says it is
/// long-running is accepted and answered with a correlation identifier the caller can follow on the event
/// stream; anything else is answered with its result. The alternative — holding the connection open for a
/// library refresh — is how an interface ends up with a spinner nobody can cancel.
/// </para>
/// Execution is capability-based. Host-owned operations such as changing monitoring state execute here;
/// an operation whose scheduler, catalog, filesystem or exclusion capability has not been built returns
/// 501 rather than being accepted and discarded.
/// </remarks>
internal static class ActionEndpoints
{
    /// <summary>The request header a caller may use to supply its own correlation identifier.</summary>
    internal const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>
    /// Maps the action route.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapActionEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/kinds/{kind}/actions/{actionId}", Invoke)
            .WithTags("Library")
            .WithName("InvokeAction")
            .WithSummary("Performs one of a media kind's declared actions.")
            .WithDescription(
                "Checks the request against the action's declaration and dispatches it through the installed "
                + "platform capability. Answers 501 when that capability is not yet installed.");

        return group;
    }

    private static async Task<Results<Ok<ActionResult>, Accepted<ActionResult>, ProblemHttpResult>> Invoke(
        string kind,
        string actionId,
        ActionRequest? request,
        IMediaKindRegistry registry,
        IIntentRegistry intents,
        IStandardActionDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(intents);
        ArgumentNullException.ThrowIfNull(dispatcher);

        if (!registry.TryGet(MediaKindId.FromString(kind), out var registered) || registered is null)
        {
            return ApiRequests.UnknownKind(kind);
        }

        if (intents.FindAction(registered.Kind, actionId) is not { } action)
        {
            return ApiRequests.Problem(
                StatusCodes.Status404NotFound,
                CoreErrorCode.MediaKindNotFound,
                $"'{kind}' declares no action '{actionId}'.");
        }

        var body = request ?? ActionRequest.Empty;
        foreach (var reference in body.Items)
        {
            if (reference.Kind != registered.Kind)
            {
                return ApiRequests.Problem(
                    StatusCodes.Status400BadRequest,
                    CoreErrorCode.MediaItemNotFound,
                    $"'{reference}' belongs to '{reference.Kind}', not '{registered.Kind}'.");
            }
        }

        if (Validate(action, body, body.Items) is { IsValid: false } invalid)
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                string.Join(" ", invalid.Failures.Select(static failure => failure.Message)));
        }

        var result = await dispatcher.TryDispatchAsync(
            action,
            body.Items,
            body.Parameters,
            cancellationToken).ConfigureAwait(false);

        if (result is not null)
        {
            return action.LongRunning
                ? TypedResults.Accepted((string?)null, result)
                : TypedResults.Ok(result);
        }

        return ApiRequests.Problem(
            StatusCodes.Status501NotImplemented,
            CoreErrorCode.Unknown,
            $"'{action.StandardAction}' is a standard media operation, but this host does not yet have "
            + "the execution capability it requires.");
    }

    /// <summary>
    /// Checks the request against what the action's own declaration says it needs.
    /// </summary>
    /// <remarks>
    /// The declaration is the only source of truth for this: nothing here has a list of action names or
    /// knows what any of them do. A required parameter that is missing is caught by comparing the request
    /// to the same data the client used to build the form.
    /// </remarks>
    private static ValidationOutcome Validate(
        ActionDescriptor action,
        ActionRequest request,
        IReadOnlyList<MediaItemRef> items)
    {
        var failures = new List<ValidationFailure>();

        var needsItems = action.Scope is ActionScope.Item or ActionScope.Selection;
        if (needsItems && items.Count == 0)
        {
            failures.Add(new ValidationFailure(
                null,
                $"'{action.ActionId}' applies to items and no item was supplied."));
        }

        if (action.Scope == ActionScope.Item && items.Count > 1)
        {
            failures.Add(new ValidationFailure(
                null,
                $"'{action.ActionId}' applies to a single item and {items.Count} were supplied."));
        }

        if (action.TargetLevelId is { } target)
        {
            foreach (var item in items.Where(item => item.Level != target))
            {
                failures.Add(new ValidationFailure(
                    null,
                    $"'{action.ActionId}' applies to '{target}' and '{ApiRequests.ToPathSegment(item)}' is not one."));
            }
        }

        foreach (var parameter in action.Parameters.Where(static parameter => parameter.Required))
        {
            if (!request.Parameters.TryGetValue(parameter.ParameterId, out var value) || string.IsNullOrWhiteSpace(value))
            {
                failures.Add(new ValidationFailure(
                    parameter.ParameterId,
                    $"'{parameter.Name}' is required."));
            }
        }

        foreach (var (parameterId, value) in request.Parameters)
        {
            var parameter = action.Parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.ParameterId, parameterId, StringComparison.Ordinal));
            if (parameter is null)
            {
                failures.Add(new ValidationFailure(parameterId, $"'{parameterId}' is not a parameter of this action."));
                continue;
            }

            var valid = parameter.ValueKind switch
            {
                FieldValueKind.Boolean => bool.TryParse(value, out _),
                FieldValueKind.Decimal => double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _),
                FieldValueKind.Enumerated => parameter.Choices.Any(choice =>
                    string.Equals(choice.Value, value, StringComparison.Ordinal)),
                FieldValueKind.ExternalIdentifier => ExternalId.TryParse(value, out _),
                _ => true
            };

            if (!valid)
            {
                failures.Add(new ValidationFailure(parameterId, $"'{value}' is not valid for '{parameter.Name}'."));
            }
        }

        return failures.Count == 0 ? ValidationOutcome.Success : new ValidationOutcome { Failures = failures };
    }
}
