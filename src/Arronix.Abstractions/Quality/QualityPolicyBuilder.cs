// The quality-axes vocabulary these builders collect is an experimental contract area until 1.0.
#pragma warning disable ARX0021

namespace Arronix.Abstractions.Quality;

/// <summary>Collects the four sections of a policy while a declaration runs.</summary>
/// <remarks>
/// Nothing here validates. Validation happens once, in <see cref="QualityPolicy.For"/>, against the
/// family's declared axes — a builder that refused as it went would refuse on the order the clauses were
/// written in rather than on what they add up to.
/// </remarks>
internal sealed class PolicyBuilder : IQualityPolicyBuilder
{
    internal List<AxisPreference> Preferences { get; } = [];

    internal List<FacetDraft> Facets { get; } = [];

    internal List<AxisRequirement> Requirements { get; } = [];

    internal List<AxisFloor> Floors { get; } = [];

    internal bool UpgradesEnabled { get; private set; } = true;

    public IAxisPreferenceBuilder Prefer(QualityAxisId axis)
    {
        Preferences.Add(new AxisPreference { Axis = axis });

        return new PreferenceScope(this, Preferences.Count - 1);
    }

    public IFacetScoreBuilder Facet(QualityAxisId axis)
    {
        var draft = new FacetDraft(axis);
        Facets.Add(draft);

        return new FacetScope(this, draft);
    }

    public IAxisRequirementBuilder Require(QualityAxisId axis, params AxisValue[] values) =>
        AddRequirement(new AxisRequirement
        {
            Axis = axis,
            Mode = RequirementMode.Require,
            Values = values is null ? [] : [.. values],
        });

    public IAxisRequirementBuilder Refuse(QualityAxisId axis, params AxisValue[] values) =>
        AddRequirement(new AxisRequirement
        {
            Axis = axis,
            Mode = RequirementMode.Refuse,
            Values = values is null ? [] : [.. values],
        });

    public IAxisRequirementBuilder RequireAtLeast(QualityAxisId axis, AxisValue atLeast) =>
        AddRequirement(new AxisRequirement
        {
            Axis = axis,
            Mode = RequirementMode.Require,
            AtLeast = atLeast,
        });

    public IAxisRequirementBuilder RequireAtMost(QualityAxisId axis, AxisValue atMost) =>
        AddRequirement(new AxisRequirement
        {
            Axis = axis,
            Mode = RequirementMode.Require,
            AtMost = atMost,
        });

    public IQualityPolicyBuilder Requirement(AxisRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        Requirements.Add(requirement);

        return this;
    }

    public IQualityPolicyBuilder GoodEnoughAt(QualityAxisId axis, AxisValue atLeast) =>
        GoodEnoughAt(axis, atLeast, UnknownEvidence.Lowest);

    public IQualityPolicyBuilder GoodEnoughAt(QualityAxisId axis, AxisValue atLeast, UnknownEvidence whenUnknown)
    {
        Floors.Add(new AxisFloor(axis, atLeast, whenUnknown));

        return this;
    }

    public IQualityPolicyBuilder WithoutUpgrades()
    {
        UpgradesEnabled = false;

        return this;
    }

    private IAxisRequirementBuilder AddRequirement(AxisRequirement requirement)
    {
        Requirements.Add(requirement);

        return new RequirementScope(this, Requirements.Count - 1);
    }
}

/// <summary>One facet while its members are being priced.</summary>
/// <param name="axis">The axis.</param>
internal sealed class FacetDraft(QualityAxisId axis)
{
    internal QualityAxisId Axis { get; } = axis;

    internal Dictionary<AxisValue, int> Points { get; } = [];

    internal FacetScore ToScore() => new() { Axis = Axis, Points = Points };
}

/// <summary>A builder scoped to one clause, which may still start the next one.</summary>
/// <param name="root">The policy under construction.</param>
internal abstract class PolicyScope(PolicyBuilder root) : IQualityPolicyBuilder
{
    protected PolicyBuilder Root { get; } = root;

    public IAxisPreferenceBuilder Prefer(QualityAxisId axis) => Root.Prefer(axis);

    public IFacetScoreBuilder Facet(QualityAxisId axis) => Root.Facet(axis);

    public IAxisRequirementBuilder Require(QualityAxisId axis, params AxisValue[] values) =>
        Root.Require(axis, values);

    public IAxisRequirementBuilder Refuse(QualityAxisId axis, params AxisValue[] values) =>
        Root.Refuse(axis, values);

    public IAxisRequirementBuilder RequireAtLeast(QualityAxisId axis, AxisValue atLeast) =>
        Root.RequireAtLeast(axis, atLeast);

    public IAxisRequirementBuilder RequireAtMost(QualityAxisId axis, AxisValue atMost) =>
        Root.RequireAtMost(axis, atMost);

    public IQualityPolicyBuilder Requirement(AxisRequirement requirement) => Root.Requirement(requirement);

    public IQualityPolicyBuilder GoodEnoughAt(QualityAxisId axis, AxisValue atLeast) =>
        Root.GoodEnoughAt(axis, atLeast);

    public IQualityPolicyBuilder GoodEnoughAt(QualityAxisId axis, AxisValue atLeast, UnknownEvidence whenUnknown) =>
        Root.GoodEnoughAt(axis, atLeast, whenUnknown);

    public IQualityPolicyBuilder WithoutUpgrades() => Root.WithoutUpgrades();
}

/// <summary>The clause that places one axis in the ordering.</summary>
/// <param name="root">The policy under construction.</param>
/// <param name="index">Which precedence entry this clause is amending.</param>
internal sealed class PreferenceScope(PolicyBuilder root, int index) : PolicyScope(root), IAxisPreferenceBuilder
{
    public IAxisPreferenceBuilder UpTo(AxisValue ceiling) => Amend(entry => entry with { Ceiling = ceiling });

    public IAxisPreferenceBuilder DownTo(AxisValue floor) => Amend(entry => entry with { Floor = floor });

    public IAxisPreferenceBuilder PreferringLess() => Amend(entry => entry with { PreferRicher = false });

    public IAxisPreferenceBuilder RankedAs(params IReadOnlyList<AxisValue>[] groups) =>
        Amend(entry => entry with { Ranking = groups is null ? [] : [.. groups] });

    public IAxisPreferenceBuilder WhenUnknownRanksLowest() =>
        Amend(entry => entry with { WhenUnknown = PreferenceUnknown.Lowest });

    public IAxisPreferenceBuilder WhenUnknownAssume(AxisValue value) =>
        Amend(entry => entry with { WhenUnknown = PreferenceUnknown.Assume(value) });

    private PreferenceScope Amend(Func<AxisPreference, AxisPreference> amendment)
    {
        Root.Preferences[index] = amendment(Root.Preferences[index]);

        return this;
    }
}

/// <summary>The clause that prices one facet's members.</summary>
/// <param name="root">The policy under construction.</param>
/// <param name="draft">The facet this clause is amending.</param>
internal sealed class FacetScope(PolicyBuilder root, FacetDraft draft) : PolicyScope(root), IFacetScoreBuilder
{
    public IFacetScoreBuilder Worth(AxisValue member, int points)
    {
        draft.Points[member] = points;

        return this;
    }
}

/// <summary>The clause that states one requirement's terms.</summary>
/// <param name="root">The policy under construction.</param>
/// <param name="index">Which requirement this clause is amending.</param>
internal sealed class RequirementScope(PolicyBuilder root, int index) : PolicyScope(root), IAxisRequirementBuilder
{
    public IAxisRequirementBuilder NotBelow(EvidenceSource source) =>
        Amend(requirement => requirement with { MinimumSource = source });

    public IAxisRequirementBuilder WhenUnknown(UnknownEvidence unknown) =>
        Amend(requirement => requirement with { WhenUnknown = unknown });

    public IAxisRequirementBuilder Because(string reason) =>
        Amend(requirement => requirement with { Reason = reason });

    private RequirementScope Amend(Func<AxisRequirement, AxisRequirement> amendment)
    {
        Root.Requirements[index] = amendment(Root.Requirements[index]);

        return this;
    }
}
