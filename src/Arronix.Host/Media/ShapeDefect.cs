using Arronix.Abstractions.Health;

namespace Arronix.Host.Media;

/// <summary>
/// One thing wrong with a declared shape.
/// </summary>
/// <param name="Path">
/// Where the fault is, in the declaration's own terms — <c>levels[2].variant</c>, <c>fileBinding.unit</c>.
/// </param>
/// <param name="Message">What is wrong, phrased so it can be acted on without the source.</param>
/// <param name="Code">The machine-readable code the load failure will carry.</param>
/// <remarks>
/// Validation reports every defect it finds rather than the first. Correcting a shape one
/// error per build cycle is the experience this type exists to avoid, and the loader has no reason to stop
/// early: it has already decided not to admit the shape.
/// </remarks>
public readonly record struct ShapeDefect(string Path, string Message, CoreErrorCode Code);
