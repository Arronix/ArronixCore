using System.Linq;

namespace Arronix.Host.Engines.Matching;

/// <summary>
/// A host-published catalog of distance features: what each feature computes is host code, and a kind
/// tunes them by identifier — enablement, weight, threshold — never by declaring operators or subject
/// paths.
/// </summary>
/// <remarks>
/// <para>
/// The honest parameter surface: the surveyed feature arguments are imperative aggregations (modal
/// values, fallback chains, dynamic maxima) a path grammar cannot reach, so the features live here and
/// only their tuning is data. The priced consequence: a novel distance feature costs a host release, not
/// a declaration edit.
/// </para>
/// <para>
/// The built-in features port the per-row computations the audit verified in Lidarr's
/// <c>DistanceCalculator</c>
/// (<c>_reference/Lidarr/src/NzbDrone.Core/MediaFiles/TrackImport/Identification/DistanceCalculator.cs:27-131</c>):
/// the string title distance, the position-mismatch bool, the length ratio with its ten-second grace and
/// thirty-second cap, the identifier-mismatch bool, and the year ratio with its dynamic
/// now-relative maximum — the one feature that needs a clock, taken as <see cref="TimeProvider"/>.
/// </para>
/// </remarks>
internal sealed class DistanceFeatureCatalog
{
    /// <summary>The catalog identifier of the host's built-in reading-to-unit feature set.</summary>
    internal const string UnitDistance = "unit-distance";

    private readonly Dictionary<string, IReadOnlyDictionary<string, DistanceFeature>> _catalogs;

    private DistanceFeatureCatalog(
        Dictionary<string, IReadOnlyDictionary<string, DistanceFeature>> catalogs) => _catalogs = catalogs;

    /// <summary>
    /// One feature's computation: reads a source-and-target pair and feeds penalties to the accumulator.
    /// </summary>
    /// <param name="accumulator">The accumulator to feed.</param>
    /// <param name="source">The reading-side candidate.</param>
    /// <param name="target">The unit-side candidate.</param>
    /// <param name="threshold">The declared threshold override, when the feature takes one.</param>
    internal delegate void DistanceFeature(
        DistanceAccumulator accumulator,
        AssignmentCandidate source,
        AssignmentCandidate target,
        double? threshold);

    /// <summary>
    /// Creates the catalog carrying the host's built-in feature sets.
    /// </summary>
    /// <param name="clock">The clock the year feature computes its dynamic maximum from.</param>
    /// <returns>The catalog.</returns>
    internal static DistanceFeatureCatalog CreateDefault(TimeProvider clock)
    {
        var unitDistance = new Dictionary<string, DistanceFeature>(StringComparer.Ordinal)
        {
            // DistanceCalculator.cs:53 — the title string distance.
            ["title"] = static (accumulator, source, target, _) =>
                accumulator.AddString("title", source.Title, target.Title),

            // DistanceCalculator.cs:27-31, 63-66 — position mismatch, only when both sides state one.
            ["position"] = static (accumulator, source, target, _) =>
            {
                if (source.Position is > 0 && target.Position is > 0)
                {
                    accumulator.AddBool("position", source.Position != target.Position);
                }
            },

            // DistanceCalculator.cs:42-48 — |local − expected| with a 10-second grace, ratio capped at
            // 30 seconds; the cap is the feature's declarable threshold.
            ["length"] = static (accumulator, source, target, threshold) =>
            {
                if (source.Length is not { } sourceLength || target.Length is not { TotalSeconds: > 0 } targetLength)
                {
                    return;
                }

                var difference = Math.Abs(sourceLength.TotalSeconds - targetLength.TotalSeconds) - 10;
                accumulator.AddRatio("length", difference, threshold ?? 30);
            },

            // DistanceCalculator.cs:68-73 — an identifier stated by the reading that names none of the
            // unit's identifiers, historical included, is a strong mismatch.
            ["identifier"] = static (accumulator, source, target, _) =>
            {
                if (source.ExternalIds.Count == 0)
                {
                    return;
                }

                var mismatch = !source.ExternalIds.Any(stated => target.ExternalIds.Contains(stated));
                accumulator.AddBool("identifier", mismatch);
            },

            // DistanceCalculator.cs:116-131 — exact year agreement is free; disagreement is a ratio over
            // the distance from the present, so an old recording mislabeled by one year hurts more than
            // a current one.
            ["year"] = (accumulator, source, target, _) =>
            {
                if (source.Year is not { } stated || stated <= 0 || target.Year is not { } expected || expected <= 0)
                {
                    return;
                }

                if (stated == expected)
                {
                    accumulator.Add("year", 0.0);
                    return;
                }

                var difference = Math.Abs(stated - expected);
                var maximum = Math.Abs(clock.GetUtcNow().Year - expected);
                accumulator.AddRatio("year", difference, maximum);
            },
        };

        return new DistanceFeatureCatalog(new Dictionary<string, IReadOnlyDictionary<string, DistanceFeature>>(StringComparer.Ordinal)
        {
            [UnitDistance] = unitDistance,
        });
    }

    /// <summary>
    /// Returns one catalog's features.
    /// </summary>
    /// <param name="catalogId">The catalog wanted.</param>
    /// <returns>The features, keyed by feature identifier.</returns>
    /// <exception cref="InvalidOperationException">The identifier names no published catalog.</exception>
    internal IReadOnlyDictionary<string, DistanceFeature> FeaturesOf(string catalogId)
    {
        if (!_catalogs.TryGetValue(catalogId, out var features))
        {
            var known = string.Join(", ", _catalogs.Keys.Order(StringComparer.Ordinal).Select(id => $"'{id}'"));
            throw new InvalidOperationException(
                $"'{catalogId}' names no published feature catalog. Published: {known}.");
        }

        return features;
    }
}
