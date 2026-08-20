using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// What every provider can do, whatever family it belongs to.
/// </summary>
/// <remarks>
/// Two members, both of which every surveyed provider family already implements in some form: prove the
/// configuration works, and supply values that can only be known by asking the service. The second
/// generalizes the ad-hoc "fetch the list of categories" request each surveyed application invented
/// separately for a different family.
/// </remarks>
public interface IProvider
{
    /// <summary>
    /// Gets the provider's identifier, assigned by the registry.
    /// </summary>
    ProviderId Id { get; }

    /// <summary>
    /// Gets the kind of external service this provider integrates with.
    /// </summary>
    ProviderFamily Family { get; }

    /// <summary>
    /// Proves that a definition can reach and authenticate against the service.
    /// </summary>
    /// <param name="invocation">The definition being tested, and its session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome, whose failures name the settings at fault where it can tell.</returns>
    Task<ValidationOutcome> TestAsync(
        ProviderInvocation invocation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Supplies the values of a setting whose permitted set can only be learned from the service.
    /// </summary>
    /// <param name="invocation">The definition asking, and its session.</param>
    /// <param name="optionSourceId">The <see cref="SettingsField.OptionSourceId"/> being resolved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The permitted values.</returns>
    Task<IReadOnlyList<FacetValue>> GetOptionsAsync(
        ProviderInvocation invocation,
        string optionSourceId,
        CancellationToken cancellationToken = default);
}
