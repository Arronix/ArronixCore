using System.Linq;

namespace Arronix.Host.Engines.Matching;

/// <summary>
/// A weighted penalty accumulator over the closed six-operator distance vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// Ports Lidarr's <c>Distance</c>
/// (<c>_reference/Lidarr/src/NzbDrone.Core/MediaFiles/TrackImport/Identification/Distance.cs</c>): the
/// six operators — string, bool, number, ratio, equality, priority — their penalty semantics, and the
/// weighted normalization <c>raw / max</c>, with one deliberate difference: the weights arrive from the
/// declaration instead of a hard-coded literal dictionary, which is the half of the surface the audit
/// verified as data.
/// </para>
/// <para>
/// What each feature feeds the operators stays host code (the feature catalog): the operator API is
/// closed and the weights are data, but the arguments are imperative aggregations a path grammar cannot
/// reach.
/// </para>
/// </remarks>
internal sealed class DistanceAccumulator
{
    private readonly IReadOnlyDictionary<string, double> _weights;
    private readonly Dictionary<string, List<double>> _penalties = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="DistanceAccumulator"/> class.
    /// </summary>
    /// <param name="weights">The feature weights, keyed by feature identifier.</param>
    internal DistanceAccumulator(IReadOnlyDictionary<string, double> weights) => _weights = weights;

    /// <summary>
    /// Gets the accumulated penalties, keyed by feature identifier.
    /// </summary>
    internal IReadOnlyDictionary<string, List<double>> Penalties => _penalties;

    /// <summary>
    /// Adds one raw penalty in the closed interval [0, 1].
    /// </summary>
    /// <param name="featureId">The feature the penalty belongs to.</param>
    /// <param name="penalty">The penalty.</param>
    internal void Add(string featureId, double penalty)
    {
        if (_penalties.TryGetValue(featureId, out var list))
        {
            list.Add(penalty);
        }
        else
        {
            _penalties[featureId] = [penalty];
        }
    }

    /// <summary>
    /// Adds a penalty for <paramref name="value"/> as a ratio of <paramref name="target"/>, clamped to
    /// [0, 1].
    /// </summary>
    /// <param name="featureId">The feature the penalty belongs to.</param>
    /// <param name="value">The measured difference.</param>
    /// <param name="target">The difference that counts as total disagreement.</param>
    internal void AddRatio(string featureId, double value, double target) =>
        Add(featureId, target > 0 ? Math.Max(Math.Min(value, target), 0.0) / target : 0.0);

    /// <summary>
    /// Adds one full penalty per whole unit of disagreement, or a zero when the numbers agree.
    /// </summary>
    /// <param name="featureId">The feature the penalty belongs to.</param>
    /// <param name="value">The measured count.</param>
    /// <param name="target">The expected count.</param>
    internal void AddNumber(string featureId, int value, int target)
    {
        var difference = Math.Abs(value - target);
        if (difference == 0)
        {
            Add(featureId, 0.0);
            return;
        }

        for (var i = 0; i < difference; i++)
        {
            Add(featureId, 1.0);
        }
    }

    /// <summary>
    /// Adds a penalty of one minus the Levenshtein coefficient of the cleaned texts.
    /// </summary>
    /// <param name="featureId">The feature the penalty belongs to.</param>
    /// <param name="value">The measured text.</param>
    /// <param name="target">The expected text.</param>
    internal void AddString(string featureId, string? value, string? target)
    {
        var cleanValue = Clean(value ?? string.Empty);
        var cleanTarget = Clean(target ?? string.Empty);

        if (cleanValue.Length == 0)
        {
            Add(featureId, cleanTarget.Length == 0 ? 0.0 : 1.0);
            return;
        }

        Add(featureId, 1.0 - LevenshteinCoefficient(cleanValue, cleanTarget));
    }

    /// <summary>
    /// Adds a full penalty when the condition holds, a zero otherwise.
    /// </summary>
    /// <param name="featureId">The feature the penalty belongs to.</param>
    /// <param name="mismatch">Whether the feature disagrees.</param>
    internal void AddBool(string featureId, bool mismatch) => Add(featureId, mismatch ? 1.0 : 0.0);

    /// <summary>
    /// Adds a zero when the value is among the accepted options, a full penalty otherwise.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="featureId">The feature the penalty belongs to.</param>
    /// <param name="value">The measured value.</param>
    /// <param name="options">The accepted values.</param>
    internal void AddEquality<T>(string featureId, T value, IReadOnlyList<T> options)
        where T : IEquatable<T> =>
        Add(featureId, options.Contains(value) ? 0.0 : 1.0);

    /// <summary>
    /// Adds a penalty proportional to the value's position in a preference-ordered list, or a full
    /// penalty when the value is not listed.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="featureId">The feature the penalty belongs to.</param>
    /// <param name="value">The measured value.</param>
    /// <param name="preferenceOrder">The accepted values, most preferred first.</param>
    internal void AddPriority<T>(string featureId, T value, IReadOnlyList<T> preferenceOrder)
        where T : IEquatable<T>
    {
        var unit = 1.0 / (preferenceOrder.Count > 0 ? preferenceOrder.Count : 1.0);
        var index = -1;
        for (var i = 0; i < preferenceOrder.Count; i++)
        {
            if (preferenceOrder[i].Equals(value))
            {
                index = i;
                break;
            }
        }

        Add(featureId, index == -1 ? 1.0 : index * unit);
    }

    /// <summary>
    /// Gets the weighted distance normalized into [0, 1]: raw over maximum.
    /// </summary>
    /// <returns>The normalized distance; zero when nothing was penalized.</returns>
    internal double NormalizedDistance()
    {
        var max = _penalties.Sum(pair => pair.Value.Count * WeightOf(pair.Key));
        return max > 0 ? _penalties.Sum(pair => pair.Value.Sum() * WeightOf(pair.Key)) / max : 0.0;
    }

    private double WeightOf(string featureId) =>
        _weights.TryGetValue(featureId, out var weight) ? weight : 1.0;

    private static string Clean(string input)
    {
        Span<char> kept = stackalloc char[input.Length];
        var length = 0;

        foreach (var character in input)
        {
            if (char.IsLetterOrDigit(character))
            {
                kept[length++] = char.ToUpperInvariant(character);
            }
        }

        return new string(kept[..length]);
    }

    private static double LevenshteinCoefficient(string value, string target)
    {
        if (value.Length == 0 && target.Length == 0)
        {
            return 1.0;
        }

        return 1.0 - ((double)LevenshteinDistance(value, target) / Math.Max(value.Length, target.Length));
    }

    private static int LevenshteinDistance(string value, string target)
    {
        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];

        for (var j = 0; j <= target.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= value.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= target.Length; j++)
            {
                var substitution = previous[j - 1] + (value[i - 1] == target[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[target.Length];
    }
}
