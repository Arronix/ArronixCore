using System.Linq;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;


namespace Arronix.Host.Media;

/// <summary>
/// Works out what can be done with the items at a level.
/// </summary>
/// <remarks>
/// <para>
/// Every affordance is <b>derived</b>, never declared. An extension that could declare "these items are
/// selectable" could declare it for a shape with no variant level, and the host would then have to decide
/// which of the two contradictory statements to believe. A declaration that can be derived is a declaration
/// that can disagree.
/// </para>
/// <para>
/// Two of the ten also depend on what the deployment has configured rather than only on the shape: an item
/// is relocatable only where a root folder exists to relocate it to, and downloadable only where a release
/// source has been set up. Those are passed in rather than read from configuration here, so that the
/// derivation stays a function of its arguments and the tests can state every combination.
/// </para>
/// </remarks>
public static class AffordanceCalculator
{
    /// <summary>
    /// Derives the affordances of one level.
    /// </summary>
    /// <param name="shape">The validated shape the level belongs to.</param>
    /// <param name="level">The level.</param>
    /// <param name="capabilities">What the owning extension was granted, after implication.</param>
    /// <param name="rootFolderConfigured">Whether the deployment has at least one root folder.</param>
    /// <param name="releaseSourceConfigured">Whether the deployment has at least one enabled release source.</param>
    /// <returns>The affordances, in enumeration order so the result is stable across calls.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="shape"/> or <paramref name="level"/> is <see langword="null"/>.
    /// </exception>
    public static IReadOnlyList<Affordance> ForLevel(
        ValidatedShape shape,
        MediaLevel level,
        CapabilitySet capabilities,
        bool rootFolderConfigured,
        bool releaseSourceConfigured)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(level);

        var affordances = new List<Affordance>();

        // The user can express whether they want it exactly when the shape gave them an axis to say so on.
        if (level.MonitorDimensions.Count > 0)
        {
            affordances.Add(Affordance.Monitorable);
        }

        var isSearchTarget = shape.Declaration.SearchKinds.Any(search => search.TargetLevelId == level.Id);

        if (isSearchTarget && capabilities.Has(Capability.Indexing))
        {
            affordances.Add(Affordance.Searchable);
        }

        // Refreshing means re-reading a catalog record, which needs both a catalog record to re-read and
        // an identifier to read it by.
        if (level.Identity.HasCatalogRecord
            && level.Identity.ExternalIds.Count > 0
            && capabilities.Has(Capability.Metadata))
        {
            affordances.Add(Affordance.Refreshable);
        }

        if (level.Roles.HasFlag(MediaLevelRoles.FileBearing) && capabilities.Has(Capability.Renaming))
        {
            affordances.Add(Affordance.Renamable);
        }

        if (level.Identity.HasLibraryRecord)
        {
            affordances.Add(Affordance.Removable);
        }

        var index = IndexOf(shape.Levels, level);

        if (index >= 0 && index < shape.Levels.Count - 1)
        {
            affordances.Add(Affordance.Browsable);
        }

        // The choice among competing manifestations is made on the level ABOVE the variant level, because
        // that is where the chosen one is recorded. Attributing it to the variant level itself would put the
        // control on each of the things being chosen between.
        if (index >= 0
            && index < shape.Levels.Count - 1
            && shape.Levels[index + 1].Roles.HasFlag(MediaLevelRoles.VariantAxis))
        {
            affordances.Add(Affordance.Selectable);
        }

        // Tags, root folders and relocation are host features attached to what the user adds, not to any
        // media concept, so they follow the library-entry role alone.
        if (level.Roles.HasFlag(MediaLevelRoles.LibraryEntry))
        {
            affordances.Add(Affordance.Taggable);

            if (rootFolderConfigured)
            {
                affordances.Add(Affordance.Relocatable);
            }
        }

        if (isSearchTarget && releaseSourceConfigured && capabilities.Has(Capability.Indexing))
        {
            affordances.Add(Affordance.Downloadable);
        }

        affordances.Sort();
        return affordances;
    }

    private static int IndexOf(IReadOnlyList<MediaLevel> levels, MediaLevel level)
    {
        for (var index = 0; index < levels.Count; index++)
        {
            if (levels[index].Id == level.Id)
            {
                return index;
            }
        }

        return -1;
    }
}
