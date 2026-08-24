namespace Arronix.Plugins.Manifest;

/// <summary>
/// One package this package needs installed beside it.
/// </summary>
/// <remarks>
/// <para>
/// A dependency is one edge: an exact package identifier and one range of compatible versions of it.
/// Which package is wanted is knowable when the dependency is written; which build of it an operator will
/// install beside it is not, which is why one half is exact and the other is a range. There is no second
/// kind of edge and no named sub-part of a package to depend on: requiring a package grants its shared
/// contract assemblies and, when it carries executable behavior, requires that exact package to be
/// admitted before this one.
/// </para>
/// <para>
/// The range is a predicate against the one installed version, never a selector among several. Nothing in
/// the platform chooses between two installed copies of one identifier, so a range can only admit or
/// refuse what is actually there.
/// </para>
/// </remarks>
public sealed record PackageDependencyDeclaration
{
    /// <summary>
    /// Gets the exact identifier of the required package.
    /// </summary>
    public required string Package { get; init; }

    /// <summary>
    /// Gets the versions of it this package is compatible with, in the grammar described by
    /// <see cref="Versioning.VersionRangeParser"/>.
    /// </summary>
    /// <remarks>
    /// Named for what it holds. The value is a range expression rather than a version, and this repository
    /// has twice recorded that naming a thing for what it is not is a defect rather than a shorthand.
    /// </remarks>
    public required string Range { get; init; }
}
