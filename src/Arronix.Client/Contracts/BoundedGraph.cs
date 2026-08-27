namespace Arronix.Client.Contracts;

/// <summary>
/// Walks a graph a contract produced, refusing one too deep or self-referential to describe.
/// </summary>
/// <remarks>
/// Iterative and bounded on purpose. A schema and a projection are both shapes the contract's own code
/// returns, and both are walked recursively everywhere else; a cycle or a few thousand levels of nesting
/// would take the process down rather than fail a payload. Reused by the projection lane for the same
/// reason it exists here.
/// </remarks>
internal static class BoundedGraph
{
    /// <summary>The deepest nesting a contract may describe. Real shapes nest two or three levels.</summary>
    internal const int MaxDepth = 32;

    /// <summary>
    /// Describes why a graph cannot be walked, or nothing when it can.
    /// </summary>
    /// <typeparam name="T">The node type.</typeparam>
    /// <param name="roots">The graph's roots.</param>
    /// <param name="children">A node's children.</param>
    /// <param name="subject">What to name in a refusal.</param>
    /// <returns>One sentence naming the defect, or <see langword="null"/>.</returns>
    internal static string? Exceeded<T>(
        IReadOnlyList<T>? roots,
        Func<T, IReadOnlyList<T>?> children,
        string subject)
        where T : class
    {
        if (roots is null)
        {
            return $"{subject} is absent rather than empty.";
        }

        // Reference identity, not equality: two equal descriptors are two nodes, and only the same object
        // reached twice on one path is a cycle.
        var open = new HashSet<T>(ReferenceEqualityComparer.Instance as IEqualityComparer<T>);
        var pending = new Stack<(T Node, int Depth, bool Leaving)>();

        for (var index = roots.Count - 1; index >= 0; index--)
        {
            if (roots[index] is null)
            {
                return $"{subject} carries a null entry.";
            }

            pending.Push((roots[index], 1, false));
        }

        while (pending.Count > 0)
        {
            var (node, depth, leaving) = pending.Pop();

            if (leaving)
            {
                open.Remove(node);
                continue;
            }

            if (!open.Add(node))
            {
                return $"{subject} contains itself, so it cannot be described.";
            }

            if (depth > MaxDepth)
            {
                return $"{subject} nests deeper than {MaxDepth} levels.";
            }

            pending.Push((node, depth, true));

            if (children(node) is not { } nested)
            {
                continue;
            }

            for (var index = nested.Count - 1; index >= 0; index--)
            {
                if (nested[index] is null)
                {
                    return $"{subject} carries a null entry.";
                }

                pending.Push((nested[index], depth + 1, false));
            }
        }

        return null;
    }
}
