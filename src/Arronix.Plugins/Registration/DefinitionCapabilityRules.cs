using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;


namespace Arronix.Plugins.Registration;

/// <summary>
/// One capability a media kind's section demands, and the section that demands it.
/// </summary>
/// <param name="Capability">The capability the section requires.</param>
/// <param name="Section">
/// The section, spelled the way a refusal should name it — <c>MediaKindModel.Parsing</c> — so the author of
/// a refused extension knows which part of the declaration carried the demand.
/// </param>
public readonly record struct DefinitionSectionRequirement(Capability Capability, string Section);

/// <summary>
/// How a media kind's derived model maps onto the capability vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// The capability enum does not change for a kind that declares rather than implements; what changes is how
/// a capability is satisfied. A kind's sections <i>are</i> its registrations: the parsing section
/// contributes release parsing exactly as an <c>AddReleaseParser</c> call would, so it demands — and
/// satisfies — the same capability. This class is that mapping, stated once, so the reverse check (a section
/// present without its capability is refused) and the forward check (a declared capability with no
/// satisfying section quarantines) read the same table.
/// </para>
/// <para>
/// The required sections — parsing, matching, querying — appear on every model, so every media kind demands
/// the media-kind, parsing, matching and indexing capabilities. The defaulted sections demand a capability
/// only when they differ from their defaults, because a default section is the host's own behavior rather
/// than a contribution: quality defaults to pure ladder derivation, naming to the bare folder spine,
/// notifications to the host-generic summary, and the catalog section to absent.
/// </para>
/// <para>
/// The rules read <see cref="MediaKindModel"/> rather than the structure, because structure is derived from
/// the item type and carries no capability of its own beyond the media-kind gate. That is the whole reason
/// the shape section is absent from this table.
/// </para>
/// </remarks>
public static class DefinitionCapabilityRules
{
    /// <summary>
    /// Gets every capability a media kind's model demands, each paired with the section demanding it.
    /// </summary>
    /// <param name="model">The derived model.</param>
    /// <returns>The demands, in section order, the registration gate itself first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<DefinitionSectionRequirement> Requirements(MediaKindModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var requirements = new List<DefinitionSectionRequirement>
        {
            new(Capability.MediaKind, nameof(MediaKindModel)),
            new(Capability.Parsing, $"{nameof(MediaKindModel)}.{nameof(MediaKindModel.Parsing)}"),
            new(Capability.Matching, $"{nameof(MediaKindModel)}.{nameof(MediaKindModel.Matching)}"),
            new(Capability.Indexing, $"{nameof(MediaKindModel)}.{nameof(MediaKindModel.Querying)}"),
        };

        if (DeclaresNaming(model.Naming))
        {
            requirements.Add(new(
                Capability.Renaming,
                $"{nameof(MediaKindModel)}.{nameof(MediaKindModel.Naming)}"));
        }

        if (DeclaresNotifications(model.Notifications))
        {
            requirements.Add(new(
                Capability.Notification,
                $"{nameof(MediaKindModel)}.{nameof(MediaKindModel.Notifications)}"));
        }

        return requirements;
    }

    /// <summary>
    /// Gets the capabilities a media kind's sections account for, for the forward half of the check.
    /// </summary>
    /// <param name="model">The derived model.</param>
    /// <returns>The satisfied set: exactly the capabilities <see cref="Requirements"/> demands.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public static CapabilitySet SatisfiedBy(MediaKindModel model) => SatisfiedBy(Requirements(model));

    /// <summary>
    /// Gets the capabilities a demand list accounts for.
    /// </summary>
    /// <param name="requirements">The demands.</param>
    /// <returns>The satisfied set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requirements"/> is <see langword="null"/>.</exception>
    public static CapabilitySet SatisfiedBy(IReadOnlyList<DefinitionSectionRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        var satisfied = CapabilitySet.None;

        foreach (var requirement in requirements)
        {
            satisfied = satisfied.Union(CapabilitySet.Of(requirement.Capability));
        }

        return satisfied;
    }

    /// <summary>
    /// Determines whether a naming section says anything the default does not.
    /// </summary>
    /// <param name="naming">The section.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool DeclaresNaming(NamingDeclaration naming)
    {
        ArgumentNullException.ThrowIfNull(naming);

        return naming.DefaultTemplates.Count > 0
            || naming.Selection.Count > 0
            || naming.MultiUnitStyles.Count > 0
            || naming.Fallbacks.Count > 0
            || !string.Equals(naming.FolderSpine, NamingDeclaration.Default.FolderSpine, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether a notification section says anything the host-generic default does not.
    /// </summary>
    /// <param name="notifications">The section.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool DeclaresNotifications(NotificationDeclaration notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        return notifications.HeadlineTemplate is not null
            || notifications.BodyFieldId is not null
            || notifications.Fields.Count > 0
            || notifications.GroupSummaries.Count > 0
            || notifications.HeadlineMaxLength != NotificationDeclaration.Default.HeadlineMaxLength
            || notifications.BodyMaxLength != NotificationDeclaration.Default.BodyMaxLength;
    }
}
