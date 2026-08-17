using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Shape;

/// <summary>
/// Supplies the shape of one media kind.
/// </summary>
/// <remarks>
/// The first of the four seams a media extension implements, and the one everything else depends on: the
/// host validates the shape once at load, resolves every identifier in it to an object, and afterwards
/// no lookup in the host can fail. A property rather than a method because a shape is a constant of the
/// extension, not a computation.
/// </remarks>
[Experimental(ExperimentalContracts.Shape, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaShapeProvider
{
    /// <summary>
    /// Gets the shape. Called once, at load.
    /// </summary>
    MediaShape Shape { get; }
}
