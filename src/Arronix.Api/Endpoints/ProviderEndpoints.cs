using System.Linq;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Shape;
using Arronix.Api.Security;
using Arronix.Common.Diagnostics;
using Arronix.Host.Media;
using Arronix.Host.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;


namespace Arronix.Api.Endpoints;

/// <summary>
/// Everything a person configures: which indexers, download clients, notifiers, metadata sources and
/// import lists exist, and what each one has been set to.
/// </summary>
/// <remarks>
/// <para>
/// One set of routes serves all five families, because a provider is described the same way whatever it
/// talks to: a declaration of the settings it takes, and a stored set of values for them. A front end that
/// can render one family can render all five without knowing they exist, which is the whole reason the
/// settings schema is declarative.
/// </para>
/// <para>
/// Deleting a definition never happens implicitly. An extension that stops being loaded leaves its
/// definitions in place, marked as orphaned, because the alternative — cleaning up definitions whose
/// implementation has vanished — means uninstalling an extension silently destroys the configuration the
/// user typed in, and reinstalling it does not bring that back.
/// </para>
/// </remarks>
internal static class ProviderEndpoints
{
    /// <summary>
    /// Maps the provider routes.
    /// </summary>
    /// <param name="group">The versioned route group.</param>
    /// <returns>The same group, for chaining.</returns>
    internal static RouteGroupBuilder MapProviderEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var providers = group.MapGroup("/providers")
            .WithTags("Providers")
            .AddEndpointFilter<SecretRedactionFilter>();

        providers.MapGet("/", ListProviders)
            .WithName("ListProviders")
            .WithSummary(
                "Lists every provider a loaded extension registered, with its qualified identifier, family "
                + "and settings declaration.");

        providers.MapGet("/definitions", ListDefinitions)
            .WithName("ListProviderDefinitions")
            .WithSummary("Lists every configured provider. Values declared sensitive are withheld.");

        providers.MapGet("/definitions/{id:int}", GetDefinition)
            .WithName("GetProviderDefinition")
            .WithSummary("Returns one configured provider. Values declared sensitive are withheld.");

        providers.MapPost("/definitions", CreateDefinition)
            .WithName("CreateProviderDefinition")
            .WithSummary("Configures a provider.");

        providers.MapPut("/definitions/{id:int}", UpdateDefinition)
            .WithName("UpdateProviderDefinition")
            .WithSummary("Changes a configured provider. A withheld value sent back unchanged is left alone.");

        providers.MapDelete("/definitions/{id:int}", DeleteDefinition)
            .WithName("DeleteProviderDefinition")
            .WithSummary("Removes a configured provider.");

        providers.MapPost("/definitions/{id:int}/test", TestDefinition)
            .WithName("TestProviderDefinition")
            .WithSummary("Asks the provider whether its current settings work.");

        providers.MapGet("/definitions/{id:int}/options/{sourceId}", GetDefinitionOptions)
            .WithName("GetProviderDefinitionOptions")
            .WithSummary("Resolves the values one of the provider's settings offers, by asking the provider.");

        return group;
    }

    private static Results<Ok<IReadOnlyList<ProviderCatalogEntry>>, ProblemHttpResult> ListProviders(
        string? family,
        string? kind,
        ProviderRegistry providers,
        IMediaKindRegistry kinds)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(kinds);

        var registered = providers.All.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(family))
        {
            if (!Enum.TryParse<ProviderFamily>(family, ignoreCase: true, out var parsed))
            {
                return ApiRequests.Problem(
                    StatusCodes.Status400BadRequest,
                    CoreErrorCode.InvalidConfiguration,
                    $"'{family}' is not one of {string.Join(", ", Enum.GetNames<ProviderFamily>())}.");
            }

            registered = registered.Where(provider => provider.Family == parsed);
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!kinds.TryGet(MediaKindId.FromString(kind), out var media) || media is null)
            {
                return ApiRequests.UnknownKind(kind);
            }

            // Admission binds a media-shaped provider to exactly one semantic kind. Plugin ownership is
            // intentionally irrelevant here: a separately packaged cataloger can serve this kind too.
            registered = registered.Where(provider => provider.PairedMediaKind == media.Kind);
        }

        // The qualified identifier and the family are host-owned facts, so they are served with the
        // declaration. A consumer that had only the declaration could not name the provider a configuration
        // must point at, and reconstructing the qualified form from a local name is a guess.
        IReadOnlyList<ProviderCatalogEntry> entries = [.. registered.Select(static provider => provider.Catalog)];
        return TypedResults.Ok(entries);
    }

    private static Ok<IReadOnlyList<ProviderDefinition>> ListDefinitions(ProviderDefinitionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return TypedResults.Ok(store.All);
    }

    private static Results<Ok<ProviderDefinition>, ProblemHttpResult> GetDefinition(int id, ProviderDefinitionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        return store.Find(id) is { } definition
            ? TypedResults.Ok(definition)
            : UnknownDefinition(id);
    }

    private static async Task<Results<Created<ProviderDefinition>, ProblemHttpResult>> CreateDefinition(
        ProviderDefinition? definition,
        ProviderRegistry providers,
        ProviderDefinitionStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(store);

        if (definition is null)
        {
            return MissingBody();
        }

        if (!providers.TryGet(definition.Provider, out var registered))
        {
            return UnknownProvider(definition.Provider);
        }

        var descriptor = registered.Descriptor;

        // A creation has no stored counterpart, so the sentinel means nothing here: there is no previous
        // value for it to stand in for. Saying so is better than the two alternatives, which are storing the
        // mask as the credential and silently creating a provider with no credential at all.
        if (SecretRedaction.CarriesSentinel(definition, descriptor))
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                "A new provider must be given its sensitive values; the placeholder only means 'unchanged'.");
        }

        var created = await store.AddAsync(definition, cancellationToken).ConfigureAwait(false);
        return TypedResults.Created($"/api/{ApiEndpoints.V1}/providers/definitions/{created.Id}", created);
    }

    private static async Task<Results<Ok<ProviderDefinition>, ProblemHttpResult>> UpdateDefinition(
        int id,
        ProviderDefinition? definition,
        ProviderRegistry providers,
        ProviderDefinitionStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(store);

        if (definition is null)
        {
            return MissingBody();
        }

        if (definition.Id != id)
        {
            return ApiRequests.Problem(
                StatusCodes.Status400BadRequest,
                CoreErrorCode.InvalidConfiguration,
                $"The body identifies definition {definition.Id} but the route addresses {id}.");
        }

        if (store.Find(id) is not { } stored)
        {
            return UnknownDefinition(id);
        }

        // A definition whose implementation is not loaded is orphaned, not invalid: it keeps its stored
        // settings and may still be edited. With no declaration to read, nothing is known to be sensitive,
        // so the merge is given none and every value travels as sent.
        var descriptor = providers.TryGet(definition.Provider, out var registered)
            ? registered.Descriptor
            : null;

        var merged = SecretRedaction.Merge(definition, stored, descriptor);
        var updated = await store.UpdateAsync(merged, cancellationToken).ConfigureAwait(false);

        return updated is null ? UnknownDefinition(id) : TypedResults.Ok(updated);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteDefinition(
        int id,
        ProviderDefinitionStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        return await store.RemoveAsync(id, cancellationToken).ConfigureAwait(false)
            ? TypedResults.NoContent()
            : UnknownDefinition(id);
    }

    private static async Task<Results<Ok<ValidationOutcome>, ProblemHttpResult>> TestDefinition(
        int id,
        ProviderDefinitionStore store,
        ProviderTestService tester,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tester);
        ArgumentNullException.ThrowIfNull(context);

        if (store.Find(id) is null)
        {
            return UnknownDefinition(id);
        }

        // The test service reads the correlation identifier from the ambient slot rather than taking one,
        // so the caller's header is pushed onto that slot for the duration of the call. Passing it as an
        // argument would be a second mechanism for one identifier.
        using var correlation = CorrelationContext.Push(CorrelationId(context));

        var outcome = await tester.TestAsync(id, cancellationToken).ConfigureAwait(false);

        // A provider that cannot be reached is not a failure of this request: the caller asked whether it
        // works and has been told, in the shape it asked for.
        return TypedResults.Ok(outcome);
    }

    private static async Task<Results<Ok<IReadOnlyList<FacetValue>>, ProblemHttpResult>> GetDefinitionOptions(
        int id,
        string sourceId,
        ProviderDefinitionStore store,
        ProviderTestService tester,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tester);
        ArgumentNullException.ThrowIfNull(context);

        if (store.Find(id) is null)
        {
            return UnknownDefinition(id);
        }

        using var correlation = CorrelationContext.Push(CorrelationId(context));

        var values = await tester.OptionsAsync(id, sourceId, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok(values);
    }

    private static string CorrelationId(HttpContext context)
        => context.Request.Headers[ActionEndpoints.CorrelationHeader].ToString() is { Length: > 0 } supplied
            ? supplied
            : context.TraceIdentifier;

    private static ProblemHttpResult UnknownDefinition(int id)
        => ApiRequests.Problem(
            StatusCodes.Status404NotFound,
            CoreErrorCode.MissingConfiguration,
            $"There is no provider definition {id}.");

    private static ProblemHttpResult UnknownProvider(ProviderId provider)
        => ApiRequests.Problem(
            StatusCodes.Status400BadRequest,
            CoreErrorCode.MissingConfiguration,
            $"No loaded extension registers the provider '{provider}'.");

    private static ProblemHttpResult MissingBody()
        => ApiRequests.Problem(
            StatusCodes.Status400BadRequest,
            CoreErrorCode.InvalidConfiguration,
            "A provider definition is required in the request body.");
}
