
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Wire;
using Arronix.Client.Rendering;

namespace Arronix.Client.Services;

/// <summary>
/// Invokes declared actions, and says how the user should be asked first.
/// </summary>
/// <remarks>
/// <para>
/// Views ask this rather than the request client directly, so that the rule about asking before acting is
/// applied in one place. A view that assembled its own request could forget to ask, and the actions worth
/// asking about are exactly the ones nobody notices until they have run.
/// </para>
/// <para>
/// It reports what it invoked so that a feed can show a request that was accepted but has not finished.
/// Acceptance and completion are different things everywhere in this platform, and the client is where
/// that difference is visible to a person.
/// </para>
/// </remarks>
public sealed class ActionDispatcher
{
    private readonly ArronixApiClient _api;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionDispatcher"/> class.
    /// </summary>
    /// <param name="api">The client used to invoke actions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="api"/> is <see langword="null"/>.</exception>
    public ActionDispatcher(ArronixApiClient api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    /// <summary>
    /// Occurs when an action has been invoked and the platform has answered.
    /// </summary>
    public event EventHandler<ActionDispatched>? Dispatched;

    /// <summary>
    /// Gets how the user must be asked before an action runs.
    /// </summary>
    /// <param name="action">The declared action.</param>
    /// <returns>What this client will do before invoking.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    public static ConfirmationPolicy PolicyFor(ActionDescriptor action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ConsequenceMap.For(action.Consequence, action.Confirmation);
    }

    /// <summary>
    /// Invokes an action. The caller is responsible for having asked, per <see cref="PolicyFor"/>.
    /// </summary>
    /// <param name="kind">The media kind.</param>
    /// <param name="action">The declared action.</param>
    /// <param name="request">What the action is being done to.</param>
    /// <param name="cancellationToken">Abandons the request.</param>
    /// <returns>Whether the platform took the request on.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public async Task<ActionResult> InvokeAsync(
        MediaKindId kind,
        ActionDescriptor action,
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(request);

        var result = await _api.InvokeActionAsync(kind, action.ActionId, request, cancellationToken)
            .ConfigureAwait(false);

        Dispatched?.Invoke(this, new ActionDispatched(kind, action, result));
        return result;
    }
}

/// <summary>
/// One action that has been invoked, and what the platform answered.
/// </summary>
/// <param name="Kind">The media kind it was invoked on.</param>
/// <param name="Action">The declared action.</param>
/// <param name="Result">What the platform answered.</param>
public sealed record ActionDispatched(MediaKindId Kind, ActionDescriptor Action, ActionResult Result);
