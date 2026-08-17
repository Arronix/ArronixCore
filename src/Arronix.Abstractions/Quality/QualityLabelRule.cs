using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Quality;

/// <summary>One rendering rule: a predicate over a point, and the word it renders.</summary>
/// <param name="When">What must hold, over the erased point.</param>
/// <param name="Label">The community's word for it.</param>
/// <remarks>
/// <para>
/// A label is produced from a point and is never read back for a comparison. No comparison, no
/// eligibility test, no cutoff and no size assessment anywhere touches a rendered string — that invariant
/// is what stops rungs growing back through the renderer.
/// </para>
/// <para>
/// The predicate is a delegate rather than declared data, deliberately. It reads two or three axes at
/// once, and a declared-data form of that is a predicate micro-grammar rebuilt for rendering. A media
/// kind never writes one; only a family does, or a kind overriding its family's rendering, and both are
/// host-side code. The authoring form and this runtime form are different types on purpose: a builder
/// takes an expression over the typed facts, and the host rewrites it onto the erased point.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record QualityLabelRule(Func<QualityPoint, bool> When, string Label);
