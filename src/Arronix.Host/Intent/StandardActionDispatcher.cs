using System.Linq;
using Arronix.Abstractions.Intent;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Host.Media.Typed;
using Arronix.Host.Storage;


namespace Arronix.Host.Intent;

/// <summary>Executes standard actions for which the host currently owns the complete lifecycle.</summary>
/// <remarks>
/// A null result means the operation is standard but its execution capability is not installed yet. This
/// keeps declaration separate from execution without silently accepting work that cannot be performed.
/// </remarks>
public interface IStandardActionDispatcher
{
    /// <summary>Attempts to execute one standard action.</summary>
    ValueTask<ActionResult?> TryDispatchAsync(
        ActionDescriptor action,
        IReadOnlyList<MediaItemRef> items,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>Executes the host-owned standard media operations.</summary>
internal sealed class StandardActionDispatcher(IMediaStore store, TimeProvider clock) : IStandardActionDispatcher
{
    private readonly IMediaStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public async ValueTask<ActionResult?> TryDispatchAsync(
        ActionDescriptor action,
        IReadOnlyList<MediaItemRef> items,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(parameters);

        if (action.StandardAction is not StandardMediaAction.SetMonitoring)
        {
            return null;
        }

        var wantedParameter = action.Parameters.SingleOrDefault(parameter =>
            parameter.StandardParameter is StandardMediaActionParameter.Wanted);
        if (wantedParameter is null
            || !parameters.TryGetValue(wantedParameter.ParameterId, out var text)
            || !bool.TryParse(text, out var wanted))
        {
            return new ActionResult(
                false,
                null,
                "'Wanted' must be true or false.",
                null);
        }

        foreach (var item in items)
        {
            var current = await _store.FindLibraryAsync(item, cancellationToken).ConfigureAwait(false);
            var monitor = current is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(current.Monitor, StringComparer.Ordinal);
            monitor[ShapeDerivation.WantedDimension.DimensionId] = wanted ? "true" : "false";

            await _store.UpsertLibraryAsync(
                (current ?? new LibraryFacet { Ref = item }) with
                {
                    Monitor = monitor,
                    AddedAt = current?.AddedAt ?? (wanted ? _clock.GetUtcNow() : null)
                },
                cancellationToken).ConfigureAwait(false);
        }

        return new ActionResult(
            true,
            null,
            wanted ? "Selected items are wanted." : "Selected items are not wanted.",
            null);
    }
}
