using Arronix.Abstractions.Plugins;

namespace Arronix.Plugins.Dependencies;

/// <summary>
/// How the dependency layer orders and validates package identifiers.
/// </summary>
/// <remarks>
/// Package identity is <see cref="PluginId"/>; there is no second identifier type. Ordering is ordinal over
/// the identifier text, which is the only ordering that is a property of the packages rather than of how
/// they were discovered.
/// </remarks>
internal static class PackageIdentity
{
    /// <summary>Gets the stable ordering of package identifiers.</summary>
    public static IComparer<PluginId> Order { get; } = new OrdinalComparer();

    /// <summary>
    /// Proves that an identifier was actually supplied.
    /// </summary>
    /// <param name="value">The identifier.</param>
    /// <param name="parameterName">The parameter being checked.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="ArgumentException">The identifier is the default value.</exception>
    public static PluginId Required(PluginId value, string parameterName)
        => string.IsNullOrEmpty(value.Value)
            ? throw new ArgumentException("A package identifier is required.", parameterName)
            : value;

    /// <summary>Renders a path of identifiers the way every diagnostic carrying one renders it.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The path text.</returns>
    public static string RenderPath(IEnumerable<PluginId> path) => string.Join(" -> ", path);

    private sealed class OrdinalComparer : IComparer<PluginId>
    {
        public int Compare(PluginId x, PluginId y) => string.CompareOrdinal(x.Value, y.Value);
    }
}
