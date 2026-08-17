using Arronix.Abstractions.Quality;

// Reads and produces the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// The host's quality engine: it reads evidence onto a family's axes, holds the policy in force over each
/// family, and answers the four questions anybody actually asks — is this eligible, is it better, is what I
/// hold good enough, and is it the size it claims to be.
/// </summary>
/// <remarks>
/// <para>
/// The split this type keeps is the design. A family's quality model says what a file <i>is</i> and cannot
/// rank anything; a policy says what a user <i>wants</i> and cannot read a release. Nothing in the middle
/// merges them into a number, so a decision can always name the axis that produced it, and a user who
/// disagrees with a decision has something to change.
/// </para>
/// <para>
/// A policy is per family and replaceable. Until a user states one, the family's own shipped opinion stands
/// — a default that a replacing profile owes nothing to.
/// </para>
/// </remarks>
internal sealed class AxisQualityEvaluator
{
    private readonly QualityFamilyRegistry registry;
    private readonly Dictionary<FormatFamilyId, QualityPolicy> stated = [];

    /// <summary>Initializes a new instance of the <see cref="AxisQualityEvaluator"/> class.</summary>
    /// <param name="registry">The registered families.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
    internal AxisQualityEvaluator(QualityFamilyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        this.registry = registry;
    }

    /// <summary>Reads a family's quality model.</summary>
    /// <param name="family">The family.</param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentException">The family is not registered.</exception>
    internal IQualityType TypeOf(FormatFamilyId family) => registry.Get(family);

    /// <summary>Reads the policy in force over a family.</summary>
    /// <param name="family">The family.</param>
    /// <returns>The user's policy when they stated one, otherwise the family's own.</returns>
    /// <exception cref="ArgumentException">The family is not registered.</exception>
    internal QualityPolicy PolicyFor(FormatFamilyId family) =>
        stated.TryGetValue(family, out var policy) ? policy : registry.Get(family).DefaultPolicy;

    /// <summary>Puts a user's own policy in force over its family.</summary>
    /// <param name="policy">The policy.</param>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The policy is over a family that is not registered.</exception>
    internal void Use(QualityPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var family = policy.Type.Family;

        if (!registry.TryGet(family, out var registered) || !ReferenceEquals(registered, policy.Type))
        {
            throw new ArgumentException(
                $"The policy is over '{family}', which is not the model this host registered for that "
                + "family. A policy is compiled against exactly one quality type and cannot be moved to "
                + "another.",
                nameof(policy));
        }

        stated[family] = policy;
    }

    /// <summary>Reads release and file evidence onto a family's axes.</summary>
    /// <param name="family">The family.</param>
    /// <param name="evidence">What the scanners and any probe produced.</param>
    /// <returns>The point.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="evidence"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The family is not registered.</exception>
    internal QualityPoint Read(FormatFamilyId family, ReleaseEvidence evidence) =>
        registry.Get(family).Read(evidence);

    /// <summary>Ranks two points under the policy in force over their family.</summary>
    /// <param name="held">The point already held.</param>
    /// <param name="candidate">The candidate's point.</param>
    /// <returns>The judgment.</returns>
    /// <exception cref="ArgumentNullException">Either point is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The points belong to different families, or to none registered.</exception>
    internal QualityJudgment Compare(QualityPoint held, QualityPoint candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return PolicyFor(candidate.Family).Compare(held, candidate);
    }

    /// <summary>Decides whether a candidate is eligible at all.</summary>
    /// <param name="candidate">The candidate's point.</param>
    /// <returns>The verdict, carrying the requirement that refused it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidate"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The candidate's family is not registered.</exception>
    internal Eligibility Admits(QualityPoint candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return PolicyFor(candidate.Family).Admits(candidate);
    }

    /// <summary>Decides whether a held file is good enough to stop looking.</summary>
    /// <param name="held">The point already held.</param>
    /// <returns><see langword="true"/> when every cutoff floor is met.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="held"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The held point's family is not registered.</exception>
    internal bool IsGoodEnough(QualityPoint held)
    {
        ArgumentNullException.ThrowIfNull(held);

        return PolicyFor(held.Family).IsGoodEnough(held);
    }

    /// <summary>Decides whether to take a candidate, and says why in one sentence.</summary>
    /// <param name="held">The point already held, or null when nothing is.</param>
    /// <param name="candidate">The candidate's point.</param>
    /// <returns>The decision and its reason.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidate"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The points belong to different families, or to none registered.</exception>
    internal GrabDecision Decide(QualityPoint? held, QualityPoint candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return PolicyFor(candidate.Family).Decide(held, candidate);
    }

    /// <summary>Renders a point in the community's vocabulary.</summary>
    /// <param name="point">The point.</param>
    /// <param name="detail">How much of the point to spell.</param>
    /// <returns>The label.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The point's family is not registered.</exception>
    internal string Label(QualityPoint point, QualityLabelDetail detail)
    {
        ArgumentNullException.ThrowIfNull(point);

        return registry.Get(point.Family).Label(point, detail);
    }

    /// <summary>Reads a community label back into a point.</summary>
    /// <param name="family">The family the label is written in.</param>
    /// <param name="label">The label.</param>
    /// <param name="point">Receives the point.</param>
    /// <returns><see langword="true"/> when the label was understood.</returns>
    /// <exception cref="ArgumentException">The family is not registered.</exception>
    internal bool TryParseLabel(FormatFamilyId family, string label, out QualityPoint point) =>
        registry.Get(family).TryParseLabel(label, out point);

    /// <summary>Computes the size a file at a point is expected to be.</summary>
    /// <param name="point">The point.</param>
    /// <param name="duration">The item's duration. Zero when unknown.</param>
    /// <returns>The expectation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The point's family is not registered.</exception>
    internal SizeExpectation ExpectedSize(QualityPoint point, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(point);

        return registry.Get(point.Family).ExpectedSize(point, duration);
    }

    /// <summary>Judges whether a file is a plausible size for what it claims to be.</summary>
    /// <param name="point">The point.</param>
    /// <param name="sizeInBytes">The file's size.</param>
    /// <param name="duration">The item's duration. Zero when unknown.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="point"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The point's family is not registered.</exception>
    /// <remarks>
    /// An unassessable expectation is a <i>pass</i>, never a rejection. A release nobody can measure is not
    /// thereby implausible, and a size gate that refused what it could not measure would be a requirement
    /// wearing a measurement's clothes.
    /// </remarks>
    internal SizeVerdict AssessSize(QualityPoint point, long sizeInBytes, TimeSpan duration) =>
        ExpectedSize(point, duration).Assess(sizeInBytes);
}
