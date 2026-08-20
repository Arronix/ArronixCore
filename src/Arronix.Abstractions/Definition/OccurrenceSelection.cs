
namespace Arronix.Abstractions.Definition;

/// <summary>
/// Which occurrence wins when a token scan matches the text more than once.
/// </summary>
/// <remarks>
/// This is a property of the scan, not of any rule table: the surveyed source scan takes the rightmost
/// occurrence in the string — which varies per release — and no fixed rule-selection mode over a table
/// authored in advance can express that. The mode therefore lives here, on the thing that scans, and
/// rule tables consume the single value the scan produced.
/// </remarks>
public enum OccurrenceSelection
{
    /// <summary>The leftmost occurrence in the text wins.</summary>
    FirstOccurrence = 0,

    /// <summary>The rightmost occurrence in the text wins.</summary>
    LastOccurrence = 1
}
