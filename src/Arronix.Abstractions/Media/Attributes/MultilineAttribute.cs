
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a text property whose value runs to several lines rather than one.
/// </summary>
/// <remarks>
/// Plain text either way — never markup. The distinction exists because a consumer laying out a table
/// needs to know which columns it must not try to fit on one row.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class MultilineAttribute : Attribute;
