using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Intent;

/// <summary>
/// Declares that a kind's items may be narrowed by one field, and which comparisons are meaningful for it.
/// </summary>
/// <param name="FieldId">The <see cref="FieldDescriptor.FieldId"/> narrowed by.</param>
/// <param name="Name">The filter's display name.</param>
/// <param name="Operators">The comparisons the source can answer for this field.</param>
/// <remarks>
/// The comparisons are declared rather than derived from the field's value shape because supporting a
/// comparison is a property of the source, not of the type: a source may hold a date it cannot answer
/// range queries against.
/// </remarks>
public sealed record FilterOption(string FieldId, string Name, FilterOperators Operators);

/// <summary>
/// The comparisons a filter may use.
/// </summary>
[Flags]
public enum FilterOperators
{
    /// <summary>No comparison. The field cannot be filtered on.</summary>
    None = 0,

    /// <summary>The value matches exactly.</summary>
    Equals = 1,

    /// <summary>The value does not match.</summary>
    NotEquals = 2,

    /// <summary>The value contains the given text.</summary>
    Contains = 4,

    /// <summary>The value is greater than the given one.</summary>
    GreaterThan = 8,

    /// <summary>The value is less than the given one.</summary>
    LessThan = 16,

    /// <summary>The value falls between two given ones, inclusive.</summary>
    Between = 32,

    /// <summary>The value is one of a given set.</summary>
    In = 64,

    /// <summary>The field has no value.</summary>
    IsNull = 128
}
