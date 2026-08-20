
namespace Arronix.Abstractions.Media;

/// <summary>A media selection that another typed definition may refer to.</summary>
public interface IDeclaredSelection
{
    /// <summary>Gets the stable selection identifier.</summary>
    string FacetId { get; }
}
