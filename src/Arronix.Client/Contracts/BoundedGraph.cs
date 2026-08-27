namespace Arronix.Client.Contracts;

/// <summary>Walks a graph a contract produced, refusing one too deep, too wide, or self-referential.</summary>
/// <remarks>
/// Iterative and bounded: a schema and a projection are both shapes the contract's own code returns, and
/// both are walked recursively elsewhere. Reused by the projection lane for the same reason.
/// </remarks>
internal static class BoundedGraph
{
    /// <summary>The deepest nesting a contract may describe. Real shapes nest two or three levels.</summary>
    internal const int MaxDepth = 32;

    /// <summary>The most nodes a contract may describe, across the whole graph.</summary>
    internal const int MaxNodes = 4096;

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

        // Reference identity, not equality: two equal nodes are two nodes, and only the same object reached
        // twice on one path is a cycle.
        var open = new HashSet<T>(ReferenceEqualityComparer.Instance as IEqualityComparer<T>);
        var pending = new Stack<(T Node, int Depth, bool Leaving)>();
        var scheduled = 0;

        // The budget is spent when an entry is scheduled, not when it is reached. A walk that counted what
        // it had popped would let every one of MaxNodes nodes schedule MaxNodes children before the next pop
        // noticed, which is millions of pending entries and the exhaustion this bound exists to prevent.
        string? Schedule(IReadOnlyList<T> nodes, int depth)
        {
            var count = nodes.Count;

            if (count < 0 || count > MaxNodes - scheduled)
            {
                return $"{subject} describes more than {MaxNodes} values.";
            }

            for (var index = count - 1; index >= 0; index--)
            {
                // Read once. The list is the contract's own object, so a second read may answer differently,
                // and what was checked would not be what was walked.
                var node = nodes[index];

                if (node is null)
                {
                    return $"{subject} carries a null entry.";
                }

                pending.Push((node, depth, false));
            }

            scheduled += count;
            return null;
        }

        if (Schedule(roots, 1) is { } wide)
        {
            return wide;
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

            if (children(node) is { } nested && Schedule(nested, depth + 1) is { } tooWide)
            {
                return tooWide;
            }
        }

        return null;
    }
}
