
namespace Arronix.Abstractions.Shape;

/// <summary>
/// What a re-issued release means once the quality cutoff is already met.
/// </summary>
/// <remarks>
/// Without this, "cutoff met, but still take the corrected issue" is inexpressible: the cutoff check
/// answers before the revision is ever consulted. The three answers are the surveyed set.
/// </remarks>
public enum ProperHandling
{
    /// <summary>A corrected issue is taken even when the cutoff is met.</summary>
    PreferProper = 0,

    /// <summary>A corrected issue is taken only while the cutoff is not met.</summary>
    AcceptProper = 1,

    /// <summary>Revisions never cause a grab on their own.</summary>
    IgnoreProper = 2
}
