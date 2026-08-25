using Arronix.Abstractions.Import;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Naming;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Quality;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Common.Contributions;


namespace Arronix.Host.Media;

/// <summary>
/// One media kind, as the host holds it after admission.
/// </summary>
/// <remarks>
/// <para>
/// Everything reachable from here has already been checked. The shape is resolved rather than declared, the
/// stable projection and the wire bundle are built rather than supplied, and the seams are exactly those the
/// contributing extension was granted.
/// </para>
/// <para>
/// The executable seams are internal, and reachable only from a leased handle. Identity, shape, descriptor
/// and projection are declarations and stay public. That split is what makes the lease a compiler rule
/// rather than a convention: outside the host there is no way to call a kind's parser or item source at all,
/// and inside it there is no way to reach one without holding the contributing extension's ticket.
/// </para>
/// </remarks>
public sealed class RegisteredMediaKind
{
    internal RegisteredMediaKind(
        MediaKindContribution contribution,
        ValidatedShape shape,
        PluginIntentSurface intent,
        IMediaKind projection,
        MediaKindDescriptor descriptor,
        IInvocationLifetime? lifetime = null)
    {
        Lifetime = lifetime;
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

    /// <summary>
    /// Gets the contributing extension's licence to be called, when an extension contributed the kind.
    /// </summary>
    /// <remarks>
    /// Everything executable this kind carries — its parser, matcher, query planner, naming and import
    /// seams — is that extension's code, so a caller that runs one holds a ticket for the whole call.
    /// </remarks>
    internal IInvocationLifetime? Lifetime { get; }

    /// <summary>Gets the extension that contributed this kind.</summary>
    public PluginId Plugin { get; }

    /// <summary>Gets that extension's version.</summary>
    public string PluginVersion { get; }

    /// <summary>Gets the capabilities granted to it, before implication.</summary>
    public CapabilitySet Capabilities { get; }

    /// <summary>Gets the media kind's identifier.</summary>
    public Abstractions.Identity.MediaKindId Kind => Shape.Kind;

    /// <summary>Gets the closed typed media runtime, when registered through the typed contract.</summary>
    internal IMediaTypeRuntime? MediaType { get; }

    /// <summary>Gets the resolved shape.</summary>
    public ValidatedShape Shape { get; }

    /// <summary>Gets the catalog projection.</summary>
    internal IMediaItemSource Items { get; }

    /// <summary>Gets the seam that decides which items a release or a file refers to.</summary>
    internal IReleaseMatcher? Matcher { get; }

    /// <summary>Gets the seam that turns an acquisition into queries.</summary>
    internal IReleaseQueryPlanner? QueryPlanner { get; }

    /// <summary>Gets release-name parsing.</summary>
    internal IReleaseParser? Parser { get; }

    /// <summary>Gets quality evaluation.</summary>
    internal IQualityModel? Quality { get; }

    /// <summary>Gets the pipeline that takes files into the library.</summary>
    internal IImportPipeline? Import { get; }

    /// <summary>Gets naming templates.</summary>
    internal IRenamePolicy? Naming { get; }

    /// <summary>Gets folder layout.</summary>
    internal ILibraryLayout? Layout { get; }

    /// <summary>Gets external-identifier resolution.</summary>
    internal IMediaIdResolver? IdResolver { get; }

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
