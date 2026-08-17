using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property the kind's items may be partitioned by.
/// </summary>
/// <remarks>
/// A facet traversal is derived for each marked property, which is what turns "these items can be grouped
/// by studio" into a browse axis without anybody writing one.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class GroupableAttribute : Attribute;
