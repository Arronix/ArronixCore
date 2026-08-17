using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Arronix.Abstractions.Media;

/// <summary>
/// The images that represent one entity, carried as one property rather than one property per role.
/// </summary>
/// <remarks>
/// Four properties differing only by a role name is the repeated-composite smell: the roles are an open,
/// host-owned vocabulary shared across every media kind, so a kind that spells them into its own field list
/// is restating a platform fact and pinning itself to whichever four the first cataloger happened to
/// supply.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ArtworkSet
{
    /// <summary>
    /// Gets the set an entity with no artwork carries.
    /// </summary>
    public static ArtworkSet Empty { get; } = new();

    /// <summary>
    /// Gets the images, in the order the supplying cataloger offered them.
    /// </summary>
    public IReadOnlyList<ArtworkImage> Images { get; init; } = [];

    /// <summary>
    /// Creates a set from images.
    /// </summary>
    /// <param name="images">The images.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="images"/> is <see langword="null"/>.</exception>
    public static ArtworkSet Of(params ArtworkImage[] images)
    {
        ArgumentNullException.ThrowIfNull(images);
        return images.Length == 0 ? Empty : new ArtworkSet { Images = [.. images] };
    }

    /// <summary>
    /// Creates a set from a sequence of images.
    /// </summary>
    /// <param name="images">The images.</param>
    /// <returns>The set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="images"/> is <see langword="null"/>.</exception>
    public static ArtworkSet From(IEnumerable<ArtworkImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        var values = images.ToArray();
        return values.Length == 0 ? Empty : new ArtworkSet { Images = values };
    }

    /// <summary>
    /// Attempts to read the first image carrying one role.
    /// </summary>
    /// <param name="role">The role, compared case-insensitively.</param>
    /// <param name="image">The image when one was carried; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when one was carried; otherwise <see langword="false"/>.</returns>
    public bool TryGet(string role, [NotNullWhen(true)] out ArtworkImage? image)
    {
        foreach (var candidate in Images)
        {
            if (string.Equals(candidate.Role, role, StringComparison.OrdinalIgnoreCase))
            {
                image = candidate;
                return true;
            }
        }

        image = null;
        return false;
    }
}

/// <summary>
/// One image representing an entity, and what it is for.
/// </summary>
/// <param name="Role">
/// What the image is for, from the host's open artwork-role vocabulary — <c>"poster"</c>, <c>"fanart"</c>.
/// Open because a cataloger may supply a role the platform has never heard of, and carrying it is strictly
/// better than dropping it.
/// </param>
/// <param name="Address">Where the image is fetched from.</param>
/// <param name="Width">The image's width in pixels, when the supplier stated one.</param>
/// <param name="Height">The image's height in pixels, when the supplier stated one.</param>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record ArtworkImage(string Role, Uri Address, int? Width = null, int? Height = null);
