using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Media;

/// <summary>
/// Marks the property that names the entity to a person.
/// </summary>
/// <remarks>
/// Exactly one property per entity, analyzer-enforced. It drives the level's display title and the host's
/// title transforms — the cleaned form, the article-moved form, the first-character form — none of which a
/// kind declares for itself. A kind that authored its own sort key or clean title would recreate the
/// per-kind divergence in article handling that the host transforms exist to prevent, which is why there is
/// no attribute for either.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed class TitleAttribute : Attribute;
