using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>Declares one user's preference over one format family's axes.</summary>
/// <remarks>
/// <para>
/// Axes are named by <see cref="QualityAxisId"/> rather than by a typed property reference, because the
/// policies that matter are the ones a <b>user</b> composes in an editor from the axes a family declares
/// — the family's own shipped default is not where a bad policy comes from. An identity is derived from a
/// property name by <see cref="QualityAxisId.FromProperty"/>, so a family writing its default in source
/// still names its axes with <c>nameof</c> and never with a hand-typed string.
/// </para>
/// <para>
/// The four sections are four different questions and are deliberately not merged into one score. What to
/// <i>prefer</i> orders candidates that are all acceptable; what to <i>refuse</i> decides acceptability;
/// what is a <i>bonus</i> separates candidates the order left tied; and what is <i>good enough</i> decides
/// when to stop.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityPolicyBuilder
{
    /// <summary>Adds an axis to the ordering, after every axis already added.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>A builder scoped to this entry.</returns>
    IAxisPreferenceBuilder Prefer(QualityAxisId axis);

    /// <summary>Declares an axis as a bonus, consulted only when the ordering leaves two candidates tied.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>A builder scoped to this facet.</returns>
    IFacetScoreBuilder Facet(QualityAxisId axis);

    /// <summary>Refuses any candidate that does not hold one of the listed values.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="values">The acceptable values.</param>
    /// <returns>A builder scoped to this requirement.</returns>
    IAxisRequirementBuilder Require(QualityAxisId axis, params AxisValue[] values);

    /// <summary>Refuses any candidate that holds one of the listed values.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="values">The refused values.</param>
    /// <returns>A builder scoped to this requirement.</returns>
    IAxisRequirementBuilder Refuse(QualityAxisId axis, params AxisValue[] values);

    /// <summary>Refuses any candidate below a stated richness.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="atLeast">The least acceptable richness.</param>
    /// <returns>A builder scoped to this requirement.</returns>
    IAxisRequirementBuilder RequireAtLeast(QualityAxisId axis, AxisValue atLeast);

    /// <summary>Refuses any candidate above a stated richness.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="atMost">The greatest acceptable richness.</param>
    /// <returns>A builder scoped to this requirement.</returns>
    IAxisRequirementBuilder RequireAtMost(QualityAxisId axis, AxisValue atMost);

    /// <summary>Adds a requirement declared in full.</summary>
    /// <param name="requirement">The requirement.</param>
    /// <returns>This builder, for chaining.</returns>
    IQualityPolicyBuilder Requirement(AxisRequirement requirement);

    /// <summary>Adds one floor to the cutoff.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="atLeast">The least acceptable richness.</param>
    /// <returns>This builder, for chaining.</returns>
    IQualityPolicyBuilder GoodEnoughAt(QualityAxisId axis, AxisValue atLeast);

    /// <summary>Adds one floor to the cutoff, saying what an absent reading means.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="atLeast">The least acceptable richness.</param>
    /// <param name="whenUnknown">What an absent reading means.</param>
    /// <returns>This builder, for chaining.</returns>
    IQualityPolicyBuilder GoodEnoughAt(QualityAxisId axis, AxisValue atLeast, UnknownEvidence whenUnknown);

    /// <summary>Declares that upgrades are not searched for at all.</summary>
    /// <returns>This builder, for chaining.</returns>
    IQualityPolicyBuilder WithoutUpgrades();
}

/// <summary>Declares one axis's place in the ordering.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IAxisPreferenceBuilder : IQualityPolicyBuilder
{
    /// <summary>Caps the axis: anything richer than this compares equal to it.</summary>
    /// <param name="ceiling">The value beyond which the axis stops mattering.</param>
    /// <returns>This builder, for chaining.</returns>
    IAxisPreferenceBuilder UpTo(AxisValue ceiling);

    /// <summary>Floors the axis: anything poorer than this compares equal to it.</summary>
    /// <param name="floor">The value below which the axis stops mattering.</param>
    /// <returns>This builder, for chaining.</returns>
    IAxisPreferenceBuilder DownTo(AxisValue floor);

    /// <summary>Inverts the axis, for the profile that prefers small files.</summary>
    /// <returns>This builder, for chaining.</returns>
    IAxisPreferenceBuilder PreferringLess();

    /// <summary>Replaces the family's declared order with the user's own, worst group first.</summary>
    /// <param name="groups">The groups, worst first; members inside one group are tied.</param>
    /// <returns>This builder, for chaining.</returns>
    IAxisPreferenceBuilder RankedAs(params IReadOnlyList<AxisValue>[] groups);

    /// <summary>States that an absent reading sorts below every present one.</summary>
    /// <returns>This builder, for chaining.</returns>
    IAxisPreferenceBuilder WhenUnknownRanksLowest();

    /// <summary>States what an absent reading is read as.</summary>
    /// <param name="value">The assumption.</param>
    /// <returns>This builder, for chaining.</returns>
    IAxisPreferenceBuilder WhenUnknownAssume(AxisValue value);
}

/// <summary>Declares what one facet's members are worth.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IFacetScoreBuilder : IQualityPolicyBuilder
{
    /// <summary>States what one member is worth.</summary>
    /// <param name="member">The member.</param>
    /// <param name="points">The points, in [-100, 100]. Negative points are a real preference.</param>
    /// <returns>This builder, for chaining.</returns>
    IFacetScoreBuilder Worth(AxisValue member, int points);
}

/// <summary>Declares the terms of one requirement.</summary>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IAxisRequirementBuilder : IQualityPolicyBuilder
{
    /// <summary>States the weakest reading this requirement will act on.</summary>
    /// <param name="source">The weakest acceptable source.</param>
    /// <returns>This builder, for chaining.</returns>
    IAxisRequirementBuilder NotBelow(EvidenceSource source);

    /// <summary>States what an absent reading means.</summary>
    /// <param name="unknown">The mode.</param>
    /// <returns>This builder, for chaining.</returns>
    IAxisRequirementBuilder WhenUnknown(UnknownEvidence unknown);

    /// <summary>States the user's own words for why.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>This builder, for chaining.</returns>
    IAxisRequirementBuilder Because(string reason);
}
