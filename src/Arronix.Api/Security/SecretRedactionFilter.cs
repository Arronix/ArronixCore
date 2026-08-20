using System.Linq;
using Arronix.Abstractions.Providers;
using Arronix.Host.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;


namespace Arronix.Api.Security;

/// <summary>
/// The one place a declared secret is turned into a sentinel on the way out and read back from storage on
/// the way in.
/// </summary>
/// <remarks>
/// <para>
/// The rule is derived, never restated. A provider declares each of its settings once, and part of that
/// declaration is how sensitive the value is; that single declaration is what drives the redaction applied
/// to logs, the intent a front end uses to mask an input, and the two rules here. Nothing in this file
/// knows the name of a single setting, so a provider added tomorrow is covered by it the day it ships and
/// a provider that forgets to mark a field is wrong in exactly one place.
/// </para>
/// <para>
/// The inbound half matters as much as the outbound half. If a value never leaves the server, a client
/// editing the rest of a form has nothing to send back for it — so the sentinel traveling inward means
/// "leave that one alone", and the stored value is spliced back in before the definition is saved. Without
/// that, every edit of a provider's name would silently blank its credentials.
/// </para>
/// </remarks>
internal static class SecretRedaction
{
    /// <summary>
    /// The value a withheld secret is represented by, in both directions.
    /// </summary>
    internal const string Sentinel = "********";

    /// <summary>
    /// Replaces every sensitive setting of a definition with the sentinel.
    /// </summary>
    /// <param name="definition">The definition to redact.</param>
    /// <param name="descriptor">The declaration that says which settings are sensitive.</param>
    /// <returns>A copy of the definition safe to serialize.</returns>
    internal static ProviderDefinition Redact(ProviderDefinition definition, ProviderDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var sensitive = SensitiveFieldIds(descriptor);
        if (sensitive.Count == 0)
        {
            return definition;
        }

        var settings = new Dictionary<string, string>(definition.Settings.Count, StringComparer.Ordinal);
        foreach (var setting in definition.Settings)
        {
            settings[setting.Key] = sensitive.Contains(setting.Key) && !string.IsNullOrEmpty(setting.Value)
                ? Sentinel
                : setting.Value;
        }

        return definition with { Settings = settings };
    }

    /// <summary>
    /// Puts the stored value back wherever an incoming definition carried the sentinel.
    /// </summary>
    /// <param name="incoming">The definition as it arrived.</param>
    /// <param name="stored">The definition as it currently is, or <see langword="null"/> when it is new.</param>
    /// <param name="descriptor">The declaration that says which settings are sensitive.</param>
    /// <returns>The definition to persist.</returns>
    internal static ProviderDefinition Merge(
        ProviderDefinition incoming,
        ProviderDefinition? stored,
        ProviderDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        var sensitive = SensitiveFieldIds(descriptor);
        if (sensitive.Count == 0)
        {
            return incoming;
        }

        var settings = new Dictionary<string, string>(incoming.Settings.Count, StringComparer.Ordinal);
        foreach (var setting in incoming.Settings)
        {
            if (sensitive.Contains(setting.Key)
                && string.Equals(setting.Value, Sentinel, StringComparison.Ordinal))
            {
                // The client was never given this value, so it cannot be sending a real one. Keeping what is
                // already stored is the only reading of the sentinel that does not destroy a credential.
                if (stored is not null && stored.Settings.TryGetValue(setting.Key, out var existing))
                {
                    settings[setting.Key] = existing;
                }

                continue;
            }

            settings[setting.Key] = setting.Value;
        }

        return incoming with { Settings = settings };
    }

    /// <summary>
    /// Says whether a definition carries the sentinel in a setting the declaration calls sensitive.
    /// </summary>
    /// <remarks>
    /// Asked before merging, never after. Merging is what makes the sentinel disappear — it is replaced by
    /// the stored value, or dropped when there is nothing stored — so a check applied to the merged result
    /// is a check that can never fail, which is worse than no check at all.
    /// </remarks>
    /// <param name="definition">The definition as it arrived.</param>
    /// <param name="descriptor">The declaration that says which settings are sensitive.</param>
    /// <returns><see langword="true"/> when at least one sensitive setting is the sentinel.</returns>
    internal static bool CarriesSentinel(ProviderDefinition definition, ProviderDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var sensitive = SensitiveFieldIds(descriptor);
        return definition.Settings.Any(setting =>
            sensitive.Contains(setting.Key)
            && string.Equals(setting.Value, Sentinel, StringComparison.Ordinal));
    }

    private static HashSet<string> SensitiveFieldIds(ProviderDescriptor? descriptor)
        => descriptor is null
            ? []
            : [.. descriptor.Settings
                .Where(static field => field.Sensitivity is SettingSensitivity.Credential or SettingSensitivity.Secret)
                .Select(static field => field.FieldId)];
}

/// <summary>
/// Applies <see cref="SecretRedaction"/> to whatever a provider endpoint returns.
/// </summary>
/// <remarks>
/// It sits on the route group rather than inside each handler on purpose. A handler that forgets to redact
/// is a leak that no test of that handler's own behavior would notice, whereas a filter that has to be
/// removed from the group to leak is a change somebody has to make deliberately and defend in review.
/// </remarks>
internal sealed class SecretRedactionFilter : IEndpointFilter
{
    private readonly ProviderRegistry _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretRedactionFilter"/> class.
    /// </summary>
    /// <param name="providers">The registry the declaration of each provider's settings is read from.</param>
    public SecretRedactionFilter(ProviderRegistry providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);

        var result = await next(context).ConfigureAwait(false);

        // A handler declared as a union of typed results does not return one of those results — it returns
        // the union, which carries the chosen result inside it. Matching without unwrapping compiles, passes
        // every test that only checks status codes, and silently never redacts anything.
        var chosen = result is INestedHttpResult nested ? nested.Result : result;

        return chosen switch
        {
            Ok<ProviderDefinition> single when single.Value is not null
                => TypedResults.Ok(Redact(single.Value)),
            Ok<IReadOnlyList<ProviderDefinition>> many when many.Value is not null
                => TypedResults.Ok<IReadOnlyList<ProviderDefinition>>([.. many.Value.Select(Redact)]),
            Created<ProviderDefinition> created when created.Value is not null
                => TypedResults.Created(created.Location, Redact(created.Value)),
            _ => result,
        };
    }

    private ProviderDefinition Redact(ProviderDefinition definition)
        => SecretRedaction.Redact(
            definition,
            _providers.TryGet(definition.Provider, out var registered) ? registered.Descriptor : null);
}
