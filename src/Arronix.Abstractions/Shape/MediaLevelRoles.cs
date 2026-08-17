using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// What a level is for. Several roles routinely land on one level.
/// </summary>
/// <remarks>
/// <para>
/// A closed flag set, unlike the open string vocabularies elsewhere in the shape, because the host
/// <i>dispatches</i> on it: an unrecognized role would be silently ignored, which is worse than making a
/// new role a deliberate contract change.
/// </para>
/// <para>
/// Flags rather than a single-valued position enum because the roles are not mutually exclusive. The
/// simplest surveyed hierarchy has one level carrying every role at once, and the richest spreads six
/// roles over four levels; a position enum can express neither.
/// </para>
/// </remarks>
[Flags]
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum MediaLevelRoles
{
    /// <summary>No role. Valid for a purely structural level.</summary>
    None = 0,

    /// <summary>
    /// What a user adds. Owns the path, the profiles, the tags and the root folder. Exactly one level
    /// per shape carries this.
    /// </summary>
    LibraryEntry = 1 << 0,

    /// <summary>What a search or a grab targets.</summary>
    AcquisitionUnit = 1 << 1,

    /// <summary>
    /// Competing manifestations of the parent, with at-most-one-selected semantics.
    /// </summary>
    VariantAxis = 1 << 2,

    /// <summary>What "have" and "missing" are counted in.</summary>
    CompletenessUnit = 1 << 3,

    /// <summary>Participates in the item-to-file join.</summary>
    FileBearing = 1 << 4
}
