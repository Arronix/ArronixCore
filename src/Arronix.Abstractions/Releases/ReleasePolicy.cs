using System.Linq.Expressions;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Media;

namespace Arronix.Abstractions.Releases;

/// <summary>Where an unknown value sorts on one preference.</summary>
public enum UnknownPreference
{
    /// <summary>Unknown sorts below every known value.</summary>
    Last = 0,

    /// <summary>Unknown sorts above every known value.</summary>
    First = 1
}

/// <summary>Why a typed release was or was not admitted by policy.</summary>
/// <param name="IsAdmitted">Whether all requirements passed.</param>
/// <param name="Reason">The first failed requirement, when refused.</param>
public sealed record ReleaseEligibility(bool IsAdmitted, string? Reason);

/// <summary>Builds a deterministic policy over typed release properties.</summary>
/// <typeparam name="TRelease">The media kind's interpreted release type.</typeparam>
public sealed class ReleasePolicyBuilder<TRelease>
    where TRelease : class, IRelease
{
    internal const int MaximumFacets = 5;
    internal const int MaximumFacetPoints = 10;

    internal List<(Func<TRelease, bool> Test, string Reason)> Requirements { get; } = [];
    internal List<IPreference> Preferences { get; } = [];
    internal List<(string Name, Func<TRelease, int> Score)> Facets { get; } = [];

    /// <summary>Adds an eligibility requirement.</summary>
    public ReleasePolicyBuilder<TRelease> Require(Func<TRelease, bool> requirement, string reason)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Requirements.Add((requirement, reason));
        return this;
    }

    /// <summary>Adds one lexicographic preference key.</summary>
    /// <typeparam name="TValue">The typed property value.</typeparam>
    /// <param name="selector">Reads the value.</param>
    /// <param name="comparer">Its domain order; the default comparer is used when absent.</param>
    /// <param name="preferGreater">Whether greater values are preferred.</param>
    /// <param name="isKnown">Whether the selected value is known.</param>
    /// <param name="unknown">Where unknown sorts relative to known.</param>
    public ReleasePolicyBuilder<TRelease> Prefer<TValue>(
        Expression<Func<TRelease, TValue>> selector,
        IComparer<TValue>? comparer = null,
        bool preferGreater = true,
        Func<TValue, bool>? isKnown = null,
        UnknownPreference unknown = UnknownPreference.Last)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var name = NameOf(selector.Body);
        Preferences.Add(new Preference<TValue>(
            name,
            selector.Compile(),
            comparer ?? Comparer<TValue>.Default,
            preferGreater,
            isKnown ?? (static _ => true),
            unknown));
        return this;
    }

    /// <summary>Adds one bounded score consulted only after every core preference ties.</summary>
    public ReleasePolicyBuilder<TRelease> Facet(Expression<Func<TRelease, int>> score)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (Facets.Count == MaximumFacets)
        {
            throw new ArgumentException($"A release policy declares at most {MaximumFacets} facets.", nameof(score));
        }

        Facets.Add((NameOf(score.Body), score.Compile()));
        return this;
    }

    private static string NameOf(Expression expression)
    {
        while (expression is UnaryExpression unary
               && unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            expression = unary.Operand;
        }

        return expression is MemberExpression member
            ? member.Member.Name
            : expression.ToString();
    }

    internal interface IPreference
    {
        int Compare(TRelease left, TRelease right);
    }

    private sealed class Preference<TValue>(
        string name,
        Func<TRelease, TValue> selector,
        IComparer<TValue> comparer,
        bool preferGreater,
        Func<TValue, bool> isKnown,
        UnknownPreference unknown) : IPreference
    {
        public int Compare(TRelease left, TRelease right)
        {
            var leftValue = selector(left);
            var rightValue = selector(right);
            var leftKnown = isKnown(leftValue);
            var rightKnown = isKnown(rightValue);

            if (leftKnown != rightKnown)
            {
                return leftKnown == (unknown == UnknownPreference.Last) ? 1 : -1;
            }

            if (!leftKnown)
            {
                return 0;
            }

            try
            {
                var compared = comparer.Compare(leftValue, rightValue);
                return preferGreater ? compared : -compared;
            }
            catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
            {
                throw new InvalidOperationException($"Release preference '{name}' could not compare its values.", failure);
            }
        }
    }
}

/// <summary>A compiled deterministic policy over one media-owned release type.</summary>
/// <typeparam name="TRelease">The interpreted release type.</typeparam>
public sealed class ReleasePolicy<TRelease>
    where TRelease : class, IRelease
{
    private readonly IReadOnlyList<(Func<TRelease, bool> Test, string Reason)> requirements;
    private readonly IReadOnlyList<ReleasePolicyBuilder<TRelease>.IPreference> preferences;
    private readonly IReadOnlyList<(string Name, Func<TRelease, int> Score)> facets;

    private ReleasePolicy(ReleasePolicyBuilder<TRelease> builder)
    {
        requirements = [.. builder.Requirements];
        preferences = [.. builder.Preferences];
        facets = [.. builder.Facets];
    }

    /// <summary>Compiles one policy declaration.</summary>
    public static ReleasePolicy<TRelease> Compile(Action<ReleasePolicyBuilder<TRelease>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ReleasePolicyBuilder<TRelease>();
        configure(builder);
        return new ReleasePolicy<TRelease>(builder);
    }

    /// <summary>Evaluates every hard requirement in declaration order.</summary>
    public ReleaseEligibility Admit(TRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);

        foreach (var (test, reason) in requirements)
        {
            if (!test(release))
            {
                return new ReleaseEligibility(false, reason);
            }
        }

        return new ReleaseEligibility(true, null);
    }

    /// <summary>Compares two admitted releases by lexicographic core and then bounded facets.</summary>
    /// <returns>A positive value when <paramref name="left"/> is preferred.</returns>
    public int Compare(TRelease left, TRelease right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        foreach (var preference in preferences)
        {
            var compared = preference.Compare(left, right);
            if (compared != 0)
            {
                return compared;
            }
        }

        return FacetScore(left).CompareTo(FacetScore(right));
    }

    private int FacetScore(TRelease release)
    {
        var total = 0;
        foreach (var (name, score) in facets)
        {
            var points = score(release);
            if (Math.Abs(points) > ReleasePolicyBuilder<TRelease>.MaximumFacetPoints)
            {
                throw new InvalidOperationException(
                    $"Release facet '{name}' returned {points}; each facet is bounded to plus or minus "
                    + $"{ReleasePolicyBuilder<TRelease>.MaximumFacetPoints} points.");
            }

            total += points;
        }

        return total;
    }
}

/// <summary>One typed release paired with the listing and target match that produced it.</summary>
public sealed record ReleaseOption<TTarget, TRelease>(
    ReleaseListing Listing,
    TRelease Release,
    TargetMatch<TTarget> Match)
    where TTarget : class, IReleaseTarget
    where TRelease : class, IRelease;
