using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed.Builders;

// The derivation reads and produces experimental contracts throughout.
#pragma warning disable ARX0013
#pragma warning disable ARX0020

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Turns a group type and the four facts its member property cannot imply into a grouping axis.
/// </summary>
/// <remarks>
/// The point of the whole exercise is the field list. A grouping axis could always declare that a group has
/// metadata of its own and never what that metadata is, so a consumer could render an item generically and
/// could not render a group at all. A group is a type, so its fields derive by exactly the rules an item's
/// do — one derivation, not two.
/// </remarks>
internal static class GroupAxisFactory
{
    /// <summary>
    /// Derives one grouping axis.
    /// </summary>
    /// <param name="draft">The axis as recorded.</param>
    /// <param name="memberLevelId">The level whose items are members.</param>
    /// <returns>The axis.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="draft"/> is <see langword="null"/>.</exception>
    internal static GroupingAxis Derive(GroupDraft draft, MediaLevelId memberLevelId)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var reading = ItemTypeReader.Read(draft.GroupType);
        var singular = draft.Singular ?? DerivedNames.Label(draft.GroupType.Name);

        return new GroupingAxis
        {
            AxisId = draft.AxisId,
            Name = singular,
            PluralName = draft.Plural ?? DerivedNames.Plural(singular),
            MemberLevelId = memberLevelId,

            // Arity, position and the primary-member flag are all read off the property being a single,
            // optional reference. There is nothing to declare, so nothing can contradict the type.
            Arity = GroupingArity.ManyToOne,
            Position = MemberPosition.None,
            HasPrimaryMember = false,

            IsMonitorable = draft.IsMonitorable,
            IsDiscoverySource = draft.IsDiscoverySource,
            Lifetime = draft.Lifetime,
            HasOwnMetadata = reading.Fields.Count > 0,
            Fields = [.. reading.Fields.Select(static candidate => candidate.Descriptor)],

            // Composed by the host from the catalogers installed for the group's key space, and empty
            // until one is. A kind naming the scheme here would be naming a vendor to express a type
            // distinction the C# type system already makes.
            ExternalIds = []
        };
    }
}
