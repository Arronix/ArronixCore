using System.Linq;
using Arronix.Abstractions.Definition;


namespace Arronix.Host.Engines.Matching;

/// <summary>
/// Optimal assignment of readings to units over host-cataloged feature distances: the
/// <c>assignment-over-features</c> member of the match strategy family.
/// </summary>
/// <remarks>
/// <para>
/// Ports the surveyed pipeline the audit verified in Lidarr: pairwise distances from the closed
/// six-operator feature set (<c>DistanceCalculator.cs:89-174</c>) solved by the generic Munkres
/// assignment (<c>Munkres.cs</c>), with weights as data. The features themselves are host code published
/// by <see cref="DistanceFeatureCatalog"/>; a kind's declaration tunes them by identifier and never
/// declares an operator or a subject path.
/// </para>
/// <para>
/// Every behavioral knob is in the request rather than hidden host policy: the accept threshold defaults
/// to the surveyed 0.15 (<c>IdentificationService.cs:162</c>) and is declarable through the strategy
/// binding, so a registry reviewer reading the definition sees the gate.
/// </para>
/// </remarks>
internal sealed class AssignmentOverFeaturesStrategy : IUnitAssignmentStrategy
{
    /// <summary>The surveyed accept gate, applied to the aggregate normalized distance.</summary>
    internal const double DefaultAcceptThreshold = 0.15;

    private readonly DistanceFeatureCatalog _catalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssignmentOverFeaturesStrategy"/> class.
    /// </summary>
    /// <param name="catalog">The host's published feature catalogs.</param>
    internal AssignmentOverFeaturesStrategy(DistanceFeatureCatalog catalog) => _catalog = catalog;

    /// <inheritdoc />
    public string Role => MatchStrategyRoles.UnitAssignment;

    /// <inheritdoc />
    public string StrategyId => "assignment-over-features";

    /// <inheritdoc />
    public AssignmentResult Assign(AssignmentRequest request)
    {
        var features = _catalog.FeaturesOf(request.CatalogId);
        var enabled = new List<(FeatureParameter Parameter, DistanceFeatureCatalog.DistanceFeature Feature)>();
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var parameter in request.Features)
        {
            if (!features.TryGetValue(parameter.FeatureId, out var feature))
            {
                var known = string.Join(", ", features.Keys.Order(StringComparer.Ordinal).Select(id => $"'{id}'"));
                throw new InvalidOperationException(
                    $"Feature '{parameter.FeatureId}' is not published by catalog '{request.CatalogId}'. "
                    + $"Published: {known}.");
            }

            if (!parameter.Enabled)
            {
                continue;
            }

            enabled.Add((parameter, feature));
            weights[parameter.FeatureId] = parameter.Weight;
        }

        if (request.Sources.Count == 0 || request.Targets.Count == 0)
        {
            return new AssignmentResult
            {
                Pairs = [],
                UnassignedSources = Enumerable.Range(0, request.Sources.Count).ToArray(),
                UnassignedTargets = Enumerable.Range(0, request.Targets.Count).ToArray(),
                NormalizedDistance = request.Sources.Count == 0 && request.Targets.Count == 0 ? 0.0 : 1.0,
                AcceptThreshold = request.AcceptThreshold,
            };
        }

        var distances = new double[request.Sources.Count][];
        for (var row = 0; row < request.Sources.Count; row++)
        {
            distances[row] = new double[request.Targets.Count];
            for (var column = 0; column < request.Targets.Count; column++)
            {
                var accumulator = new DistanceAccumulator(weights);
                foreach (var (parameter, feature) in enabled)
                {
                    feature(accumulator, request.Sources[row], request.Targets[column], parameter.Threshold);
                }

                distances[row][column] = accumulator.NormalizedDistance();
            }
        }

        var solver = new MunkresSolver(distances);
        var solution = solver.Solve();

        var pairs = solution
            .Select(pair => new AssignmentPair(pair.Row, pair.Column, distances[pair.Row][pair.Column]))
            .ToArray();

        var assignedSources = pairs.Select(pair => pair.SourceIndex).ToHashSet();
        var assignedTargets = pairs.Select(pair => pair.TargetIndex).ToHashSet();

        // The aggregate follows the surveyed shape: matched-pair distances plus a full penalty per
        // unmatched row on either side, normalized by the larger side.
        var penaltyCount = Math.Max(request.Sources.Count, request.Targets.Count);
        var rawPenalty = pairs.Sum(pair => pair.Distance)
            + (request.Sources.Count - pairs.Length)
            + (request.Targets.Count - pairs.Length);
        var aggregate = penaltyCount > 0 ? Math.Min(rawPenalty / penaltyCount, 1.0) : 0.0;

        return new AssignmentResult
        {
            Pairs = pairs,
            UnassignedSources = Enumerable.Range(0, request.Sources.Count)
                .Where(index => !assignedSources.Contains(index))
                .ToArray(),
            UnassignedTargets = Enumerable.Range(0, request.Targets.Count)
                .Where(index => !assignedTargets.Contains(index))
                .ToArray(),
            NormalizedDistance = aggregate,
            AcceptThreshold = request.AcceptThreshold,
        };
    }
}

/// <summary>
/// The strategy surface the <see cref="MatchStrategyRoles.UnitAssignment"/> role requires.
/// </summary>
internal interface IUnitAssignmentStrategy : IMatchStrategy
{
    /// <summary>
    /// Assigns sources to targets by least total feature distance.
    /// </summary>
    /// <param name="request">The problem and its declared tuning.</param>
    /// <returns>The assignment.</returns>
    AssignmentResult Assign(AssignmentRequest request);
}

/// <summary>
/// One assignment problem, with the declared tuning it runs under.
/// </summary>
internal sealed record AssignmentRequest
{
    /// <summary>
    /// Gets the feature catalog the distances run on.
    /// </summary>
    public string CatalogId { get; init; } = DistanceFeatureCatalog.UnitDistance;

    /// <summary>
    /// Gets the per-feature tuning rows. A row naming an unpublished feature fails the request.
    /// </summary>
    public required IReadOnlyList<FeatureParameter> Features { get; init; }

    /// <summary>
    /// Gets the greatest aggregate normalized distance at which the assignment is acceptable.
    /// </summary>
    public double AcceptThreshold { get; init; } = AssignmentOverFeaturesStrategy.DefaultAcceptThreshold;

    /// <summary>
    /// Gets the reading-side rows.
    /// </summary>
    public required IReadOnlyList<AssignmentCandidate> Sources { get; init; }

    /// <summary>
    /// Gets the unit-side rows.
    /// </summary>
    public required IReadOnlyList<AssignmentCandidate> Targets { get; init; }
}

/// <summary>
/// One matched pair of an assignment.
/// </summary>
/// <param name="SourceIndex">The index of the source row.</param>
/// <param name="TargetIndex">The index of the target row it was assigned to.</param>
/// <param name="Distance">The pair's normalized distance.</param>
internal readonly record struct AssignmentPair(int SourceIndex, int TargetIndex, double Distance);

/// <summary>
/// What an assignment decided.
/// </summary>
internal sealed record AssignmentResult
{
    /// <summary>
    /// Gets the matched pairs.
    /// </summary>
    public required IReadOnlyList<AssignmentPair> Pairs { get; init; }

    /// <summary>
    /// Gets the source indexes no target was assigned to.
    /// </summary>
    public required IReadOnlyList<int> UnassignedSources { get; init; }

    /// <summary>
    /// Gets the target indexes no source was assigned to.
    /// </summary>
    public required IReadOnlyList<int> UnassignedTargets { get; init; }

    /// <summary>
    /// Gets the aggregate normalized distance: matched-pair distances plus unmatched penalties.
    /// </summary>
    public required double NormalizedDistance { get; init; }

    /// <summary>
    /// Gets the declared accept gate the aggregate is judged against.
    /// </summary>
    public required double AcceptThreshold { get; init; }

    /// <summary>
    /// Gets a value indicating whether the assignment clears the accept gate.
    /// </summary>
    public bool IsAcceptable => NormalizedDistance <= AcceptThreshold;
}
