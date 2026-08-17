using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Media;

/// <summary>
/// States how important a property is, which is what a summary, a default column set and a verbose flag all
/// read.
/// </summary>
/// <remarks>
/// An importance rank, not a layout instruction, and the default rank is the one most properties want — so
/// only the properties that identify an entity, qualify it, or matter solely when something has gone wrong
/// carry this at all.
/// </remarks>
/// <param name="prominence">How important the property is.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class ProminenceAttribute(Prominence prominence) : Attribute
{
    /// <summary>
    /// Gets how important the property is.
    /// </summary>
    public Prominence Prominence { get; } = prominence;
}
