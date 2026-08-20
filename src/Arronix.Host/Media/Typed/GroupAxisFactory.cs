using System.Linq;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Typed.Compilation;

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
    /// <param name="compiledShapes">The build-time-generated entity projections.</param>
    /// <returns>The axis.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="draft"/> is <see langword="null"/>.</exception>
    internal static GroupingAxis Derive(
        GroupDraft draft,
        MediaLevelId memberLevelId,
        CompiledShapeCatalog compiledShapes)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var reading = ItemTypeReader.Read(compiledShapes.Get(draft.GroupType));
        var singular = draft.Singular ?? DerivedNames.Label(draft.GroupType.Name);

        return new GroupingAxis
        {
            AxisId = draft.AxisId,
            Name = singular,
            PluralName = draft.Plural ?? DerivedNames.Plural(singular),
            MemberLevelId = memberLevelId,

            // Arity is read from the typed membership property: a scalar relationship is many-to-one and
            // a collection relationship is many-to-many.
            Arity = draft.Arity,
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
