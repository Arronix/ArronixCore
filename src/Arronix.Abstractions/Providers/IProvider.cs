using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// What every provider can do, whatever family it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Two members, both of which every surveyed provider family already implements in some form: prove the
/// configuration works, and supply values that can only be known by asking the service. The second
/// generalizes the ad-hoc "fetch the list of categories" request each surveyed application invented
/// separately for a different family.
/// </para>
/// <para>
/// A provider states neither its identifier nor its family. Identity is minted by the host from the
/// contributing extension and the local name the declaration chose, and family is fixed by the registration
/// the implementation is admitted through. A provider that needs its own qualified identifier during a call
/// reads <see cref="ProviderDefinition.Provider"/> from the invocation, which is that one authority.
/// </para>
/// </remarks>
public interface IProvider
{
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
