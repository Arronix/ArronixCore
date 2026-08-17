using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a whole-number property that counts things, as distinct from one that measures them.
/// </summary>
/// <remarks>
/// A count may be shown against a total — "nine of twelve" — and a plain integer may not. That is the whole
/// of the distinction, and it is not recoverable from the type.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class CountAttribute : Attribute;
