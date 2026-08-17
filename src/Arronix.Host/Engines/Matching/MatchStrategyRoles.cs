namespace Arronix.Host.Engines.Matching;

/// <summary>
/// The closed role vocabulary of the match strategy family: every strategy the matcher resolves fills one
/// of these roles.
/// </summary>
internal static class MatchStrategyRoles
{
    /// <summary>Resolving a reading to a catalog entry.</summary>
    internal const string EntryResolution = "entry-resolution";

    /// <summary>Assigning a set of readings to a set of units by least total feature distance.</summary>
    internal const string UnitAssignment = "unit-assignment";
}
