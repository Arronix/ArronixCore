
namespace Arronix.Abstractions.Media;

/// <summary>The common typed shape of an interpreted release.</summary>
/// <typeparam name="TRepresentation">The representation family used by the release.</typeparam>
/// <param name="Title">The media title stated by the release.</param>
/// <param name="Year">The release year it states, when present.</param>
/// <param name="Edition">The edition it states, when present.</param>
/// <param name="Representation">The representation, or <see langword="null"/> when it could not be read.</param>
/// <remarks>
/// Media types use this closed type directly when title, year, edition, and representation are their whole
/// release shape. A media-owned release type derives from it only to add genuine facts such as a set of
/// episode coordinates; the media kind's name alone does not justify another wrapper.
/// </remarks>
public record Release<TRepresentation>(
    string Title,
    int? Year,
    string? Edition = null,
    TRepresentation? Representation = null) : IRelease
    where TRepresentation : class, IRepresentation;
