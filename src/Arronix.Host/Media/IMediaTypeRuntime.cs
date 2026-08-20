using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Releases;
using Arronix.Abstractions.Shape;


namespace Arronix.Host.Media;

/// <summary>The host's kind-blind handle to one closed typed media runtime.</summary>
/// <remarks>
/// Host-owned by design. This is the erasure a heterogeneous registry needs, not a second media-type
/// contract and not something an extension or client implements. Exact item, target and release types
/// remain on the captured registration; wire descriptors are projections for discovery and generic UI.
/// </remarks>
public interface IMediaTypeRuntime
{
    MediaKindId Kind { get; }

    Type ItemType { get; }

    Type TargetType { get; }

    Type ReleaseType { get; }

    Type ParserType { get; }

    IReadOnlyList<Type> GroupTypes { get; }

    MediaShape Shape { get; }

    PluginIntentSurface Intent { get; }

    MediaKindModel Model { get; }

    bool HasReleasePolicy { get; }

    /// <summary>Invokes the statically bound parser through this closed runtime.</summary>
    IRelease? Parse(ReleaseParseContext context);

    ItemView Project(object item);

    FieldValue Read(object item, string fieldId);
}

/// <summary>The typed host runtime available after the registration reopens its type arguments.</summary>
public interface IMediaTypeRuntime<TItem, TTarget, TRelease> : IMediaTypeRuntime
    where TItem : class, IMediaItem
    where TTarget : class, IReleaseTarget
    where TRelease : class, IRelease
{
    ReleasePolicy<TRelease>? ReleasePolicy { get; }
}
