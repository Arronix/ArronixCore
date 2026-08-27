using System.Linq;
using Arronix.Abstractions.Errors;
using Arronix.Abstractions.Health;
using Arronix.Abstractions.Shape;
using Arronix.Host.Media;

namespace Arronix.Host.Storage;

/// <summary>
/// What a library facet must satisfy before any store writes it.
/// </summary>
/// <remarks>
/// Here rather than in one implementation so that swapping where a facet is kept cannot change what a facet
/// is allowed to say. The rules come from the kind's own declaration, so one code path serves every kind.
/// </remarks>
internal static class LibraryFacetRules
{
    /// <summary>
    /// Requires every monitoring answer to name an axis the item's level declares, and to be one of that
    /// axis's choices when it enumerates them.
    /// </summary>
    /// <param name="shape">The kind's resolved shape.</param>
    /// <param name="facet">The facet being written.</param>
    /// <exception cref="ArronixException">An answer names an undeclared axis or an undeclared choice.</exception>
    internal static void RequireDeclaredMonitoring(ValidatedShape shape, LibraryFacet facet)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(facet);

        if (facet.Monitor.Count == 0)
        {
            return;
        }

        var level = shape.LevelOf(facet.Ref.Level);

        foreach (var (dimensionId, value) in facet.Monitor)
        {
            var dimension = level.MonitorDimensions
                .FirstOrDefault(candidate => string.Equals(candidate.DimensionId, dimensionId, StringComparison.Ordinal));

            if (dimension is null)
            {
                throw new ArronixException(
                    CoreErrorCode.InvalidConfiguration,
                    $"Level '{facet.Ref.Level}' of media kind '{shape.Kind}' declares no monitoring axis '{dimensionId}'.");
            }

            if (dimension.Kind == MonitorDimensionKind.Enumerated
                && !dimension.Choices.Any(choice => string.Equals(choice.Value, value, StringComparison.Ordinal)))
            {
                throw new ArronixException(
                    CoreErrorCode.InvalidConfiguration,
                    $"'{value}' is not one of the choices monitoring axis '{dimensionId}' declares.");
            }
        }
    }
}
