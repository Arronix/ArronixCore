
using System.Linq;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;
using Arronix.Format.Video;
using Arronix.Host.Media;
using Arronix.Host.Media.Catalog;
using Arronix.Host.Media.Typed;
using Arronix.Plugin.Movies.Definition;

namespace Arronix.Plugin.Movies.Tests.Support;

/// <summary>
/// The runtime model the host derives from the typed movie kind, plus the lookups every fixture needs.
/// </summary>
/// <remarks>Derived once so every fixture reads the same model the host admits.</remarks>
internal static class MoviesDeclaration
{
    /// <summary>The kind's runtime model, derived once from <see cref="Movie"/> and <see cref="Movies"/>.</summary>
    internal static IMediaTypeRuntime Model { get; } =
        MediaTypeModelFactory.Build<
            Movie,
            ReleaseTarget<Movie>,
            Release<Video>,
            MovieReleaseParser,
            Movies>();

    /// <summary>Host identity state, standing in for the one a running host owns.</summary>
    internal static CatalogIdentity Identity { get; } = new();

    /// <summary>
    /// The reference the host holds one entity under, assigned from its catalog identifiers.
    /// </summary>
    /// <remarks>
    /// Assignment is what taking an item in does, so the fixture reaches it the way the take-in path does —
    /// through the assigning contract. Projection below is handed the reader and could not assign if it
    /// tried.
    /// </remarks>
    internal static MediaItemRef Reference(IMediaEntity entity) =>
        ((ICatalogIdentityAssignment)Identity).Identify(Model.Kind, Level.Id, entity!.ExternalIds.Values);

    /// <summary>
    /// The reference the host holds one group under, in the axis that addresses it.
    /// </summary>
    /// <remarks>
    /// A group is addressed per grouping axis rather than per level, so the axis identifier fills the level
    /// slot. Naming one is assignment, the same as naming an item: a projection resolves what has been named
    /// and never names anything itself.
    /// </remarks>
    internal static MediaItemRef ReferenceIn(string axisId, IMediaEntity entity) =>
        ((ICatalogIdentityAssignment)Identity)
            .Identify(Model.Kind, MediaLevelId.FromString(axisId), entity.ExternalIds.Values);

    /// <summary>Projects one entity the way the host does: host states the reference, model reads the item.</summary>
    internal static ItemView Project(IMediaEntity entity) => Model.Project(Reference(entity), entity, Identity);

    /// <summary>Reads one field off one entity.</summary>
    internal static FieldValue Read(object item, string fieldId) => Model.Read(item, fieldId, Identity);

    /// <summary>The derived structure.</summary>
    internal static MediaShape Shape => Model.Shape;

    /// <summary>The derived intent surface.</summary>
    internal static PluginIntentSurface Intent => Model.Intent;

    /// <summary>The carried per-kind engine inputs.</summary>
    internal static MediaKindModel Carried => Model.Model;

    /// <summary>The single video format family.</summary>
    internal static FormatFamily Video => Shape.FormatFamilies[0];

    /// <summary>The derived runtime model, exactly as the host derives it.</summary>
    /// <remarks>
    /// <para>
    /// Nothing is composed on top any more. Three things used to be: the external-identifier schemes, one
    /// unit-resolution row and the match-confidence table. The host now supplies all three itself — the
    /// schemes because the gate stopped cross-checking a catalog map against a shape that deliberately does
    /// not enumerate them, the unit row because the derivation emits the one its structure implies, and the
    /// confidence table because how far to trust a basis is host policy and
    /// <c>MatchConfidencePolicy</c> owns it.
    /// </para>
    /// <para>
    /// So a fixture reading this is reading the derivation's own output, with nothing standing in.
    /// </para>
    /// </remarks>
    internal static IMediaTypeRuntime Derived => Model;

    /// <summary>The item level of the derived shape.</summary>
    internal static MediaLevel Level => Shape.Levels[0];

    /// <summary>Every derived field of the item level, by its derived identifier.</summary>
    internal static IReadOnlyDictionary<string, FieldDescriptor> Fields { get; } =
        Level.Fields.ToDictionary(static field => field.FieldId, StringComparer.Ordinal);

}
