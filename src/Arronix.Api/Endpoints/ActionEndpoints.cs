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

#pragma warning disable ARX0006 // Health contracts are experimental; error codes are quoted in problem documents.
#pragma warning disable ARX0013 // Shape contracts are experimental; item references are parsed from them.
#pragma warning disable ARX0015 // Provider contracts are experimental; validation outcomes are returned.
#pragma warning disable ARX0016 // Intent contracts are experimental; this assembly dispatches through them.
#pragma warning disable ARX0017 // Wire contracts are experimental; this assembly publishes them.

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
/// <para>
/// The dispatch itself is missing, and the route says so with a 501 rather than pretending otherwise. No
/// contract an extension implements has a method that performs a declared action, so the platform can state
/// that an action exists and check a request against it, and can do nothing else with it. See the note in
/// <see cref="Invoke"/> for what closing that would take.
/// </para>
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
                "Checks the request against the action's declaration and answers 501 while no extension seam "
                + "exists to perform one. Answers 202 when the declaration says the action outlives the "
                + "request, and 200 otherwise, once there is.");

        return group;
    }

    private static Results<Ok<ActionResult>, Accepted<ActionResult>, ProblemHttpResult> Invoke(
        string kind,
        string actionId,
        ActionRequest? request,
        IMediaKindRegistry registry,
        IIntentRegistry intents)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(intents);

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
        var items = new List<MediaItemRef>(body.Items.Count);

        foreach (var text in body.Items)
        {
            if (!ApiRequests.TryParseItemRef(registered.Kind, text, out var reference))
            {
                return ApiRequests.Problem(
                    StatusCodes.Status400BadRequest,
                    CoreErrorCode.MediaItemNotFound,
                    $"'{text}' is not a well-formed item reference; the form is 'level:id'.");
            }

            items.Add(reference);
        }

        if (Validate(action, body, items) is { IsValid: false } invalid)
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                string.Join(" ", invalid.Failures.Select(static failure => failure.Message)));
        }

        // TODO: dispatch the action. There is no seam to dispatch it through. An extension contributes its
        // catalog through IMediaItemSource, and that contract has exactly two write methods — ProposeAsync
        // and CommitAsync, which serve the declared working surface. It has nothing that performs a declared
        // action, and neither IIntentRegistry nor any other host type supplies one, so a declaration like
        // 'refresh' or 'monitor.set' can be published, validated and refused, but not carried out. Closing
        // this needs a new contract member in Arronix.Abstractions (IMediaItemSource, or an action-handling
        // seam beside it) and a host-side dispatcher that resolves the descriptor, applies the confirmation
        // and consequence rules, and queues the long-running ones onto the scheduler rather than running
        // them inside the request. Everything above this line is the half that does not depend on it: the
        // kind, the action, the item references and the declared parameters have all been checked, so a
        // caller learns what is wrong with its request before it learns that the platform cannot yet act.
        return ApiRequests.Problem(
            StatusCodes.Status501NotImplemented,
            CoreErrorCode.Unknown,
            $"'{kind}' declares the action '{actionId}' and the request for it is well-formed, but no "
            + "extension seam exists to perform a declared action, so nothing can carry it out yet.");
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

        return failures.Count == 0 ? ValidationOutcome.Success : new ValidationOutcome { Failures = failures };
    }
}

/// <summary>
/// What a caller sends to perform an action.
/// </summary>
/// <remarks>
/// This lives here rather than in the shared contract assembly because the contract assembly has a result
/// type for an action and no request type for one — an asymmetry worth recording, since it means a client
/// has to reproduce this shape rather than being handed it. Promoting it is additive and costs nothing; it
/// is left alone in this milestone rather than editing a contract area another work package owns.
/// </remarks>
/// <param name="Items">The items to act on, each as <c>level:id</c>.</param>
/// <param name="Parameters">The values the action's declared parameters were filled in with.</param>
internal sealed record ActionRequest(
    IReadOnlyList<string> Items,
    IReadOnlyDictionary<string, string> Parameters)
{
    /// <summary>An action invoked with no items and no parameters.</summary>
    internal static ActionRequest Empty { get; } =
        new([], new Dictionary<string, string>(StringComparer.Ordinal));
}
