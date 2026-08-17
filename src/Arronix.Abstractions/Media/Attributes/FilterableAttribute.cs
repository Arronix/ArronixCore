using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property the kind's items may be narrowed by.
/// </summary>
/// <remarks>
/// The comparison operators are <i>derived</i> from the property's type rather than written: text
/// contains and equals, numbers compare and fall between, enumerations are set membership, and anything
/// nullable also answers "has no value". Hand-writing the operator set per field is a table that drifts the
/// moment a property's type changes and cannot be checked against anything.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class FilterableAttribute : Attribute;
