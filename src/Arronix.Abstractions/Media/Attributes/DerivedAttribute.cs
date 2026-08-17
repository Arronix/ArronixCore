using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks a property whose value the host recomputes: not editable by a user, not supplied by a cataloger,
/// rewritten whenever its inputs change.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a constraint that is easy to miss and expensive to discover late. A value that is
/// computed <i>and</i> queryable cannot be a computed property: an expression-bodied property is invisible
/// to query translation, so ordering a library by it would fall back to evaluating every row in memory. So
/// it is a stored property the host recomputes on write, and this attribute is the declaration of exactly
/// that.
/// </para>
/// <para>
/// The recomputation itself is a method on the kind's own type, bound on the builder. That is the whole of
/// what used to be a miniature expression grammar carried in strings: a reduction over dates, a comparison
/// against a window, a conditional — all of them ordinary code once the model is typed.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class DerivedAttribute : Attribute;
