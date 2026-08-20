using System.Linq;
using Arronix.Host.Languages;

namespace Arronix.Host.Engines.Matching;

/// <summary>
/// The named registry of the match strategy family.
/// </summary>
/// <remarks>
/// Resolution fails fast and loudly: an unknown role or strategy identifier is refused with both names in
/// the message, never given a silent fallback. Nothing outside the host can reach this any more — the two
/// strategies a matcher uses are derived from the kind's own declaration rather than named by it — so a
/// failure here is a host defect, and the loudness is what keeps it one.
/// </remarks>
internal sealed class MatchStrategyRegistry
{
    private readonly Dictionary<(string Role, string StrategyId), IMatchStrategy> _strategies = [];

    /// <summary>
    /// Creates a registry carrying the host's built-in strategies.
    /// </summary>
    /// <param name="clock">The clock time-dependent distance features read.</param>
    /// <param name="languages">The installed language operations.</param>
    /// <returns>The registry.</returns>
    internal static MatchStrategyRegistry CreateDefault(TimeProvider clock, LanguageTextService? languages = null)
    {
        languages ??= new LanguageTextService(new LanguageDefinitionRegistry());
        var registry = new MatchStrategyRegistry();
        registry.Register(new LayeredKeyLookupStrategy(new MatchKeyNormalizers(languages)));
        registry.Register(new AssignmentOverFeaturesStrategy(DistanceFeatureCatalog.CreateDefault(clock)));
        return registry;
    }

    /// <summary>
    /// Registers one strategy.
    /// </summary>
    /// <param name="strategy">The strategy to register.</param>
    /// <exception cref="InvalidOperationException">The role and identifier pair is already taken.</exception>
    internal void Register(IMatchStrategy strategy)
    {
        if (!_strategies.TryAdd((strategy.Role, strategy.StrategyId), strategy))
        {
            throw new InvalidOperationException(
                $"A strategy '{strategy.StrategyId}' for role '{strategy.Role}' is already registered.");
        }
    }

    /// <summary>
    /// Resolves one strategy, typed.
    /// </summary>
    /// <typeparam name="TStrategy">The strategy surface the role requires.</typeparam>
    /// <param name="role">The role being filled.</param>
    /// <param name="strategyId">The strategy chosen for the role.</param>
    /// <returns>The strategy.</returns>
    /// <exception cref="InvalidOperationException">
    /// The pair is unregistered, or the registered strategy does not carry the required surface.
    /// </exception>
    internal TStrategy Resolve<TStrategy>(string role, string strategyId)
        where TStrategy : class, IMatchStrategy
    {
        if (!_strategies.TryGetValue((role, strategyId), out var strategy))
        {
            var known = string.Join(
                ", ",
                _strategies.Keys
                    .Where(key => string.Equals(key.Role, role, StringComparison.Ordinal))
                    .Select(key => $"'{key.StrategyId}'")
                    .Order(StringComparer.Ordinal));

            throw new InvalidOperationException(
                $"No strategy '{strategyId}' is registered for role '{role}'. "
                + (known.Length > 0 ? $"Registered for the role: {known}." : "The role itself is unknown."));
        }

        return strategy as TStrategy
            ?? throw new InvalidOperationException(
                $"Strategy '{strategyId}' for role '{role}' is a {strategy.GetType().Name}, "
                + $"not the {typeof(TStrategy).Name} the role requires.");
    }
}
