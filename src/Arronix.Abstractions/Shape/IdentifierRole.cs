
namespace Arronix.Abstractions.Shape;

/// <summary>
/// An abstract external-identifier role a media kind can declare without naming a catalog.
/// </summary>
/// <remarks>
/// <para>
/// A kind states that its items are keyed by <i>some</i> catalog and may be cross-referenced to
/// <i>another</i>; which concrete schemes fill those roles is a fact about the installed catalogers, not
/// about the media kind. Declaring the role instead of the scheme is what lets identifier search work with
/// whichever cataloger is installed rather than only with the one the kind happened to be written against.
/// </para>
/// <para>
/// The roles are the union of what the surveyed kinds actually distinguish, and no more. A second work
/// identifier and a group identifier are different roles rather than different schemes because they live in
/// different key spaces: comparing a group identifier against a work identifier is always a defect, and
/// the role is what makes that statable.
/// </para>
/// </remarks>
public enum IdentifierRole
{
    /// <summary>The catalog the kind's own records are keyed by. At most one scheme fills this role.</summary>
    PrimaryWork = 0,

    /// <summary>A second catalog the same work is known to.</summary>
    SecondaryWork = 1,

    /// <summary>The catalog a group on a declared grouping axis is keyed by.</summary>
    PrimaryGroup = 2
}
