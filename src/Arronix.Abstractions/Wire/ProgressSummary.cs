
namespace Arronix.Abstractions.Wire;

/// <summary>
/// How much of what is wanted has been obtained.
/// </summary>
/// <param name="Have">The number of wanted items that are satisfied.</param>
/// <param name="Want">The number of items the user wants.</param>
/// <param name="Total">The number of items that exist, wanted or not.</param>
/// <param name="SizeOnDisk">The total size of the files satisfying them, in bytes.</param>
/// <remarks>
/// Three counts rather than two, because "wanted" and "exists" are different questions and collapsing
/// them makes a library that is complete for its owner look incomplete. Computed by the host from the
/// declared shape: what counts as an item, whether it counts against the selected variant only, and which
/// positions are excluded outright are all declarations, not code.
/// </remarks>
public sealed record ProgressSummary(int Have, int Want, int Total, long SizeOnDisk);
