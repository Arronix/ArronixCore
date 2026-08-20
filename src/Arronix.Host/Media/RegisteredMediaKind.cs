using Arronix.Abstractions.Import;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;


namespace Arronix.Host.Media;

/// <summary>
/// One media kind, as the host holds it after admission.
/// </summary>
/// <remarks>
/// Everything reachable from here has already been checked. The shape is resolved rather than declared, the
/// stable projection and the wire bundle are built rather than supplied, and the seams are exactly those the
/// contributing extension was granted. Nothing downstream needs to re-validate anything, which is the whole
/// purpose of putting a gate at the boundary.
/// </remarks>
public sealed class RegisteredMediaKind
{
    internal RegisteredMediaKind(
        MediaKindContribution contribution,
        ValidatedShape shape,
        PluginIntentSurface intent,
        IMediaKind projection,
        MediaKindDescriptor descriptor)
    {
        Plugin = contribution.Plugin;
        PluginVersion = contribution.PluginVersion;
        Capabilities = contribution.Capabilities;
        Shape = shape;
        Items = contribution.Items;
        Matcher = contribution.Matcher;
        QueryPlanner = contribution.QueryPlanner;
        Parser = contribution.Parser;
        Quality = contribution.Quality;
        Import = contribution.Import;
        Naming = contribution.Naming;
        Layout = contribution.Layout;
        IdResolver = contribution.IdResolver;
        Intent = intent;
        Projection = projection;
        Descriptor = descriptor;
        Definition = contribution.Definition;
        MediaType = contribution.MediaType;
    }

    /// <summary>Gets the extension that contributed this kind.</summary>
    public PluginId Plugin { get; }

    /// <summary>Gets that extension's version.</summary>
    public string PluginVersion { get; }

    /// <summary>Gets the capabilities granted to it, before implication.</summary>
    public CapabilitySet Capabilities { get; }

    /// <summary>Gets the media kind's identifier.</summary>
    public Abstractions.Identity.MediaKindId Kind => Shape.Kind;

    /// <summary>Gets the closed typed media runtime, when registered through the typed contract.</summary>
    public IMediaTypeRuntime? MediaType { get; }

    /// <summary>Gets the resolved shape.</summary>
    public ValidatedShape Shape { get; }

    /// <summary>Gets the catalog projection.</summary>
    public IMediaItemSource Items { get; }

    /// <summary>Gets the seam that decides which items a release or a file refers to.</summary>
    public IReleaseMatcher? Matcher { get; }

    /// <summary>Gets the seam that turns an acquisition into queries.</summary>
    public IReleaseQueryPlanner? QueryPlanner { get; }

    /// <summary>Gets release-name parsing.</summary>
    public IReleaseParser? Parser { get; }

    /// <summary>Gets quality evaluation.</summary>
    public IQualityModel? Quality { get; }

    /// <summary>Gets the pipeline that takes files into the library.</summary>
    public IImportPipeline? Import { get; }

    /// <summary>Gets naming templates.</summary>
    public IRenamePolicy? Naming { get; }

    /// <summary>Gets folder layout.</summary>
    public ILibraryLayout? Layout { get; }

    /// <summary>Gets external-identifier resolution.</summary>
    public IMediaIdResolver? IdResolver { get; }

    /// <summary>Gets what the extension declared about how its kind is worked with.</summary>
    public PluginIntentSurface Intent { get; }

    /// <summary>
    /// Gets the validated definition the seams were built from, when the kind arrived declaratively;
    /// <see langword="null"/> for an imperative kind.
    /// </summary>
    public ValidatedDefinition? Definition { get; }

    /// <summary>Gets the stable media-kind contract, built by the host.</summary>
    public IMediaKind Projection { get; }

    /// <summary>
    /// Gets the bundle a consumer needs to present this kind, built once at admission.
    /// </summary>
    /// <remarks>
    /// Cached because it is the single largest response the platform serves and it changes only when the
    /// deployment does. The registry rebuilds it when a fact it derives from — a configured root folder, an
    /// enabled release source — changes.
    /// </remarks>
    public MediaKindDescriptor Descriptor { get; internal set; }
}
