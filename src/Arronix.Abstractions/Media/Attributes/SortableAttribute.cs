using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property the kind's items may be ordered by.
/// </summary>
/// <remarks>
/// One ordering is derived per marked property, and its default direction is derived from the property's
/// type: the useful end of a number or a date is the recent, largest end, and the useful end of text is the
/// beginning. A kind that disagrees says so once on the intent builder rather than restating every
/// ordering.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class SortableAttribute : Attribute;
