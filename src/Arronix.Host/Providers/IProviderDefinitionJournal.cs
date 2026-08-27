using Arronix.Abstractions.Providers;

namespace Arronix.Host.Providers;

/// <summary>
/// Where the operator's configured provider instances are kept between processes.
/// </summary>
/// <remarks>
/// The rule stays in <see cref="ProviderDefinitionStore"/>; this is only where its answers are written down.
/// Whether an implementation is currently present is not written: that is recomputed against the loaded
/// registry every time the installation changes, and a stored answer would be a stale one.
/// </remarks>
internal interface IProviderDefinitionJournal
{
    /// <summary>Reads every definition previously written, in identifier order.</summary>
    /// <returns>The definitions.</returns>
    IReadOnlyList<ProviderDefinition> Load();

    /// <summary>Writes one definition, replacing any held under the same identifier.</summary>
    /// <param name="definition">The definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when it is stored.</returns>
    ValueTask WriteAsync(ProviderDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Removes one definition.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when it is gone.</returns>
    ValueTask DeleteAsync(int id, CancellationToken cancellationToken = default);
}
