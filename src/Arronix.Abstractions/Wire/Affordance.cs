using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Wire;

/// <summary>
/// Something that can be done with the items at a level.
/// </summary>
/// <remarks>
/// <para>
/// Every member is <b>derived by the host</b> from the validated shape and from what the deployment has
/// configured — the ability to choose among variants follows from a level carrying the variant role, the
/// ability to browse from its having a child, and so on. None of it is declared, because a declaration
/// that can be derived is a declaration that can disagree.
/// </para>
/// <para>
/// Closed, and switched over exhaustively by consumers, so an addition here is a compile error at each of
/// them rather than an ability that silently never appears.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Wire, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum Affordance
{
    /// <summary>The user can express whether they want the item.</summary>
    Monitorable = 0,

    /// <summary>The item can be searched for.</summary>
    Searchable = 1,

    /// <summary>The item's catalog record can be refreshed.</summary>
    Refreshable = 2,

    /// <summary>The item's files can be renamed.</summary>
    Renamable = 3,

    /// <summary>The item can be removed from the library.</summary>
    Removable = 4,

    /// <summary>The item contains items that can be listed.</summary>
    Browsable = 5,

    /// <summary>One of the item's competing manifestations can be chosen.</summary>
    Selectable = 6,

    /// <summary>The item can carry platform tags.</summary>
    Taggable = 7,

    /// <summary>The item's files can be moved to another root folder.</summary>
    Relocatable = 8,

    /// <summary>The item can be acquired, because a release source is configured for it.</summary>
    Downloadable = 9
}
