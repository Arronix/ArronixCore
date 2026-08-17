using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Definition;

/// <summary>
/// One field row of a rendered summary.
/// </summary>
/// <param name="Label">The label shown beside the value.</param>
/// <param name="Template">The value template over fields, files and links.</param>
/// <param name="Weight">How important the row is; a one-line destination takes only the primary rows.</param>
[Experimental(ExperimentalContracts.Definition, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct SummaryFieldRule(string Label, string Template, SummaryFieldWeight Weight);
