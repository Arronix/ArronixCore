using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Arronix.Abstractions.Quality;

// Reads and produces the experimental quality contracts.
#pragma warning disable ARX0021

namespace Arronix.Host.Engines.Quality;

/// <summary>
/// Rewrites a rendering rule written against a family's typed facts onto the erased point everything
/// downstream holds.
/// </summary>
/// <remarks>
/// <para>
/// This is why a rendering rule is authored as an expression and not as a delegate. A family writes
/// <c>f =&gt; f.Origin.Or(…) == …</c> against its own facts, and the renderer is handed points read from
/// storage, from a probe or from a pasted label — none of which are facts. A compiled delegate cannot be
/// pointed at a different type; an expression can be read.
/// </para>
/// <para>
/// <b>The grammar is small on purpose, and it is checked rather than assumed.</b> A rule is a boolean
/// combination of atoms, each atom naming exactly one axis: <c>f.A.IsKnown</c>, <c>f.A.Has(member)</c>, and
/// a comparison between <c>f.A.Or(fallback)</c> and a constant. Anything else throws when the family is
/// registered, which is the moment a person can still fix it. The alternative — a general expression
/// interpreter — is a predicate micro-grammar rebuilt for rendering, and deriving identities from
/// properties exists precisely so that there is not one of those.
/// </para>
/// <para>
/// Comparison is on the value's own number: a member's declared rank, which is its enumeration member's
/// numeric value, and a quantity's magnitude. So the rewritten rule means exactly what the source rule
/// means rather than what a policy would mean by it — a rendering has no opinion about richness, and an
/// axis declared descending renders by its raw count like any other.
/// </para>
/// </remarks>
internal static class QualityLabelPredicate
{
    /// <summary>Rewrites one rendering rule onto a point.</summary>
    /// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
    /// <param name="rule">The rule, as authored.</param>
    /// <param name="axes">The declared axes, keyed by the property that declares each.</param>
    /// <returns>The rewritten predicate, and the values it asserts outright.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rule"/> or <paramref name="axes"/> is null.</exception>
    /// <exception cref="ArgumentException">The rule is outside the rendering grammar.</exception>
    internal static CompiledLabelRule Compile<TFacts>(
        Expression<Func<TFacts, bool>> rule,
        IReadOnlyDictionary<string, DeclaredAxis> axes)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(axes);

        var rewriter = new Rewriter(rule, axes);
        var predicate = rewriter.Build(rule.Body);
        var assertions = new List<AxisAssertion>();

        rewriter.Collect(rule.Body, assertions);

        return new CompiledLabelRule(predicate, assertions);
    }

    /// <summary>Reads the axis a suffix is declared over.</summary>
    /// <typeparam name="TFacts">The family's quality-facts type.</typeparam>
    /// <typeparam name="TValue">The axis's value type.</typeparam>
    /// <param name="axis">The axis, as a property reference.</param>
    /// <param name="axes">The declared axes, keyed by the property that declares each.</param>
    /// <returns>The axis.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="axis"/> or <paramref name="axes"/> is null.</exception>
    /// <exception cref="ArgumentException">The expression is not a direct axis property access.</exception>
    internal static DeclaredAxis AxisOf<TFacts, TValue>(
        Expression<Func<TFacts, Evidence<TValue>>> axis,
        IReadOnlyDictionary<string, DeclaredAxis> axes)
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(axis);
        ArgumentNullException.ThrowIfNull(axes);

        if (Unwrap(axis.Body) is MemberExpression { Member: PropertyInfo property, Expression: ParameterExpression }
            && axes.TryGetValue(property.Name, out var declared))
        {
            return declared;
        }

        throw new ArgumentException(
            $"'{axis}' does not name one axis of '{typeof(TFacts).Name}'. A suffix is declared over a single "
            + "axis property, so that the identity it renders is the identity everything else compares on.",
            nameof(axis));
    }

    /// <summary>Reads the number a value compares on, in the terms its own axis speaks.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="value">The value.</param>
    /// <returns>The number, or negative infinity when the value is absent.</returns>
    internal static double Ordinate(DeclaredAxis axis, AxisValue value) =>
        !value.IsKnown ? double.NegativeInfinity
        : axis.Kind == AxisValueShape.Quantity ? value.Magnitude
        : value.DeclaredRank;

    private static Expression Unwrap(Expression node) =>
        node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } cast
            ? Unwrap(cast.Operand)
            : node;

    /// <summary>Turns one authored rule into a predicate over a point.</summary>
    private sealed class Rewriter(LambdaExpression rule, IReadOnlyDictionary<string, DeclaredAxis> axes)
    {
        internal Func<QualityPoint, bool> Build(Expression node)
        {
            switch (node)
            {
                case BinaryExpression { NodeType: ExpressionType.AndAlso } conjunction:
                {
                    var left = Build(conjunction.Left);
                    var right = Build(conjunction.Right);

                    return point => left(point) && right(point);
                }

                case BinaryExpression { NodeType: ExpressionType.OrElse } disjunction:
                {
                    var left = Build(disjunction.Left);
                    var right = Build(disjunction.Right);

                    return point => left(point) || right(point);
                }

                case UnaryExpression { NodeType: ExpressionType.Not } negation:
                {
                    var inner = Build(negation.Operand);

                    return point => !inner(point);
                }

                case ConstantExpression { Value: bool literal }:
                    return _ => literal;

                default:
                    return BuildAtom(node);
            }
        }

        /// <summary>Collects the values the rule's top-level conjunction states outright.</summary>
        /// <param name="node">The rule body, or a branch of it.</param>
        /// <param name="assertions">Receives the assertions.</param>
        /// <remarks>
        /// Only equality and set membership are collected, and only from a conjunction. A rule that says an
        /// axis is <i>at least</i> something has not told you what value spells its word, and a rule under a
        /// disjunction or a negation has told you about a branch that may not be the one taken. Everything
        /// read back from a label is verified by re-rendering it, so under-collecting costs a parse and
        /// over-collecting would cost a wrong point.
        /// </remarks>
        internal void Collect(Expression node, List<AxisAssertion> assertions)
        {
            if (node is BinaryExpression { NodeType: ExpressionType.AndAlso } conjunction)
            {
                Collect(conjunction.Left, assertions);
                Collect(conjunction.Right, assertions);

                return;
            }

            // A rule offering alternatives is read back into its first one. The alternatives spell the same
            // word by construction — that is what makes them one rule — so any of them would do, and the
            // first is the one a reader would name.
            if (node is BinaryExpression { NodeType: ExpressionType.OrElse } disjunction)
            {
                Collect(disjunction.Left, assertions);

                return;
            }

            if (node is BinaryExpression bound && IsWitnessable(bound.NodeType))
            {
                if (TryAxisTerm(bound.Left, out var axis, out _) && TryValue(axis, bound.Right, out var value))
                {
                    assertions.Add(Witness(axis, bound.NodeType, value));
                }
                else if (TryAxisTerm(bound.Right, out axis, out _) && TryValue(axis, bound.Left, out value))
                {
                    assertions.Add(Witness(axis, Mirror(bound.NodeType), value));
                }

                return;
            }

            if (node is MethodCallExpression { Method.Name: "Has", Arguments.Count: 1 } call
                && TryAxis(call.Object, out var set)
                && TryValue(set, call.Arguments[0], out var member))
            {
                assertions.Add(new AxisAssertion(set, member));
            }
        }

        /// <summary>Names one value that satisfies a bound.</summary>
        /// <param name="axis">The axis.</param>
        /// <param name="comparison">The comparison.</param>
        /// <param name="bound">The bound.</param>
        /// <returns>The assertion.</returns>
        /// <remarks>
        /// A bound does not say what value spells a word, but it does say one that would, and one is all a
        /// parse needs — the point it builds is re-rendered and checked, so a witness that turns out to spell
        /// a different word costs a failed parse and never a wrong point. A strict bound steps one place
        /// past itself, which is the next member of a closed axis and the next count of a quantity.
        /// </remarks>
        private static AxisAssertion Witness(DeclaredAxis axis, ExpressionType comparison, AxisValue bound)
        {
            var step = comparison switch
            {
                ExpressionType.GreaterThan => 1,
                ExpressionType.LessThan => -1,
                _ => 0,
            };

            if (step == 0)
            {
                return new AxisAssertion(axis, bound);
            }

            if (axis.Kind == AxisValueShape.Quantity)
            {
                return new AxisAssertion(axis, AxisValue.Quantity(bound.Magnitude + step));
            }

            var rank = bound.DeclaredRank + step;

            foreach (var member in axis.Axis.Members)
            {
                if (member.DeclaredRank == rank)
                {
                    return new AxisAssertion(axis, member);
                }
            }

            return new AxisAssertion(axis, bound);
        }

        private static bool IsWitnessable(ExpressionType nodeType) =>
            nodeType is ExpressionType.Equal
                or ExpressionType.LessThan
                or ExpressionType.LessThanOrEqual
                or ExpressionType.GreaterThan
                or ExpressionType.GreaterThanOrEqual;

        private Func<QualityPoint, bool> BuildAtom(Expression node)
        {
            if (node is MemberExpression { Member: PropertyInfo { Name: nameof(Evidence<int>.IsKnown) } } presence
                && TryAxis(presence.Expression, out var present))
            {
                var known = present.Axis.Id;

                return point => point[known] is { IsKnown: true, Values.Count: > 0 };
            }

            if (node is MethodCallExpression { Method.Name: "Has", Arguments.Count: 1 } call
                && TryAxis(call.Object, out var set)
                && TryValue(set, call.Arguments[0], out var member))
            {
                var holder = set.Axis.Id;

                return point => point[holder].Holds(member);
            }

            if (node is BinaryExpression comparison && IsComparison(comparison.NodeType))
            {
                return BuildComparison(comparison);
            }

            throw Unsupported(node);
        }

        private Func<QualityPoint, bool> BuildComparison(BinaryExpression comparison)
        {
            if (TryAxisTerm(comparison.Left, out var axis, out var fallback)
                && TryValue(axis, comparison.Right, out var bound))
            {
                return Compare(axis, fallback, comparison.NodeType, bound);
            }

            if (TryAxisTerm(comparison.Right, out axis, out fallback)
                && TryValue(axis, comparison.Left, out bound))
            {
                return Compare(axis, fallback, Mirror(comparison.NodeType), bound);
            }

            throw Unsupported(comparison);
        }

        private static Func<QualityPoint, bool> Compare(
            DeclaredAxis axis,
            AxisValue fallback,
            ExpressionType comparison,
            AxisValue bound)
        {
            var id = axis.Axis.Id;
            var right = Ordinate(axis, bound);
            var missing = Ordinate(axis, fallback);

            return point =>
            {
                var reading = point[id];
                var left = reading is { IsKnown: true, Values.Count: > 0 }
                    ? Ordinate(axis, reading.Values[0])
                    : missing;

                return comparison switch
                {
                    ExpressionType.Equal => left.Equals(right),
                    ExpressionType.NotEqual => !left.Equals(right),
                    ExpressionType.LessThan => left < right,
                    ExpressionType.LessThanOrEqual => left <= right,
                    ExpressionType.GreaterThan => left > right,
                    _ => left >= right,
                };
            };
        }

        private static bool IsComparison(ExpressionType nodeType) =>
            nodeType is ExpressionType.Equal
                or ExpressionType.NotEqual
                or ExpressionType.LessThan
                or ExpressionType.LessThanOrEqual
                or ExpressionType.GreaterThan
                or ExpressionType.GreaterThanOrEqual;

        private static ExpressionType Mirror(ExpressionType nodeType) =>
            nodeType switch
            {
                ExpressionType.LessThan => ExpressionType.GreaterThan,
                ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
                ExpressionType.GreaterThan => ExpressionType.LessThan,
                ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
                _ => nodeType,
            };

        private bool TryAxis(Expression? node, out DeclaredAxis axis)
        {
            axis = null!;

            if (node is not null
                && Unwrap(node) is MemberExpression
                {
                    Member: PropertyInfo property,
                    Expression: ParameterExpression,
                }
                && axes.TryGetValue(property.Name, out var declared))
            {
                axis = declared;

                return true;
            }

            return false;
        }

        private bool TryAxisTerm(Expression node, out DeclaredAxis axis, out AxisValue fallback)
        {
            axis = null!;
            fallback = AxisValue.None;

            return Unwrap(node) is MethodCallExpression { Method.Name: nameof(Evidence<int>.Or), Arguments.Count: 1 } call
                && TryAxis(call.Object, out axis)
                && TryValue(axis, call.Arguments[0], out fallback);
        }

        private bool TryValue(DeclaredAxis axis, Expression node, out AxisValue value)
        {
            value = AxisValue.None;

            var closed = Unwrap(node);

            if (ReferencesParameter(closed))
            {
                return false;
            }

            var literal = closed is ConstantExpression constant
                ? constant.Value
                : Expression.Lambda(Expression.Convert(closed, typeof(object))).Compile().DynamicInvoke();

            if (literal is null)
            {
                return false;
            }

            if (axis.Kind == AxisValueShape.Quantity)
            {
                value = AxisValue.Quantity(Convert.ToDouble(literal, CultureInfo.InvariantCulture));

                return true;
            }

            var member = literal.GetType().IsEnum
                ? literal
                : Enum.ToObject(axis.ValueType, Convert.ToInt32(literal, CultureInfo.InvariantCulture));

            value = AxisValue.Member(
                Convert.ToInt32(member, CultureInfo.InvariantCulture),
                member.ToString() ?? string.Empty);

            return true;
        }

        private bool ReferencesParameter(Expression node)
        {
            var finder = new ParameterFinder(rule.Parameters);

            finder.Visit(node);

            return finder.Found;
        }

        private ArgumentException Unsupported(Expression node) =>
            new(
                $"'{node}' is outside the rendering grammar of '{rule}'. A rendering rule combines, with and, "
                + "or and not, atoms of three shapes: an axis is known, a set axis holds a member, or an "
                + "axis's value compares against a constant.",
                nameof(rule));
    }

    /// <summary>Reports whether an expression reaches one of a lambda's parameters.</summary>
    private sealed class ParameterFinder(IReadOnlyList<ParameterExpression> parameters) : ExpressionVisitor
    {
        internal bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found |= parameters.Contains(node);

            return node;
        }
    }
}

/// <summary>One rendering rule, rewritten onto a point.</summary>
/// <param name="When">What must hold.</param>
/// <param name="Assertions">
/// The values the rule states outright in its top-level conjunction. These are what a label is read back
/// into: a rule saying the origin <i>is</i> a disc bitstream tells you which point spells that word, and a
/// rule saying a resolution is at least something does not.
/// </param>
internal sealed record CompiledLabelRule(
    Func<QualityPoint, bool> When,
    IReadOnlyList<AxisAssertion> Assertions);

/// <summary>One value a rendering rule states outright.</summary>
/// <param name="Axis">The axis.</param>
/// <param name="Value">The value.</param>
internal sealed record AxisAssertion(DeclaredAxis Axis, AxisValue Value);
