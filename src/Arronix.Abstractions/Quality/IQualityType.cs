using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>One format family's runtime quality model, held by the host and served to the client.</summary>
/// <remarks>
/// <para>
/// Built by the host from an <see cref="IQualityType{TFacts}"/>; never implemented by an extension. Every
/// member is derived from the facts type or carried verbatim from the builder, so there is no second
/// source of truth and nothing here can disagree with the facts type.
/// </para>
/// <para>
/// <b>Not on it, deliberately: any comparison.</b> This type cannot tell you which of two points is
/// better, because that question has no answer without a policy. The type that <i>knows about</i> quality
/// is structurally incapable of <i>ranking</i> it, which is what separating what the kind detects from
/// what the user prefers looks like once it is fixed.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IQualityType
{
    /// <summary>Gets the format family.</summary>
    FormatFamilyId Family { get; }

    /// <summary>Gets the family's display name.</summary>
    string Name { get; }

    /// <summary>Gets the facts type, so serialization and storage have a target.</summary>
    Type FactsType { get; }

    /// <summary>Gets the declared axes, in declaration order. Declaration order is not preference order.</summary>
    IReadOnlyList<QualityAxis> Axes { get; }

    /// <summary>Gets the policy shipped as this family's stated opinion. A user may replace it entirely.</summary>
    QualityPolicy DefaultPolicy { get; }

    /// <summary>Reads evidence onto a kind-blind point.</summary>
    /// <param name="evidence">What the parser and any probe produced.</param>
    /// <returns>The point.</returns>
    QualityPoint Read(ReleaseEvidence evidence);

    /// <summary>Projects typed facts onto a kind-blind point.</summary>
    /// <param name="facts">An instance of <see cref="FactsType"/>.</param>
    /// <returns>The point.</returns>
    /// <exception cref="ArgumentException"><paramref name="facts"/> is not of <see cref="FactsType"/>.</exception>
    QualityPoint Project(object facts);

    /// <summary>Renders a point in the community's vocabulary.</summary>
    /// <param name="point">The point.</param>
    /// <param name="detail">How much of the point to spell.</param>
    /// <returns>The label.</returns>
    string Label(QualityPoint point, QualityLabelDetail detail);

    /// <summary>Reads a community label back into a point, for a pasted profile or a stored string.</summary>
    /// <param name="label">The label.</param>
    /// <param name="point">Receives the point.</param>
    /// <returns><see langword="true"/> when the label was understood.</returns>
    /// <remarks>
    /// For exactly two callers — reading a stored string, and accepting a label a user typed — and both
    /// convert to a point immediately. Nothing that ranks, admits or assesses ever reads a rendered
    /// string.
    /// </remarks>
    bool TryParseLabel(string label, out QualityPoint point);

    /// <summary>Computes the size a file at this point is expected to be.</summary>
    /// <param name="point">The point.</param>
    /// <param name="duration">The item's duration. <see cref="TimeSpan.Zero"/> when unknown.</param>
    /// <returns>The expectation, or an unassessable one when the family has no size model.</returns>
    SizeExpectation ExpectedSize(QualityPoint point, TimeSpan duration);
}
