using Arronix.Abstractions.Shape;

// The shape contracts are experimental; the join is expressed in their identity types.
#pragma warning disable ARX0013

namespace Arronix.Host.Storage;

/// <summary>
/// One unit, one file, and where the file sits within the unit when that means anything.
/// </summary>
/// <param name="Unit">What the file satisfies.</param>
/// <param name="File">The file.</param>
/// <param name="Ordinal">
/// The file's position within the unit, when the shape declares the ordinal meaningful. Null otherwise, and
/// the store refuses a value on a binding that says it means nothing.
/// </param>
/// <remarks>
/// <para>
/// The single most consequential storage decision in this milestone, taken deliberately before persistence
/// exists so that the eventual relational schema cannot be biased toward any one surveyed application's
/// foreign-key direction. All four of them are degenerate projections of this triple: one puts the key on
/// the file, one on the unit, one uses a join table, and one carries a part number. Picking any of their
/// directions as the platform's default would have broken the other three.
/// </para>
/// <para>
/// It is a join row and never a foreign key on either side. That is what lets one file satisfy several units
/// and one unit span several files without either case being a special one.
/// </para>
/// </remarks>
public readonly record struct UnitFileLink(MediaItemRef Unit, MediaFileId File, int? Ordinal);
