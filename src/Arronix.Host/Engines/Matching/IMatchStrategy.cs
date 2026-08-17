namespace Arronix.Host.Engines.Matching;

/// <summary>
/// One member of the match strategy family: host-owned, named, versioned with the host, and
/// parameterized by declared data. The declaration references a strategy by role and identifier; the
/// plugin ships no code for it.
/// </summary>
/// <remarks>
/// Matching is a family, honestly: the surveyed kinds split between layered key lookup and assignment
/// over feature distances, and one operator chain covering both would be an over-claim. A kind that fits
/// neither gets a new member here — the family grows host-side, and the declaration vocabulary does not.
/// </remarks>
internal interface IMatchStrategy
{
    /// <summary>
    /// Gets the role this strategy can fill, from the host's closed role vocabulary.
    /// </summary>
    string Role { get; }

    /// <summary>
    /// Gets the identifier this strategy is resolved by within its role.
    /// </summary>
    string StrategyId { get; }
}
