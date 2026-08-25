using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Host.Media;

/// <summary>
/// Copies a media declaration into host-owned values before the host retains and publishes it.
/// </summary>
/// <remarks>
/// <para>
/// A shape and an intent surface are the longest-lived things an extension hands over: the host keeps them
/// for as long as the kind is published, reads them on every query, and projects them to consumers. Every
/// collection in them is declared as <c>IReadOnlyList&lt;T&gt;</c>, and through the imperative registration
/// path an extension supplies the instances — so a lazy sequence in one of them runs extension code every
/// time the host reads the shape, and a collection type from the extension's own assembly keeps its
/// collectible context alive for the life of the process.
/// </para>
/// <para>
/// Every collection is therefore rebuilt. The copy is mechanical and has to stay exhaustive as these
/// records gain members, which is what the hostile-collection regression exists to enforce: it walks a
/// copied graph and fails on anything the contract assembly did not define.
/// </para>
/// </remarks>
internal static class DeclarationBoundary
{
    /// <summary>Copies a declared shape and everything under it.</summary>
    /// <param name="shape">What the extension declared.</param>
    /// <returns>The shape, built from host-owned collections.</returns>
    internal static MediaShape Snapshot(MediaShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        return shape with
        {
            Levels = [.. shape.Levels.Select(Snapshot)],
            CoordinateSpaces = [.. shape.CoordinateSpaces.Select(Snapshot)],
            GroupingAxes = [.. shape.GroupingAxes.Select(Snapshot)],
            FormatFamilies = [.. shape.FormatFamilies.Select(Snapshot)],
            SelectionFacets = [.. shape.SelectionFacets.Select(Snapshot)],
            SearchKinds = [.. shape.SearchKinds.Select(Snapshot)],
            Tokens = [.. shape.Tokens.Select(Snapshot)],
            FileBinding = Snapshot(shape.FileBinding),
        };
    }

    /// <summary>Copies a declared intent surface and everything under it.</summary>
    /// <param name="surface">What the extension declared.</param>
    /// <returns>The surface, built from host-owned collections.</returns>
    internal static PluginIntentSurface Snapshot(PluginIntentSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        return surface with
        {
            BrowseAxes = [.. surface.BrowseAxes],
            Sorts = [.. surface.Sorts],
            Filters = [.. surface.Filters],
            Actions = [.. surface.Actions.Select(Snapshot)],
            States = [.. surface.States],
            ExternalSurfaces = [.. surface.ExternalSurfaces],
            Workbenches = [.. surface.Workbenches.Select(Snapshot)],
        };
    }

    private static MediaLevel Snapshot(MediaLevel level)
        => level with
        {
            Identity = Snapshot(level.Identity),
            CoordinateSpaceIds = [.. level.CoordinateSpaceIds],
            SequenceAxes = [.. level.SequenceAxes.Select(Snapshot)],
            Fields = [.. level.Fields.Select(Snapshot)],
            MonitorDimensions = [.. level.MonitorDimensions.Select(Snapshot)],
            FormatFamilyIds = [.. level.FormatFamilyIds],
        };

    private static LevelIdentity Snapshot(LevelIdentity identity)
        => identity with
        {
            RequiredRoles = [.. identity.RequiredRoles],
            AdmittedRoles = [.. identity.AdmittedRoles],
            ExternalIds = [.. identity.ExternalIds],
        };

    /// <remarks><see cref="NamingToken"/> is not sealed, so a declared token can be a subclass.</remarks>
    private static NamingToken Snapshot(NamingToken token)
        => token.GetType() == typeof(NamingToken)
            ? token
            : new NamingToken(token.Name, token.Description, token.ExampleValue, token.IsRequired);

    /// <remarks><see cref="QualityTier"/> is not sealed, so a declared tier can be a subclass.</remarks>
    private static QualityTier? Snapshot(QualityTier? tier)
        => tier is null || tier.GetType() == typeof(QualityTier)
            ? tier
            : new QualityTier(tier.Name, tier.Rank, tier.Weight, tier.GroupName, tier.Revision);

    private static SequenceAxis Snapshot(SequenceAxis axis)
        => axis with { Exceptions = [.. axis.Exceptions] };

    private static MonitorDimension Snapshot(MonitorDimension dimension)
        => dimension with { Choices = [.. dimension.Choices] };

    /// <remarks>Recursive: a composite field declares its components as fields of their own.</remarks>
    private static FieldDescriptor Snapshot(FieldDescriptor field)
        => field with
        {
            Choices = [.. field.Choices],
            Components = [.. field.Components.Select(Snapshot)],
        };

    private static CoordinateSpace Snapshot(CoordinateSpace space)
        => space with { Components = [.. space.Components] };

    private static GroupingAxis Snapshot(GroupingAxis axis)
        => axis with
        {
            Fields = [.. axis.Fields.Select(Snapshot)],
            ExternalIds = [.. axis.ExternalIds],
        };

    private static FormatFamily Snapshot(FormatFamily family)
        => family with
        {
            FileExtensions = [.. family.FileExtensions],
            Ladder = [.. family.Ladder.Select(tier => Snapshot(tier)!)],
            Unknown = Snapshot(family.Unknown),
        };

    private static SelectionFacet Snapshot(SelectionFacet facet)
        => facet with
        {
            Values = [.. facet.Values],
            DefaultAllowed = [.. facet.DefaultAllowed],
        };

    private static SearchKind Snapshot(SearchKind kind)
        => kind with
        {
            RequiredTerms = [.. kind.RequiredTerms],
            OptionalTerms = [.. kind.OptionalTerms],
            Categories = [.. kind.Categories],
        };

    private static FileBinding Snapshot(FileBinding binding)
        => binding with { SpanConstraints = [.. binding.SpanConstraints] };

    private static ActionDescriptor Snapshot(ActionDescriptor action)
        => action with { Parameters = [.. action.Parameters.Select(Snapshot)] };

    private static ActionParameter Snapshot(ActionParameter parameter)
        => parameter with { Choices = [.. parameter.Choices] };

    private static WorkbenchDescriptor Snapshot(WorkbenchDescriptor workbench)
        => workbench with
        {
            Columns = [.. workbench.Columns.Select(column => column with { Field = Snapshot(column.Field) })],
            Inputs = [.. workbench.Inputs.Select(Snapshot)],
        };
}
