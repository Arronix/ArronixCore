using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Arronix.Host.Media.Typed;

/// <summary>
/// Turns an expression over an entity into the field paths and templates the carried declarations are
/// written in.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole mechanical value of the typed authoring surface at the seams that are not yet typed. A
/// declaration that named fields by string — a key template, a query's required fields, a summary's
/// headline — named them in a grammar nothing checked until load and no rename ever touched. The author now
/// writes an expression, the compiler checks it, and this turns it back into the string the existing engine
/// reads.
/// </para>
/// <para>
/// <b>The limitation, stated rather than hidden.</b> An expression that filters or projects a list derives
/// to the list's own field path: <c>m =&gt; m.Titles.Where(t =&gt; t.Role == Release).Select(t =&gt;
/// t.Text)</c> becomes <c>{titles.text}</c> and the predicate is lost. That is not a defect of the
/// expression — it is that the declaration being derived into has no slot for a predicate. It disappears
/// when the match and query declarations are themselves typed; until then a kind whose correctness depends
/// on such a filter must not rely on this path.
/// </para>
/// </remarks>
internal static class ExpressionPaths
{
    /// <summary>
    /// Reads the single field an expression names.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The field identifier.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The expression names no field of the parameter.</exception>
    internal static string FieldId(LambdaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var paths = Paths(expression);

        return paths.Count == 1
            ? paths[0]
            : throw new ArgumentException(
                $"The expression '{expression}' names {paths.Count} fields where exactly one was expected.",
                nameof(expression));
    }

    /// <summary>
    /// Reads the direct member an expression names.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The member name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The expression is not a single property access.</exception>
    internal static string DirectMemberName(LambdaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var body = Unwrap(expression.Body);

        return body is MemberExpression { Expression: ParameterExpression } member
            ? member.Member.Name
            : throw new ArgumentException(
                $"The expression '{expression}' is not a direct member access on its parameter.",
                nameof(expression));
    }

    /// <summary>
    /// Reads every field path an expression reaches into, in the order they appear.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The field paths.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    internal static IReadOnlyList<string> Paths(LambdaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var collector = new PathCollector(expression.Parameters);
        collector.Visit(expression.Body);

        return collector.Paths;
    }

    /// <summary>
    /// Turns an expression into the brace template the carried declarations are written in:
    /// <c>m =&gt; $"{m.Title} ({m.Year})"</c> becomes <c>"{title} ({year})"</c>.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The template.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The expression is not a template.</exception>
    internal static string Template(LambdaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return Render(expression, Unwrap(expression.Body));
    }

    /// <summary>
    /// Joins the field paths an expression reaches into with the separator a key template uses.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The key template.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expression"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The expression names no field.</exception>
    internal static string KeyTemplate(LambdaExpression expression)
    {
        var paths = Paths(expression);

        return paths.Count > 0
            ? string.Join('|', paths.Select(static path => $"{{{path}}}"))
            : throw new ArgumentException(
                $"The expression '{expression}' names no field to key on.",
                nameof(expression));
    }

    private static string Render(LambdaExpression expression, Expression body)
    {
        switch (body)
        {
            case ConstantExpression { Value: string literal }:
                return literal;

            case MemberExpression:
                return $"{{{FieldId(expression)}}}";

            // An interpolated string in an expression tree is a call to string.Format or string.Concat.
            case MethodCallExpression call
                when call.Method.DeclaringType == typeof(string)
                    && string.Equals(call.Method.Name, nameof(string.Format), StringComparison.Ordinal):
                return RenderFormat(expression, call);

            case MethodCallExpression call
                when call.Method.DeclaringType == typeof(string)
                    && string.Equals(call.Method.Name, nameof(string.Concat), StringComparison.Ordinal):
                return RenderConcat(expression, call);

            default:
                throw new ArgumentException(
                    $"The expression '{expression}' is not a template: a template is a field, a literal, or "
                    + "an interpolation of them.",
                    nameof(expression));
        }
    }

    private static string RenderFormat(LambdaExpression expression, MethodCallExpression call)
    {
        var arguments = call.Arguments;

        if (arguments.Count == 0 || Unwrap(arguments[0]) is not ConstantExpression { Value: string format })
        {
            throw new ArgumentException(
                $"The expression '{expression}' formats with a computed format string, which cannot become a "
                + "template.",
                nameof(expression));
        }

        var holes = arguments.Count == 2 && Unwrap(arguments[1]) is NewArrayExpression array
            ? array.Expressions.Select(Unwrap).ToArray()
            : [.. arguments.Skip(1).Select(Unwrap)];

        var rendered = new StringBuilder(format.Length + (holes.Length * 12));

        for (var index = 0; index < format.Length; index++)
        {
            if (format[index] != '{')
            {
                rendered.Append(format[index]);
                continue;
            }

            var close = format.IndexOf('}', index);

            if (close < 0
                || !int.TryParse(
                    format.AsSpan(index + 1, close - index - 1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var hole)
                || hole >= holes.Length)
            {
                throw new ArgumentException(
                    $"The expression '{expression}' uses a format hole this derivation cannot read.",
                    nameof(expression));
            }

            rendered.Append('{').Append(PathOf(expression, holes[hole])).Append('}');
            index = close;
        }

        return rendered.ToString();
    }

    private static string RenderConcat(LambdaExpression expression, MethodCallExpression call)
    {
        var parts = call.Arguments.Count == 1 && Unwrap(call.Arguments[0]) is NewArrayExpression array
            ? array.Expressions.Select(Unwrap).ToArray()
            : [.. call.Arguments.Select(Unwrap)];

        var rendered = new StringBuilder();

        foreach (var part in parts)
        {
            if (part is ConstantExpression { Value: string literal })
            {
                rendered.Append(literal);
                continue;
            }

            rendered.Append('{').Append(PathOf(expression, part)).Append('}');
        }

        return rendered.ToString();
    }

    private static string PathOf(LambdaExpression expression, Expression hole)
    {
        var collector = new PathCollector(expression.Parameters);
        collector.Visit(hole);

        return collector.Paths.Count == 1
            ? collector.Paths[0]
            : throw new ArgumentException(
                $"The expression '{expression}' interpolates something that is not one field.",
                nameof(expression));
    }

    private static Expression Unwrap(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } cast
            ? Unwrap(cast.Operand)
            : expression;

    /// <summary>
    /// Collects the dotted field paths an expression reaches into, rooted at one of the lambda's
    /// parameters.
    /// </summary>
    private sealed class PathCollector(IReadOnlyList<ParameterExpression> parameters) : ExpressionVisitor
    {
        private readonly List<string> _paths = [];

        internal IReadOnlyList<string> Paths => _paths;

        protected override Expression VisitMember(MemberExpression node)
        {
            if (TryPath(node, out var path))
            {
                if (!_paths.Contains(path, StringComparer.Ordinal))
                {
                    _paths.Add(path);
                }

                return node;
            }

            return base.VisitMember(node);
        }

        private bool TryPath(MemberExpression node, out string path)
        {
            var segments = new List<string>();
            Expression? current = node;

            while (current is MemberExpression member)
            {
                segments.Insert(0, DerivedNames.Identifier(member.Member.Name));
                current = member.Expression;
            }

            // A path rooted at a lambda parameter is a field path. Anything else — a captured local, a
            // static — is a value the expression closes over, not a field of the entity.
            if (current is ParameterExpression parameter && parameters.Contains(parameter))
            {
                path = string.Join('.', segments);
                return true;
            }

            path = string.Empty;
            return false;
        }
    }
}
