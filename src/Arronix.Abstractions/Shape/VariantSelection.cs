using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Declares the at-most-one-selected semantics of a level that carries
/// <see cref="MediaLevelRoles.VariantAxis"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two of the surveyed applications hand-roll this invariant inside a repository, each with its own
/// assertion and its own repair path. Declaring it lets the host enforce it once, for any kind.
/// </para>
/// <para>
/// <see cref="Triggers"/> is the part worth reading twice: where importing files is allowed to change the
/// selection, importing silently redefines which manifestation the library considers canonical. That is
/// surprising when it emerges from an implementation and unsurprising when a kind declares it.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record VariantSelection
{
    /// <summary>
    /// Gets a value indicating whether the host may switch the selection on the user's behalf by default.
    /// </summary>
    public bool AutoSwitchByDefault { get; init; } = true;

    /// <summary>
    /// Gets the occasions on which an automatic switch may happen.
    /// </summary>
    public SelectionTrigger Triggers { get; init; } = SelectionTrigger.Manual;

    /// <summary>
    /// Gets a value indicating whether completeness is counted against the selected variant rather than
    /// against the union of all of them.
    /// </summary>
    public bool CompletenessIsVariantRelative { get; init; } = true;
}

/// <summary>
/// When the host may change a variant selection without being asked.
/// </summary>
[Flags]
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum SelectionTrigger
{
    /// <summary>Only a user changes the selection.</summary>
    Manual = 0,

    /// <summary>Importing files that match another variant may switch to it.</summary>
    OnImport = 1,

    /// <summary>Refreshing the catalog may switch to a variant that has become preferable.</summary>
    OnRefresh = 2
}
