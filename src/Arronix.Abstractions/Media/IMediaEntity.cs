using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Media;

/// <summary>
/// The compiled structural floor shared by every durable media entity, whether it is an item or a group.
/// </summary>
/// <remarks>
/// These members are common domain facts, not a host storage model and not a presentation schema. A media
/// extension remains free to add its own strongly typed properties; the host derives a descriptor from the
/// resulting type for kind-blind navigation.
/// </remarks>
public interface IMediaEntity
{
    /// <summary>Gets the host-minted identity, unique within the media kind.</summary>
    MediaItemId Key { get; }

    /// <summary>Gets the identifiers assigned by external catalogs.</summary>
    ExternalIdSet ExternalIds { get; }

    /// <summary>Gets the configured display title.</summary>
    string Title { get; }

    /// <summary>Gets the language of <see cref="Title"/>, when known.</summary>
    Language? TitleLanguage { get; }

    /// <summary>Gets the configured synopsis or description, when one exists.</summary>
    string? Overview { get; }

    /// <summary>Gets the entity's artwork, keyed by open role names.</summary>
    ArtworkSet Artwork { get; }
}

/// <summary>An entity which carries ratings from one or more authorities.</summary>
public interface IHasRatings
{
    /// <summary>Gets the ratings, retaining each authority's scale and voice.</summary>
    IReadOnlyList<Rating> Ratings { get; }
}

/// <summary>An entity which carries an age or audience certification.</summary>
public interface IHasCertification
{
    /// <summary>Gets the certification selected for the configured region.</summary>
    ContentCertification? Certification { get; }
}
