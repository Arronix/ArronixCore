using Arronix.Abstractions.Shape;


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
