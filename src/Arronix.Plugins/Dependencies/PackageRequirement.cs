using Arronix.Abstractions.Plugins;
using Arronix.Plugins.Versioning;

namespace Arronix.Plugins.Dependencies;

/// <summary>
/// One dependency edge: an exact package identifier and the versions of it that are compatible.
/// </summary>
/// <remarks>
/// The identifier is exact and the version is a range. A requirement that could name several identifiers
/// would be a search, and a search has to be resolved by preference, which this engine never does. The range
/// has already been proved by <see cref="VersionRangeParser"/>; nothing here reads range text.
/// </remarks>
internal sealed class PackageRequirement
{
    /// <summary>Initializes a new instance of the <see cref="PackageRequirement"/> class.</summary>
    /// <param name="packageId">The exact package required.</param>
    /// <param name="range">The versions of it that are compatible.</param>
    /// <exception cref="ArgumentException"><paramref name="packageId"/> is the default value.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="range"/> is <see langword="null"/>.</exception>
    public PackageRequirement(PluginId packageId, VersionRange range)
    {
        ArgumentNullException.ThrowIfNull(range);

        PackageId = PackageIdentity.Required(packageId, nameof(packageId));
        Range = range;
    }

    /// <summary>Gets the exact package required.</summary>
    public PluginId PackageId { get; }

    /// <summary>Gets the versions of it that are compatible.</summary>
    public VersionRange Range { get; }

    /// <inheritdoc />
    public override string ToString() => $"{PackageId} {Range}";
}
