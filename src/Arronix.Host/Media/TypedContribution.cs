using Arronix.Abstractions.Media;
using Arronix.Abstractions.Plugins;

// The typed media surface and the extension model are experimental; this record is the bundle the loader
// hands the binder.
#pragma warning disable ARX0014
#pragma warning disable ARX0020

namespace Arronix.Host.Media;

/// <summary>
/// One captured typed media kind, together with who contributed it, gathered before admission.
/// </summary>
/// <remarks>
/// The typed counterpart of <see cref="MediaKindContribution"/>: the loader captures the type pair from the
/// extension's one registration and hands it over as a single argument, so the boundary between "what was
/// captured" and "what the host derived, validated, bound and published" stays a single call. The extension
/// contributed two types and nothing else — every descriptor is derived and every seam instance is built
/// host-side by <see cref="MediaTypeBinder"/>.
/// </remarks>
public sealed record TypedContribution
{
    /// <summary>
    /// Gets the extension that contributed the kind.
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
    /// Gets the captured registration, with both type arguments still recoverable.
    /// </summary>
    public required IMediaTypeRegistration Registration { get; init; }
}
