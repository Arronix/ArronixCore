using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Intent;

/// <summary>
/// Declares that an item has a representation the platform does not own, reachable at a templated
/// address.
/// </summary>
/// <param name="SurfaceId">The identifier of the surface.</param>
/// <param name="Name">The surface's display name.</param>
/// <param name="LevelId">The level whose items have such a representation.</param>
/// <param name="UriTemplate">
/// An absolute address with <c>{fieldId}</c> placeholders, validated at load: the scheme must be
/// <c>http</c> or <c>https</c> and every placeholder must name a field of the level.
/// </param>
/// <remarks>
/// <para>
/// The honest, deliberately weak escape hatch. An extension may <b>point at</b> something it hosts
/// itself; it can never inject one into the platform's own interface. What is reached this way runs
/// outside the platform's origin and outside its capability model, and that is the whole reason the hatch
/// is safe to offer.
/// </para>
/// <para>
/// Every consumer has an obvious way to honor it: open the address. Nothing about it presumes what does
/// the opening.
/// </para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1054:URI-like parameters should not be strings",
    Justification = "A template with placeholders is not a well-formed URI until the placeholders are substituted, so it cannot be carried as a Uri.")]
[SuppressMessage(
    "Design",
    "CA1056:URI-like properties should not be strings",
    Justification = "A template with placeholders is not a well-formed URI until the placeholders are substituted, so it cannot be carried as a Uri.")]
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ExternalSurfaceDescriptor(
    string SurfaceId,
    string Name,
    MediaLevelId LevelId,
    string UriTemplate);
