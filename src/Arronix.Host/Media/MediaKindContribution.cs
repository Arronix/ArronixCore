using Arronix.Abstractions.Import;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;

// Every contract named here is experimental; this record is the bundle the loader hands the registry.
#pragma warning disable ARX0009
#pragma warning disable ARX0013
#pragma warning disable ARX0014
#pragma warning disable ARX0016

namespace Arronix.Host.Media;

/// <summary>
/// Everything one extension contributed for one media kind, gathered before admission.
/// </summary>
/// <remarks>
/// <para>
/// The registry admits a media kind atomically or not at all, so the pieces have to arrive together. This
/// record is that bundle: it exists so that the boundary between "what the loader collected" and "what the
/// registry validated and published" is a single argument rather than a sequence of calls that could leave a
/// kind half-registered.
/// </para>
/// <para>
/// Only the shape and the item source are required. A media kind with neither a matcher nor a query planner
/// is a browsable catalog with no acquisition, which is a legitimate thing to ship and is what the
/// capability model already says: the extension simply did not declare those privileges.
/// </para>
/// </remarks>
public sealed record MediaKindContribution
{
    /// <summary>
    /// Gets the extension that contributed it.
    /// </summary>
    public required PluginId Plugin { get; init; }

    /// <summary>
    /// Gets that extension's version, verbatim from its manifest.
    /// </summary>
    public required string PluginVersion { get; init; }

    /// <summary>
    /// Gets the capabilities granted to that extension, after implication.
    /// </summary>
    public required CapabilitySet Capabilities { get; init; }

    /// <summary>
    /// Gets the declared structure.
    /// </summary>
    public required MediaShape Shape { get; init; }

    /// <summary>
    /// Gets the catalog projection.
    /// </summary>
    public required IMediaItemSource Items { get; init; }

    /// <summary>
    /// Gets what the extension declared about how its kind is worked with, when it declared anything.
    /// </summary>
    public PluginIntentSurface? Intent { get; init; }

    /// <summary>
    /// Gets the seam that decides which items a release or a file refers to.
    /// </summary>
    public IReleaseMatcher? Matcher { get; init; }

    /// <summary>
    /// Gets the seam that turns an acquisition into queries.
    /// </summary>
    public IReleaseQueryPlanner? QueryPlanner { get; init; }

    /// <summary>
    /// Gets release-name parsing.
    /// </summary>
    public IReleaseParser? Parser { get; init; }

    /// <summary>
    /// Gets quality evaluation.
    /// </summary>
    public IQualityModel? Quality { get; init; }

    /// <summary>
    /// Gets the pipeline that takes files into the library.
    /// </summary>
    public IImportPipeline? Import { get; init; }

    /// <summary>
    /// Gets naming templates.
    /// </summary>
    public IRenamePolicy? Naming { get; init; }

    /// <summary>
    /// Gets folder layout.
    /// </summary>
    public ILibraryLayout? Layout { get; init; }

    /// <summary>
    /// Gets external-identifier resolution.
    /// </summary>
    public IMediaIdResolver? IdResolver { get; init; }

    /// <summary>
    /// Gets the validated definition the seams were built from, when the kind arrived declaratively.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> for an imperative kind. Downstream behavior never branches on it — the seams
    /// are the behavior either way — but a declarative kind's complete behavior is reviewable as data, and
    /// this is where that data survives admission.
    /// </remarks>
    public ValidatedDefinition? Definition { get; init; }
}
