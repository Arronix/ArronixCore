using Arronix.Abstractions.Shape;

// The media-shape contracts are experimental; this seam is the query engine's read window over them.
#pragma warning disable ARX0013

namespace Arronix.Host.Engines.Search;

/// <summary>
/// The read window the query templater sees items through: exactly the lookups templating needs and
/// nothing a planner could grow query-shaped dependencies on.
/// </summary>
internal interface IQueryItemReader
{
    /// <summary>
    /// Returns one item.
    /// </summary>
    /// <param name="reference">The item wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item, or <see langword="null"/> when there is no such item.</returns>
    Task<ItemView?> GetAsync(MediaItemRef reference, CancellationToken cancellationToken = default);
}
