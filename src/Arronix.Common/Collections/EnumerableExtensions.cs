using System.Linq;

namespace Arronix.Common.Collections;

/// <summary>
/// The three sequence operations that survive .NET's own query operators.
/// </summary>
/// <remarks>
/// <para>
/// This is a deliberately short list. Everything else the platform inherited either duplicated a framework
/// operator or, worse, shadowed one: two members were named <c>IntersectBy</c> and <c>ExceptBy</c>, which
/// have since become framework operators with the same names, a different parameter order and different
/// semantics. Code calling those read as if it were calling the framework and did something else, which is
/// the single most expensive kind of helper to keep.
/// </para>
/// <para>
/// The file is named after the type it contains. Its predecessor was named for the interface the methods
/// extend, so the class and the file disagreed and neither could be found from the other.
/// </para>
/// </remarks>
public static class EnumerableExtensions
{
    /// <summary>
    /// Adds an item to a collection unless it is <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="collection">The collection to add to.</param>
    /// <param name="item">The item to add, which may be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collection"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Exists so that building a list from a run of optional values does not need a conditional statement per
    /// value. It takes <see cref="ICollection{T}"/> rather than a concrete list, because the sets and
    /// observable collections the platform builds want the same thing.
    /// </remarks>
    public static void AddIfNotNull<T>(this ICollection<T> collection, T? item)
    {
        ArgumentNullException.ThrowIfNull(collection);

        if (item is null)
        {
            return;
        }

        collection.Add(item);
    }

    /// <summary>
    /// Determines whether no element satisfies a condition.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The sequence to test.</param>
    /// <param name="predicate">The condition applied to each element.</param>
    /// <returns>
    /// <see langword="true"/> when no element satisfies <paramref name="predicate"/>; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="predicate"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Reads better than the negation it replaces, and reads correctly: a leading <c>!</c> in front of a long
    /// query expression is easy to miss, and this states the intent at the point the eye starts.
    /// </remarks>
    public static bool None<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        return !source.Any(predicate);
    }

    /// <summary>
    /// Builds a dictionary keyed by a selector, keeping the first item seen for each key and discarding
    /// later ones.
    /// </summary>
    /// <typeparam name="TItem">The element type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="source">The sequence to index.</param>
    /// <param name="keySelector">Produces the key for an element.</param>
    /// <param name="comparer">
    /// How keys are compared, or <see langword="null"/> for the type's default comparer.
    /// </param>
    /// <returns>A dictionary holding the first element seen for each distinct key.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="keySelector"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The framework's own dictionary projection throws on a duplicate key. Data arriving from a remote
    /// source routinely contains duplicates, and rejecting the whole payload because one entry repeats is
    /// rarely what the caller wants — so this states in its name that duplicates are dropped, rather than
    /// leaving the caller to discover it from a stack trace in production.
    /// </remarks>
    public static Dictionary<TKey, TItem> ToDictionaryIgnoreDuplicates<TItem, TKey>(
        this IEnumerable<TItem> source,
        Func<TItem, TKey> keySelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);

        var result = new Dictionary<TKey, TItem>(comparer);

        foreach (var item in source)
        {
            result.TryAdd(keySelector(item), item);
        }

        return result;
    }

    /// <summary>
    /// Builds a dictionary from a key and a value selector, keeping the first item seen for each key and
    /// discarding later ones.
    /// </summary>
    /// <typeparam name="TItem">The element type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <param name="source">The sequence to index.</param>
    /// <param name="keySelector">Produces the key for an element.</param>
    /// <param name="valueSelector">Produces the value for an element.</param>
    /// <param name="comparer">
    /// How keys are compared, or <see langword="null"/> for the type's default comparer.
    /// </param>
    /// <returns>A dictionary holding the value of the first element seen for each distinct key.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="source"/>, <paramref name="keySelector"/> or <paramref name="valueSelector"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The value selector is evaluated only for the element that wins its key, so a projection with a cost
    /// is not paid for entries that are about to be discarded.
    /// </remarks>
    public static Dictionary<TKey, TValue> ToDictionaryIgnoreDuplicates<TItem, TKey, TValue>(
        this IEnumerable<TItem> source,
        Func<TItem, TKey> keySelector,
        Func<TItem, TValue> valueSelector,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(valueSelector);

        var result = new Dictionary<TKey, TValue>(comparer);

        foreach (var item in source)
        {
            var key = keySelector(item);

            if (!result.ContainsKey(key))
            {
                result[key] = valueSelector(item);
            }
        }

        return result;
    }
}
