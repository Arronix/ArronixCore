using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>
/// One media kind's runtime model: the derived descriptors, the item type, and the projections a consumer
/// that cannot name the item type needs.
/// </summary>
/// <remarks>
/// <para>
/// Built by the host from an <see cref="IMediaType{TItem}"/>; never implemented by a plugin. Every member
/// is either derived from the item type or carried verbatim from the builder, so there is no second source
/// of truth and nothing here can disagree with the entity.
/// </para>
/// <para>
/// Not on it, deliberately: anything returning a <see cref="Task"/>. This is a model, not a service.
/// Fetching, projecting from a catalog and everything else that takes time stays on the seams that already
/// own it.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaType
{
    /// <summary>
    /// Gets the media kind identifier.
    /// </summary>
    MediaKindId Kind { get; }

    /// <summary>
    /// Gets the item type, so a serializer and a data mapper both have a target.
    /// </summary>
    Type ItemType { get; }

    /// <summary>
    /// Gets the group types this kind declares, if any.
    /// </summary>
    IReadOnlyList<Type> GroupTypes { get; }

    /// <summary>
    /// Gets the derived structure — the same descriptor every engine already reads.
    /// </summary>
    MediaShape Shape { get; }

    /// <summary>
    /// Gets the derived intent surface.
    /// </summary>
    PluginIntentSurface Intent { get; }

    /// <summary>
    /// Gets the release, match, query, naming, quality and summary models the engines compile.
    /// </summary>
    MediaKindModel Model { get; }

    /// <summary>
    /// Projects one typed item onto the descriptor-shaped view a consumer that cannot load the assembly
    /// reads.
    /// </summary>
    /// <param name="item">An instance of <see cref="ItemType"/>.</param>
    /// <returns>The view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="item"/> is not an instance of <see cref="ItemType"/>.
    /// </exception>
    ItemView Project(object item);

    /// <summary>
    /// Reads one field off a typed item by its derived identifier, for a generic sort or filter that
    /// should not have to project a whole item to order one column.
    /// </summary>
    /// <param name="item">An instance of <see cref="ItemType"/>.</param>
    /// <param name="fieldId">A <see cref="FieldDescriptor.FieldId"/> from <see cref="Shape"/>.</param>
    /// <returns>The value, or an absent value when the item carries none.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="item"/> is not an instance of <see cref="ItemType"/>, or
    /// <paramref name="fieldId"/> names no field of the item level.
    /// </exception>
    FieldValue Read(object item, string fieldId);
}
