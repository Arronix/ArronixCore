namespace Arronix.Common.Text;

/// <summary>
/// Edit-distance measures between two pieces of text.
/// </summary>
/// <remarks>
/// <para>
/// Exposed as ordinary static methods over spans rather than as extension methods on <see cref="string"/>.
/// An edit distance is an algorithm a caller chooses deliberately, not a property text has, and hanging one
/// off every string in the platform both advertises it to code that should not be doing fuzzy matching and
/// forces an allocation on callers who already hold a slice.
/// </para>
/// <para>
/// Domain-specific matching — stripping punctuation, weighting the edit operations to favor one kind of
/// difference over another — is a policy decision belonging to whoever owns the domain being matched. This
/// type supplies only the primitive those policies are built from.
/// </para>
/// </remarks>
public static class StringDistance
{
    /// <summary>
    /// Length above which the working row is heap-allocated rather than taken from the stack.
    /// </summary>
    private const int StackAllocationLimit = 256;

    /// <summary>
    /// Computes the weighted Levenshtein distance: the least total cost of the insertions, deletions and
    /// substitutions that turn <paramref name="source"/> into <paramref name="target"/>.
    /// </summary>
    /// <param name="source">The text being transformed.</param>
    /// <param name="target">The text being transformed into.</param>
    /// <param name="insertionCost">
    /// Cost of adding one character that appears in <paramref name="target"/> but not in
    /// <paramref name="source"/>.
    /// </param>
    /// <param name="deletionCost">
    /// Cost of removing one character that appears in <paramref name="source"/> but not in
    /// <paramref name="target"/>.
    /// </param>
    /// <param name="substitutionCost">Cost of replacing one character with a different one.</param>
    /// <returns>The total cost of the cheapest edit sequence. Zero when the two are identical.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Any of the three costs is negative.</exception>
    /// <remarks>
    /// Comparison is ordinal and case-sensitive: the caller decides whether case or accents matter and
    /// normalizes before calling, because folding inside the measure would make the result depend on a
    /// culture the caller never named.
    /// </remarks>
    public static int Levenshtein(
        ReadOnlySpan<char> source,
        ReadOnlySpan<char> target,
        int insertionCost = 1,
        int deletionCost = 1,
        int substitutionCost = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(insertionCost);
        ArgumentOutOfRangeException.ThrowIfNegative(deletionCost);
        ArgumentOutOfRangeException.ThrowIfNegative(substitutionCost);

        if (source.SequenceEqual(target))
        {
            return 0;
        }

        if (source.IsEmpty)
        {
            return target.Length * insertionCost;
        }

        if (target.IsEmpty)
        {
            return source.Length * deletionCost;
        }

        // Only the previous row of the edit matrix is ever read, so one row is kept and rewritten in place.
        var row = target.Length < StackAllocationLimit
            ? stackalloc int[target.Length + 1]
            : new int[target.Length + 1];

        for (var column = 1; column < row.Length; column++)
        {
            row[column] = column * insertionCost;
        }

        for (var i = 0; i < source.Length; i++)
        {
            var diagonal = row[0];
            row[0] += deletionCost;

            for (var j = 0; j < target.Length; j++)
            {
                var above = row[j];
                var left = row[j + 1];

                var insertion = above + insertionCost;
                var deletion = left + deletionCost;
                var substitution = diagonal + (source[i] == target[j] ? 0 : substitutionCost);

                diagonal = row[j + 1];
                row[j + 1] = Math.Min(Math.Min(insertion, deletion), substitution);
            }
        }

        return row[target.Length];
    }
}
