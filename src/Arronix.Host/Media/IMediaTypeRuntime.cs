using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Parsing;
using Arronix.Abstractions.Releases;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media.Catalog;


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

    /// <summary>Projects one entity into the descriptor-shaped view.</summary>
    /// <param name="reference">The host-owned reference the entity is held under.</param>
    /// <param name="item">The entity.</param>
    /// <param name="identity">Host identity state, used to address the entity's group references.</param>
    /// <returns>The view.</returns>
    ItemView Project(MediaItemRef reference, object item, CatalogIdentity identity);

    /// <summary>Reads one field off one entity.</summary>
    /// <param name="item">The entity.</param>
    /// <param name="fieldId">The field identifier.</param>
    /// <param name="identity">Host identity state, used to address the entity's group references.</param>
    /// <returns>The value.</returns>
    FieldValue Read(object item, string fieldId, CatalogIdentity identity);
}

/// <summary>The typed host runtime available after the registration reopens its type arguments.</summary>
public interface IMediaTypeRuntime<TItem, TTarget, TRelease> : IMediaTypeRuntime
    where TItem : class, IMediaItem
    where TTarget : class, IReleaseTarget
    where TRelease : class, IRelease
{
    ReleasePolicy<TRelease>? ReleasePolicy { get; }
}
