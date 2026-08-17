using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property recording when something happened.
/// </summary>
/// <remarks>
/// A sequence traversal is derived for each marked property. A schedule of what is coming next is a
/// traversal over a date, so declaring the date is enough: one consumer renders it as a calendar and
/// another as an ordered list, from the same declaration.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class TimestampAttribute : Attribute;
