using System.Linq;
using Arronix.Abstractions.Shape;
using Arronix.Abstractions.Wire;
using Arronix.Host.Storage;

// Shape and wire contracts are experimental; completeness is computed from the first and published as the
// second.
#pragma warning disable ARX0013
#pragma warning disable ARX0017

namespace Arronix.Host.Media;

/// <summary>
/// One unit that completeness might count.
/// </summary>
/// <param name="Unit">The unit.</param>
/// <param name="Coordinates">Its position, which decides whether a declared exception excludes it.</param>
/// <param name="Wanted">Whether the user has said they want it.</param>
/// <param name="Variant">
/// The manifestation this unit belongs to, for shapes that count completeness against the chosen one.
/// </param>
public readonly record struct CompletenessCandidate(
    MediaItemRef Unit,
    CoordinateSet Coordinates,
    bool Wanted,
    MediaItemRef? Variant);

/// <summary>
/// Counts what is present against what is wanted.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no completeness contract for an extension to implement, because there is nothing
/// left for one to decide. Every surveyed rule falls out of declarations already made: which level counts
/// comes from the completeness role, which coordinates do not count comes from the declared sequence
/// exceptions, and whether the count is taken against one chosen manifestation or the union of all of them
/// comes from the variant selection. A policy seam here would be a place for four implementations of one
/// arithmetic to diverge.
/// </para>
/// <para>
/// The two rules that motivated it are worth naming. One surveyed application counts a work complete when
/// the pressing the user selected is complete, not when any pressing is; another excludes a reserved
/// sequence value — the one every hard-coded "greater than zero" test in its naming and statistics code was
/// written for. Both are now declarations rather than conditionals.
/// </para>
/// </remarks>
/// <param name="store">Where the links and file sizes are read from.</param>
public sealed class CompletenessCalculator(IMediaStore store)
{
    private readonly IMediaStore _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>
    /// Counts a set of candidate units.
    /// </summary>
    /// <param name="shape">The validated shape the units belong to.</param>
    /// <param name="candidates">Every unit that could count, before exclusions.</param>
    /// <param name="selectedVariant">The chosen manifestation, when the parent has one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What is present, what is wanted, how many there are and how much space they take.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="shape"/> or <paramref name="candidates"/> is <see langword="null"/>.
    /// </exception>
    public async ValueTask<ProgressSummary> ComputeAsync(
        ValidatedShape shape,
        IReadOnlyList<CompletenessCandidate> candidates,
        MediaItemRef? selectedVariant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(candidates);

        var variantRelative = shape.VariantLevel?.Variant?.CompletenessIsVariantRelative == true
            && selectedVariant is not null;

        var excluded = ExcludedValues(shape);

        var total = 0;
        var want = 0;
        var have = 0;
        var size = 0L;
        var countedFiles = new HashSet<MediaFileId>();

        foreach (var candidate in candidates)
        {
            if (variantRelative && candidate.Variant != selectedVariant)
            {
                continue;
            }

            if (IsExcluded(excluded, candidate.Coordinates))
            {
                continue;
            }

            total++;

            var links = await _store.LinksForUnitAsync(candidate.Unit, cancellationToken).ConfigureAwait(false);

            if (candidate.Wanted)
            {
                want++;

                if (links.Count > 0)
                {
                    have++;
                }
            }

            foreach (var link in links)
            {
                // A file satisfying several units is one file on disk; counting its bytes once per unit
                // would report a library several times its real size.
                if (!countedFiles.Add(link.File))
                {
                    continue;
                }

                var file = await _store.FindFileAsync(link.File, cancellationToken).ConfigureAwait(false);

                if (file is not null)
                {
                    size += file.Size;
                }
            }
        }

        return new ProgressSummary(have, want, total, size);
    }

    private static IReadOnlyList<(string SpaceId, int ComponentIndex, long Value)> ExcludedValues(ValidatedShape shape)
    {
        var excluded = new List<(string SpaceId, int ComponentIndex, long Value)>();

        foreach (var axis in shape.AllSequenceAxes())
        {
            foreach (var exception in axis.Exceptions.Where(candidate => candidate.ExcludedFromCompleteness))
            {
                excluded.Add((axis.SpaceId, axis.ComponentIndex, exception.Value));
            }
        }

        return excluded;
    }

    private static bool IsExcluded(
        IReadOnlyList<(string SpaceId, int ComponentIndex, long Value)> excluded,
        CoordinateSet coordinates)
    {
        foreach (var (spaceId, componentIndex, value) in excluded)
        {
            if (!coordinates.TryGet(spaceId, out var reading)
                || reading.Value.Kind != CoordinateKind.Ordinal
                || componentIndex >= reading.Value.Ordinals.Length)
            {
                continue;
            }

            if (reading.Value.Ordinals[componentIndex] == value)
            {
                return true;
            }
        }

        return false;
    }
}
