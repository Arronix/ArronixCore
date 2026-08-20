
namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property the kind's items may be ordered by.
/// </summary>
/// <remarks>
/// One ordering is derived per marked property, and its default direction is derived from the property's
/// type: the useful end of a number or a date is the recent, largest end, and the useful end of text is the
/// beginning. A kind that disagrees says so once in its intent definition rather than restating every
/// ordering.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class SortableAttribute : Attribute;
