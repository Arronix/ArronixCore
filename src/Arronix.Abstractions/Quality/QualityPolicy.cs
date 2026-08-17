using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Arronix.Abstractions.Quality;

/// <summary>One user's stated preference over one format family's axes.</summary>
/// <remarks>
/// <para>
/// Compiled against exactly one <see cref="IQualityType"/>. A point of another family is rejected at
/// runtime by <see cref="Compare"/> and <see cref="Admits"/>, <b>not</b> at compile time: the host stores
/// points, serializes them and holds them in heterogeneous collections, so a non-generic erasure is
/// required anyway and a generic point would buy a compile-time guarantee at the authoring seam only to
/// re-introduce the runtime check at every storage boundary. One guard, in one place, with one test on it.
/// </para>
/// <para>
/// The four sections are four different questions and are deliberately not merged into one score. What to
/// <i>prefer</i> orders candidates that are all acceptable; what to <i>refuse</i> decides acceptability;
/// what is a <i>bonus</i> separates candidates the order left tied; and what is <i>good enough</i> decides
/// when to stop. Merging all of them into one number is what forces a magic rejection value.
/// </para>
/// <para>
/// <b>The guarantee this type owes: <see cref="Compare"/> cannot cycle.</b> A comparison admitting a
/// strict preference cycle is not a corner case, it is an unbounded download loop, because a grab happens
/// whenever the comparison says "better". Every <see cref="AxisPreference"/> is by construction a function
/// from points into a totally ordered set — <see cref="PreferenceUnknown"/> maps an absent reading to a
/// fixed element, and ceilings, floors, rankings and polarity are monotone or order-reversing maps — so
/// each entry induces a total preorder, the lexicographic composition of finitely many total preorders is
/// a total preorder, and <see cref="FacetScoring.Of"/> composes as a further lexicographic factor because
/// it is a function of one point. A total preorder is transitive, and a transitive relation has no strict
/// cycles.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class QualityPolicy
{
    private readonly Dictionary<QualityAxisId, QualityAxis> axes;

    private QualityPolicy(
        IQualityType type,
        Dictionary<QualityAxisId, QualityAxis> axes,
        IReadOnlyList<AxisPreference> precedence,
        IReadOnlyList<AxisRequirement> requirements,
        FacetScoring facets,
        CutoffPredicate cutoff)
    {
        this.axes = axes;
        Type = type;
        Precedence = precedence;
        Requirements = requirements;
        Facets = facets;
        Cutoff = cutoff;
    }

    /// <summary>Gets the family this policy is over.</summary>
    public IQualityType Type { get; }

    /// <summary>Gets the axes that order candidates, most significant first. The core tier.</summary>
    /// <remarks>
    /// <para>
    /// An axis absent from this list never orders anything, though it may still refuse through
    /// <see cref="Requirements"/> or score through <see cref="Facets"/>.
    /// </para>
    /// <para>
    /// Absence is not the equivalence mechanism it looks like. Dropping an axis to tie two candidates ties
    /// every pair that axis separates, not the one pair meant; a <see cref="AxisPreference.Ceiling"/> ties
    /// exactly the range asked for and leaves the axis live below it.
    /// </para>
    /// </remarks>
    public IReadOnlyList<AxisPreference> Precedence { get; }

    /// <summary>Gets what makes a candidate ineligible regardless of how it ranks.</summary>
    public IReadOnlyList<AxisRequirement> Requirements { get; }

    /// <summary>Gets the bounded scores consulted only when <see cref="Precedence"/> leaves a tie.</summary>
    public FacetScoring Facets { get; }

    /// <summary>Gets when a held file is good enough to stop looking.</summary>
    public CutoffPredicate Cutoff { get; }

    /// <summary>Builds a policy against a family's quality type.</summary>
    /// <param name="type">The family's quality type.</param>
    /// <param name="configure">The declaration.</param>
    /// <returns>The compiled policy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> or <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The declaration names an axis the family does not declare; orders one axis twice; places an
    /// unordered axis in <see cref="Precedence"/>; gives one axis both a precedence entry and a facet
    /// score; declares more than <see cref="FacetScoring.MaximumFacets"/> facets or a facet point outside
    /// plus or minus <see cref="FacetScoring.PointsPerFacet"/>; states a requirement that names neither a
    /// value nor a bound; or re-ranks an axis without naming every one of its members exactly once.
    /// </exception>
    /// <remarks>
    /// An analyzer can cover policies written in source; this covers policies a <b>user</b> composed in
    /// the editor, which no analyzer ever sees. Both are needed, and the second is the one the
    /// cycle-safety argument depends on.
    /// </remarks>
    public static QualityPolicy For(IQualityType type, Action<IQualityPolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new PolicyBuilder();
        configure(builder);

        var declared = new Dictionary<QualityAxisId, QualityAxis>();

        foreach (var axis in type.Axes)
        {
            declared[axis.Id] = axis;
        }

        var precedence = ValidatePrecedence(builder, declared);
        var facets = ValidateFacets(builder, declared, precedence);
        var requirements = ValidateRequirements(builder, declared);
        var floors = ValidateFloors(builder, declared);

        var cutoff = new CutoffPredicate
        {
            Floors = floors,
            UpgradesEnabled = builder.UpgradesEnabled,
            Declared = declared,
        };

        return new QualityPolicy(type, declared, precedence, requirements, facets, cutoff);
    }

    /// <summary>Ranks two points of this family.</summary>
    /// <param name="held">The point already held.</param>
    /// <param name="candidate">The candidate's point.</param>
    /// <returns>The judgment.</returns>
    /// <exception cref="ArgumentNullException">Either point is null.</exception>
    /// <exception cref="ArgumentException">Either point belongs to another family.</exception>
    /// <remarks>
    /// A pure ordering and nothing else. It knows nothing about downloads, and in particular it does
    /// <b>not</b> carry the provenance rule: that is a rule about whether to <i>act</i>, it is pairwise
    /// and asymmetric rather than pointwise, and folding it in here would destroy the transitivity the
    /// whole tier rests on. It lives in <see cref="Decide"/>.
    /// </remarks>
    public QualityJudgment Compare(QualityPoint held, QualityPoint candidate) =>
        CompareCore(held, candidate, out _);

    /// <summary>Decides whether a candidate is eligible at all.</summary>
    /// <param name="candidate">The candidate's point.</param>
    /// <returns>The verdict, carrying the requirement that refused it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidate"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="candidate"/> belongs to another family.</exception>
    public Eligibility Admits(QualityPoint candidate) => AdmitCore(candidate, out _);

    /// <summary>Decides whether a held file is good enough to stop looking for upgrades.</summary>
    /// <param name="held">The point already held.</param>
    /// <returns><see langword="true"/> when every cutoff floor is met.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="held"/> is null.</exception>
    public bool IsGoodEnough(QualityPoint held) => Cutoff.IsSatisfiedBy(held);

    /// <summary>Decides whether to take a candidate, and says why in one sentence.</summary>
    /// <param name="held">The point already held, or null when nothing is.</param>
    /// <param name="candidate">The candidate's point.</param>
    /// <returns>The decision and its reason.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidate"/> is null.</exception>
    /// <exception cref="ArgumentException">Either point belongs to another family.</exception>
    /// <remarks>
    /// <para>
    /// Walks a fixed order, and every branch produces a sentence naming the axis that decided.
    /// </para>
    /// <para>
    /// <b>The provenance rule lives here.</b> When the comparison reports an upgrade and the deciding
    /// axis's candidate reading is at a strictly weaker <see cref="EvidenceSource"/> than the held
    /// reading's on that same axis, the answer is <see cref="GrabVerdict.NotAnUpgrade"/>: a claim never
    /// outranks a measurement. Without it, a title over-claiming a resolution we have since measured on
    /// the held file is re-grabbed on every pass, forever. It does not fall through to the next axis —
    /// falling through is skipping by another name, with the same cycle — and it terminates, because
    /// after an import the held reading sits at a probe source no claim can exceed.
    /// </para>
    /// </remarks>
    public GrabDecision Decide(QualityPoint? held, QualityPoint candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        RequireFamily(candidate, nameof(candidate));

        var eligibility = AdmitCore(candidate, out var refusedForSilence);

        if (!eligibility.IsAdmitted)
        {
            var requirement = eligibility.RefusedBy!;
            var refusedAxis = NameOf(requirement.Axis);

            if (refusedForSilence)
            {
                return new GrabDecision(
                    GrabVerdict.EvidenceInsufficient,
                    $"Refused: nothing in the release says what its {refusedAxis} is.");
            }

            var refusedValue = Spell(candidate[requirement.Axis]);

            return new GrabDecision(
                GrabVerdict.Refused,
                requirement.Reason ?? $"Refused: {refusedAxis} is {refusedValue}, which you never take.");
        }

        if (held is null)
        {
            return new GrabDecision(GrabVerdict.Grab, "Nothing held.");
        }

        RequireFamily(held, nameof(held));

        var judgment = CompareCore(held, candidate, out var decidedBy);

        if (judgment == QualityJudgment.Better && IsGoodEnough(held))
        {
            return new GrabDecision(
                GrabVerdict.AlreadyGoodEnough,
                $"Already good enough: {Type.Label(held, QualityLabelDetail.Standard)}.");
        }

        if (judgment is QualityJudgment.Worse or QualityJudgment.Same)
        {
            return new GrabDecision(GrabVerdict.NotAnUpgrade, NotAnUpgrade(held, candidate, decidedBy));
        }

        if (judgment == QualityJudgment.Incomparable)
        {
            return new GrabDecision(
                GrabVerdict.EvidenceInsufficient,
                $"Cannot tell: {NameOf(decidedBy)} has no reading on one side.");
        }

        if (decidedBy.IsNamed && ClaimOutrunsMeasurement(held, candidate, decidedBy))
        {
            return new GrabDecision(
                GrabVerdict.NotAnUpgrade,
                $"Not an upgrade: the release claims {NameOf(decidedBy)} {Spell(candidate[decidedBy])}; "
                + $"we measured {Spell(held[decidedBy])} on the file we hold.");
        }

        return new GrabDecision(GrabVerdict.Grab, Upgrade(held, candidate, decidedBy));
    }

    /// <summary>Renders the policy as English a person can read.</summary>
    /// <returns>The sentence.</returns>
    /// <remarks>
    /// Every clause is generated, including the last: the provenance rule is rendered because it is
    /// behavior a user would otherwise have to discover. A clause for a requirement the policy does not
    /// hold is not rendered at all — the prose says what the policy does, never what it was going to do.
    /// </remarks>
    public string Describe()
    {
        var prose = new StringBuilder();

        if (Precedence.Count == 0)
        {
            prose.Append("Order nothing.");
        }
        else
        {
            prose.Append("Prefer ");

            for (var index = 0; index < Precedence.Count; index++)
            {
                if (index > 0)
                {
                    prose.Append(" — then ");
                }

                prose.Append(DescribePreference(Precedence[index]));
            }

            prose.Append('.');
        }

        if (Facets.Scores.Count > 0)
        {
            prose.Append(" Among releases none of that separates, count ");
            prose.Append(DescribeFacets());
            prose.Append(" as a bonus.");
        }

        if (Cutoff.Floors.Count > 0)
        {
            prose.Append(" Good enough at ");
            prose.Append(DescribeFloors());
            prose.Append('.');
        }

        var refusals = DescribeRefusals();

        if (refusals.Length > 0)
        {
            prose.Append(" Never take ");
            prose.Append(refusals);
            prose.Append('.');
        }

        prose.Append(" Never re-download on a claim we have already measured.");

        return prose.ToString();
    }

    /// <summary>Ranks two points and names the precedence axis that decided.</summary>
    /// <param name="held">The point already held.</param>
    /// <param name="candidate">The candidate's point.</param>
    /// <param name="decidedBy">
    /// Receives the axis that decided, or <see cref="QualityAxisId.None"/> when the core tier tied and the
    /// facet tier settled it.
    /// </param>
    /// <returns>The judgment.</returns>
    internal QualityJudgment CompareCore(QualityPoint held, QualityPoint candidate, out QualityAxisId decidedBy)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(candidate);
        RequireFamily(held, nameof(held));
        RequireFamily(candidate, nameof(candidate));

        decidedBy = QualityAxisId.None;

        foreach (var preference in Precedence)
        {
            var axis = axes[preference.Axis];
            var heldKey = KeyOf(held, preference, axis);
            var candidateKey = KeyOf(candidate, preference, axis);

            if (candidateKey > heldKey)
            {
                decidedBy = preference.Axis;

                return QualityJudgment.Better;
            }

            if (candidateKey < heldKey)
            {
                decidedBy = preference.Axis;

                return QualityJudgment.Worse;
            }
        }

        var heldScore = Facets.Of(held);
        var candidateScore = Facets.Of(candidate);

        return candidateScore > heldScore ? QualityJudgment.Better
            : candidateScore < heldScore ? QualityJudgment.Worse
            : QualityJudgment.Same;
    }

    /// <summary>Tests every requirement, and says whether a refusal was for silence rather than for a value.</summary>
    /// <param name="candidate">The candidate's point.</param>
    /// <param name="refusedForSilence">Receives whether the refusal was caused by an absent reading.</param>
    /// <returns>The verdict.</returns>
    internal Eligibility AdmitCore(QualityPoint candidate, out bool refusedForSilence)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        RequireFamily(candidate, nameof(candidate));

        refusedForSilence = false;

        foreach (var requirement in Requirements)
        {
            if (Satisfies(requirement, candidate, out var bySilence))
            {
                continue;
            }

            refusedForSilence = bySilence;

            return new Eligibility(false, requirement);
        }

        return new Eligibility(true, null);
    }

    private static IReadOnlyList<AxisPreference> ValidatePrecedence(
        PolicyBuilder builder,
        Dictionary<QualityAxisId, QualityAxis> declared)
    {
        var seen = new HashSet<QualityAxisId>();

        foreach (var preference in builder.Preferences)
        {
            var axis = Declared(declared, preference.Axis);

            if (!seen.Add(preference.Axis))
            {
                throw new ArgumentException(
                    $"'{preference.Axis}' appears twice in the ordering; an axis has one place in it.",
                    nameof(builder));
            }

            if (axis.Form == AxisForm.Nominal)
            {
                throw new ArgumentException(
                    $"'{preference.Axis}' has no order, so it cannot order anything. Score it as a facet instead.",
                    nameof(builder));
            }

            ValidateRanking(preference, axis);
        }

        return [.. builder.Preferences];
    }

    private static void ValidateRanking(AxisPreference preference, QualityAxis axis)
    {
        if (preference.Ranking.Count == 0)
        {
            return;
        }

        if (axis.Form != AxisForm.Ordinal)
        {
            throw new ArgumentException(
                $"'{preference.Axis}' is a quantity, and a quantity has no members to re-rank.",
                nameof(preference));
        }

        var ranked = 0;

        foreach (var group in preference.Ranking)
        {
            ranked += group.Count;
        }

        if (ranked != axis.Members.Count)
        {
            throw new ArgumentException(
                $"A ranking replaces '{preference.Axis}'s declared order, so it must name all "
                + $"{axis.Members.Count} of its members exactly once; this one names {ranked}.",
                nameof(preference));
        }

        foreach (var member in axis.Members)
        {
            var found = 0;

            foreach (var group in preference.Ranking)
            {
                foreach (var ranking in group)
                {
                    if (ranking.Names(member))
                    {
                        found++;
                    }
                }
            }

            if (found != 1)
            {
                throw new ArgumentException(
                    $"'{preference.Axis}'s member '{member.Token}' is named {found} times in its ranking; "
                    + "a ranking names every member exactly once.",
                    nameof(preference));
            }
        }
    }

    private static FacetScoring ValidateFacets(
        PolicyBuilder builder,
        Dictionary<QualityAxisId, QualityAxis> declared,
        IReadOnlyList<AxisPreference> precedence)
    {
        if (builder.Facets.Count == 0)
        {
            return FacetScoring.None;
        }

        if (builder.Facets.Count > FacetScoring.MaximumFacets)
        {
            throw new ArgumentException(
                $"A policy declares at most {FacetScoring.MaximumFacets} facets; this one declares "
                + $"{builder.Facets.Count}. The bound is what stops the bonus tier growing into a second "
                + "ordering.",
                nameof(builder));
        }

        var ordered = new HashSet<QualityAxisId>();

        foreach (var preference in precedence)
        {
            ordered.Add(preference.Axis);
        }

        var scores = new List<FacetScore>(builder.Facets.Count);
        var seen = new HashSet<QualityAxisId>();

        foreach (var draft in builder.Facets)
        {
            Declared(declared, draft.Axis);

            if (!seen.Add(draft.Axis))
            {
                throw new ArgumentException($"'{draft.Axis}' is scored twice.", nameof(builder));
            }

            if (ordered.Contains(draft.Axis))
            {
                throw new ArgumentException(
                    $"'{draft.Axis}' both orders and scores. An axis is in the ordering or in the bonus "
                    + "tier, never both — that disjointness is what the cycle-safety argument rests on, and "
                    + "it is what keeps a user from having to reason about an axis fighting itself.",
                    nameof(builder));
            }

            foreach (var entry in draft.Points)
            {
                if (Math.Abs(entry.Value) > FacetScoring.PointsPerFacet)
                {
                    throw new ArgumentException(
                        $"'{draft.Axis}' prices '{entry.Key.Token}' at {entry.Value.ToString(CultureInfo.InvariantCulture)}, "
                        + $"outside plus or minus {FacetScoring.PointsPerFacet.ToString(CultureInfo.InvariantCulture)}.",
                        nameof(builder));
                }
            }

            scores.Add(draft.ToScore());
        }

        return new FacetScoring { Scores = scores };
    }

    private static IReadOnlyList<AxisRequirement> ValidateRequirements(
        PolicyBuilder builder,
        Dictionary<QualityAxisId, QualityAxis> declared)
    {
        foreach (var requirement in builder.Requirements)
        {
            Declared(declared, requirement.Axis);

            if (requirement.Values.Count == 0 && requirement.AtLeast is null && requirement.AtMost is null)
            {
                throw new ArgumentException(
                    $"The requirement on '{requirement.Axis}' names neither a value nor a bound, so there "
                    + "is nothing for it to admit or refuse.",
                    nameof(builder));
            }
        }

        return [.. builder.Requirements];
    }

    private static IReadOnlyList<AxisFloor> ValidateFloors(
        PolicyBuilder builder,
        Dictionary<QualityAxisId, QualityAxis> declared)
    {
        foreach (var floor in builder.Floors)
        {
            Declared(declared, floor.Axis);
        }

        return [.. builder.Floors];
    }

    private static QualityAxis Declared(Dictionary<QualityAxisId, QualityAxis> declared, QualityAxisId id)
    {
        if (declared.TryGetValue(id, out var axis))
        {
            return axis;
        }

        throw new ArgumentException($"The family declares no axis called '{id}'.", nameof(id));
    }

    private static double KeyOf(QualityPoint point, AxisPreference preference, QualityAxis axis)
    {
        var reading = point[preference.Axis];
        var value = Richest(axis, reading);

        if (!value.IsKnown)
        {
            if (preference.WhenUnknown.Mode == PreferenceUnknownMode.Lowest)
            {
                return double.NegativeInfinity;
            }

            value = preference.WhenUnknown.Assumption;
        }

        var richness = Richness(preference, axis, value);

        if (preference.Ceiling is { } ceiling)
        {
            richness = Math.Min(richness, Richness(preference, axis, ceiling));
        }

        if (preference.Floor is { } floor)
        {
            richness = Math.Max(richness, Richness(preference, axis, floor));
        }

        return double.IsNegativeInfinity(richness) || preference.PreferRicher ? richness : -richness;
    }

    private static double Richness(AxisPreference preference, QualityAxis axis, AxisValue value)
    {
        if (preference.Ranking.Count == 0)
        {
            return axis.RichnessOf(value);
        }

        for (var group = 0; group < preference.Ranking.Count; group++)
        {
            foreach (var member in preference.Ranking[group])
            {
                if (member.Names(value))
                {
                    return group;
                }
            }
        }

        return double.NegativeInfinity;
    }

    private static AxisValue Richest(QualityAxis axis, AxisReading reading)
    {
        if (!reading.IsKnown || reading.Values.Count == 0)
        {
            return AxisValue.None;
        }

        var best = AxisValue.None;
        var bestRichness = double.NegativeInfinity;

        foreach (var value in reading.Values)
        {
            var richness = axis.RichnessOf(value);

            if (!best.IsKnown || richness > bestRichness)
            {
                best = value;
                bestRichness = richness;
            }
        }

        return best;
    }

    private static bool ClaimOutrunsMeasurement(QualityPoint held, QualityPoint candidate, QualityAxisId axis)
    {
        var heldReading = held[axis];

        if (!heldReading.IsKnown || heldReading.Values.Count == 0)
        {
            return false;
        }

        var candidateReading = candidate[axis];

        var candidateSource = candidateReading.IsKnown && candidateReading.Values.Count > 0
            ? candidateReading.Source
            : EvidenceSource.Assumed;

        return candidateSource < heldReading.Source;
    }

    private static string Spell(AxisReading reading)
    {
        if (!reading.IsKnown || reading.Values.Count == 0)
        {
            return "unstated";
        }

        var spelling = new StringBuilder();

        foreach (var value in reading.Values)
        {
            if (spelling.Length > 0)
            {
                spelling.Append(" and ");
            }

            spelling.Append(value.Token);
        }

        return spelling.ToString();
    }

    private bool Satisfies(AxisRequirement requirement, QualityPoint candidate, out bool bySilence)
    {
        bySilence = false;

        var axis = axes[requirement.Axis];
        var reading = candidate[requirement.Axis];
        var stated = reading.IsKnown
            && reading.Values.Count > 0
            && reading.Source >= requirement.MinimumSource;

        IReadOnlyList<AxisValue> values;
        double richness;

        if (stated)
        {
            values = reading.Values;
            richness = axis.RichnessOf(Richest(axis, reading));
        }
        else
        {
            switch (requirement.WhenUnknown.Mode)
            {
                case UnknownEvidenceMode.Ignore:
                    return true;

                case UnknownEvidenceMode.Refuse:
                    bySilence = true;

                    return false;

                case UnknownEvidenceMode.Assume:
                    values = [requirement.WhenUnknown.Assumption];
                    richness = axis.RichnessOf(requirement.WhenUnknown.Assumption);

                    break;

                default:
                    values = [];
                    richness = double.NegativeInfinity;

                    break;
            }
        }

        var matches = requirement.Values.Count > 0
            ? Intersects(values, requirement.Values)
            : (requirement.AtLeast is not { } least || richness >= axis.RichnessOf(least))
                && (requirement.AtMost is not { } most || richness <= axis.RichnessOf(most));

        return requirement.Mode == RequirementMode.Require ? matches : !matches;
    }

    private static bool Intersects(IReadOnlyList<AxisValue> held, IReadOnlyList<AxisValue> named)
    {
        foreach (var value in held)
        {
            foreach (var wanted in named)
            {
                if (value.Names(wanted))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void RequireFamily(QualityPoint point, string parameterName)
    {
        if (point.Family != Type.Family)
        {
            throw new ArgumentException(
                $"This policy is over '{Type.Family}' and the point is of '{point.Family}'. Two format "
                + "families are never ranked against one another.",
                parameterName);
        }
    }

    private string NameOf(QualityAxisId id) => axes.TryGetValue(id, out var axis) ? axis.Name : id.ToString();

    private string NotAnUpgrade(QualityPoint held, QualityPoint candidate, QualityAxisId decidedBy)
    {
        if (!decidedBy.IsNamed)
        {
            return "Not an upgrade: nothing you order on separates them.";
        }

        return $"Not an upgrade: {NameOf(decidedBy)} is {Spell(candidate[decidedBy])} against "
            + $"{Spell(held[decidedBy])}.";
    }

    private string Upgrade(QualityPoint held, QualityPoint candidate, QualityAxisId decidedBy)
    {
        if (!decidedBy.IsNamed)
        {
            var heldScore = Facets.Of(held).ToString(CultureInfo.InvariantCulture);
            var candidateScore = Facets.Of(candidate).ToString(CultureInfo.InvariantCulture);

            return $"Upgrade: nothing you order on separates them, but the bonus score is {candidateScore} "
                + $"against {heldScore}.";
        }

        return $"Upgrade: {NameOf(decidedBy)} {Spell(held[decidedBy])} to {Spell(candidate[decidedBy])}.";
    }

    private string DescribePreference(AxisPreference preference)
    {
        var clause = new StringBuilder();
        var axis = axes[preference.Axis];

        clause.Append(preference.PreferRicher ? "the richest " : "the least ");
        clause.Append(axis.Name.ToLowerInvariant());

        if (preference.Ranking.Count > 0)
        {
            clause.Append(", in your own order");
        }

        if (preference.Ceiling is { } ceiling)
        {
            clause.Append(", and beyond ");
            clause.Append(Unitized(axis, ceiling));
            clause.Append(" it stops mattering");
        }

        if (preference.Floor is { } floor)
        {
            clause.Append(", and below ");
            clause.Append(Unitized(axis, floor));
            clause.Append(" it stops mattering");
        }

        if (preference.WhenUnknown.Mode == PreferenceUnknownMode.Assume)
        {
            clause.Append(", reading silence as ");
            clause.Append(Unitized(axis, preference.WhenUnknown.Assumption));
        }

        return clause.ToString();
    }

    private string DescribeFacets()
    {
        var clause = new StringBuilder();

        foreach (var score in Facets.Scores)
        {
            if (clause.Length > 0)
            {
                clause.Append(", and ");
            }

            clause.Append(axes[score.Axis].Name.ToLowerInvariant());
        }

        return clause.ToString();
    }

    private string DescribeFloors()
    {
        var clause = new StringBuilder();

        foreach (var floor in Cutoff.Floors)
        {
            if (clause.Length > 0)
            {
                clause.Append(" and ");
            }

            var axis = axes[floor.Axis];

            clause.Append(axis.GreaterIsRicher ? "at least " : "at most ");
            clause.Append(Unitized(axis, floor.AtLeast));
        }

        return clause.ToString();
    }

    private string DescribeRefusals()
    {
        var clause = new StringBuilder();

        foreach (var requirement in Requirements)
        {
            if (requirement.Mode != RequirementMode.Refuse || requirement.Values.Count == 0)
            {
                continue;
            }

            foreach (var value in requirement.Values)
            {
                if (clause.Length > 0)
                {
                    clause.Append(", or ");
                }

                clause.Append(value.Token);
            }
        }

        return clause.ToString();
    }

    private static string Unitized(QualityAxis axis, AxisValue value) =>
        string.IsNullOrEmpty(axis.Unit) ? value.Token : $"{value.Token} {axis.Unit}";
}
