using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Intent;

/// <summary>
/// Declares that a kind's items may be ordered by one field, and which way round is usually wanted.
/// </summary>
/// <param name="FieldId">The <see cref="FieldDescriptor.FieldId"/> ordered by.</param>
/// <param name="Name">The ordering's display name.</param>
/// <param name="DefaultDirection">The direction applied when the user expresses no preference.</param>
/// <remarks>
/// The default direction is part of the declaration because it is domain knowledge, not a preference: the
/// useful end of a date is the recent end and the useful end of a title is the beginning, and no consumer
/// can work that out from the field's type.
/// </remarks>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record SortOption(string FieldId, string Name, SortDirection DefaultDirection);

/// <summary>
/// Which way round an ordering runs.
/// </summary>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum SortDirection
{
    /// <summary>From least to greatest.</summary>
    Ascending = 0,

    /// <summary>From greatest to least.</summary>
    Descending = 1
}
