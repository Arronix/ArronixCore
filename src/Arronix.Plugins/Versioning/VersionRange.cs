using System.Linq;

namespace Arronix.Plugins.Versioning;

/// <summary>
/// How a comparator relates a candidate version to its operand.
/// </summary>
public enum ComparatorOperator
{
    /// <summary>The candidate must equal the operand exactly.</summary>
    Equal = 0,

    /// <summary>The candidate must follow the operand.</summary>
    GreaterThan = 1,

    /// <summary>The candidate must not precede the operand.</summary>
    GreaterThanOrEqual = 2,

    /// <summary>The candidate must precede the operand.</summary>
    LessThan = 3,

    /// <summary>The candidate must not follow the operand.</summary>
    LessThanOrEqual = 4
}

/// <summary>
/// One comparison against one version.
/// </summary>
/// <param name="Operator">How the candidate is compared.</param>
/// <param name="Operand">The version compared against.</param>
public readonly record struct VersionComparator(ComparatorOperator Operator, SemanticVersion Operand)
{
    /// <summary>
    /// Determines whether a version satisfies this comparison.
    /// </summary>
    /// <param name="version">The candidate.</param>
    /// <returns><see langword="true"/> when the candidate satisfies it.</returns>
    public bool IsSatisfiedBy(SemanticVersion version) => Operator switch
    {
        ComparatorOperator.Equal => version == Operand,
        ComparatorOperator.GreaterThan => version > Operand,
        ComparatorOperator.GreaterThanOrEqual => version >= Operand,
        ComparatorOperator.LessThan => version < Operand,
        ComparatorOperator.LessThanOrEqual => version <= Operand,
        _ => false
    };

    /// <summary>
    /// Gets the comparison in the form the parser reads back.
    /// </summary>
    /// <returns>The comparison text.</returns>
    public override string ToString() => Symbol(Operator) + Operand;

    private static string Symbol(ComparatorOperator value) => value switch
    {
        ComparatorOperator.Equal => "=",
        ComparatorOperator.GreaterThan => ">",
        ComparatorOperator.GreaterThanOrEqual => ">=",
        ComparatorOperator.LessThan => "<",
        ComparatorOperator.LessThanOrEqual => "<=",
        _ => "="
    };
}

/// <summary>
/// A conjunction of comparisons: every one of them must hold.
/// </summary>
public sealed class ComparatorSet
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ComparatorSet"/> class.
    /// </summary>
    /// <param name="comparators">The comparisons, all of which must hold.</param>
    /// <exception cref="ArgumentNullException"><paramref name="comparators"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="comparators"/> is empty.</exception>
    public ComparatorSet(IReadOnlyList<VersionComparator> comparators)
    {
        ArgumentNullException.ThrowIfNull(comparators);

        if (comparators.Count == 0)
        {
            throw new ArgumentException("A comparator set must contain at least one comparison.", nameof(comparators));
        }

        Comparators = comparators;
    }

    /// <summary>
    /// Gets the comparisons, all of which must hold.
    /// </summary>
    public IReadOnlyList<VersionComparator> Comparators { get; }

    /// <summary>
    /// Gets the lowest version this set excludes from above, or <see langword="null"/> when it admits
    /// arbitrarily high versions.
    /// </summary>
    /// <remarks>
    /// A sound bound rather than the tightest one: an inclusive upper comparison is reported as the next
    /// patch, because the exclusive bound that would be exact is not representable. Soundness is what the
    /// experimental gate needs — it must never conclude that an unbounded range is bounded.
    /// </remarks>
    public SemanticVersion? UpperBoundExclusive
    {
        get
        {
            SemanticVersion? bound = null;

            foreach (var comparator in Comparators)
            {
                var candidate = comparator.Operator switch
                {
                    ComparatorOperator.LessThan => comparator.Operand,
                    ComparatorOperator.LessThanOrEqual => NextPatch(comparator.Operand),
                    ComparatorOperator.Equal => NextPatch(comparator.Operand),
                    _ => (SemanticVersion?)null
                };

                if (candidate is { } value && (bound is null || value < bound.Value))
                {
                    bound = value;
                }
            }

            return bound;
        }
    }

    /// <summary>
    /// Determines whether a version satisfies every comparison in the set.
    /// </summary>
    /// <param name="version">The candidate.</param>
    /// <returns><see langword="true"/> when the candidate satisfies all of them.</returns>
    public bool IsSatisfiedBy(SemanticVersion version)
    {
        foreach (var comparator in Comparators)
        {
            if (!comparator.IsSatisfiedBy(version))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the set in the form the parser reads back.
    /// </summary>
    /// <returns>The set text.</returns>
    public override string ToString() => string.Join(' ', Comparators);

    private static SemanticVersion NextPatch(SemanticVersion version)
        => new(version.Major, version.Minor, version.Patch + 1);
}

/// <summary>
/// A contract range, as a disjunction of conjunctions.
/// </summary>
/// <remarks>
/// <para>
/// The grammar is a documented subset of the npm and yarn range syntax: comparison operators, whitespace
/// conjunction, alternation, and partial versions. Caret and tilde are rejected because their behavior
/// below <c>1.0.0</c> has been ambiguous for a decade and every governing document in this repository
/// writes an explicit lower and upper bound instead. Wildcards and hyphen ranges are rejected because four
/// spellings of "the 0.3 line" is three too many, and a range grammar is itself an unversioned contract:
/// every form admitted must be parsed identically forever.
/// </para>
/// <para>
/// An unparseable range is a load failure, never a lenient reinterpretation. A loader that guesses what an
/// extension author meant by a range it does not understand is a loader that will one day guess wrong about
/// a compatibility boundary.
/// </para>
/// </remarks>
public sealed class VersionRange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VersionRange"/> class.
    /// </summary>
    /// <param name="sets">The alternatives, any one of which may hold.</param>
    /// <param name="text">The range as written, kept verbatim for diagnostics.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sets"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sets"/> is empty.</exception>
    public VersionRange(IReadOnlyList<ComparatorSet> sets, string text)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (sets.Count == 0)
        {
            throw new ArgumentException("A version range must contain at least one comparator set.", nameof(sets));
        }

        Sets = sets;
        Text = text;
    }

    /// <summary>
    /// Gets the alternatives, any one of which may hold.
    /// </summary>
    public IReadOnlyList<ComparatorSet> Sets { get; }

    /// <summary>
    /// Gets the range exactly as the manifest wrote it.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the lowest version the whole range excludes from above, or <see langword="null"/> when any
    /// alternative admits arbitrarily high versions.
    /// </summary>
    public SemanticVersion? UpperBoundExclusive
    {
        get
        {
            SemanticVersion? bound = null;

            foreach (var set in Sets)
            {
                if (set.UpperBoundExclusive is not { } candidate)
                {
                    return null;
                }

                if (bound is null || candidate > bound.Value)
                {
                    bound = candidate;
                }
            }

            return bound;
        }
    }

    /// <summary>
    /// Determines whether a version satisfies the range.
    /// </summary>
    /// <param name="version">The candidate.</param>
    /// <returns><see langword="true"/> when any alternative admits it.</returns>
    public bool IsSatisfiedBy(SemanticVersion version) => Sets.Any(set => set.IsSatisfiedBy(version));

    /// <summary>
    /// Determines whether the range is narrow enough for an extension to depend on experimental contracts.
    /// </summary>
    /// <param name="hostVersion">The version of the contract assembly the host is running.</param>
    /// <returns><see langword="true"/> when the range is permitted.</returns>
    /// <remarks>
    /// The stability policy allows an experimental contract to change in any minor release, so a range that
    /// reaches past the next minor is a promise the host cannot keep. Requiring an upper bound at or below
    /// the next minor is the revocability the policy was written to buy: it is why a contract may be
    /// published before its shape has settled, and it is why an unbounded range is rejected rather than
    /// trusted.
    /// </remarks>
    public bool SatisfiesExperimentalGate(SemanticVersion hostVersion)
        => UpperBoundExclusive is { } upper
            && upper <= new SemanticVersion(hostVersion.Major, hostVersion.Minor + 1, 0);

    /// <summary>
    /// Gets the range as written.
    /// </summary>
    /// <returns>The range text.</returns>
    public override string ToString() => Text;
}
