namespace Arronix.Plugin.Music;

/// <summary>
/// The field identifiers this extension declares, in one place.
/// </summary>
/// <remarks>
/// Field identifiers are plain strings in the contract, deliberately: they are dictionary keys within one
/// shape and a branded type would buy nothing. Collecting them here is what stops the shape, the intent
/// surface and the item projection drifting apart, because all three read the same constant.
/// </remarks>
public static class MusicFields
{
    /// <summary>The performer's name. Carries the title semantic at the root level.</summary>
    public const string Name = "name";

    /// <summary>The sorting form of a name or title.</summary>
    public const string SortName = "sort-name";

    /// <summary>The title of a work, a pressing or a recording.</summary>
    public const string Title = "title";

    /// <summary>What tells two same-named subjects apart.</summary>
    public const string Disambiguation = "disambiguation";

    /// <summary>Long-form prose about a subject.</summary>
    public const string Overview = "overview";

    /// <summary>Whether a performer is a person, a group, an orchestra and so on.</summary>
    public const string PerformerKind = "artist-type";

    /// <summary>The performer credited on one recording, which may differ from the work's.</summary>
    public const string Performer = "track-artist";

    /// <summary>Free-text classification of a subject.</summary>
    public const string Genres = "genres";

    /// <summary>The date a work or a pressing was issued.</summary>
    public const string ReleaseDate = "release-date";

    /// <summary>A work's single primary classification.</summary>
    public const string PrimaryKind = "primary-type";

    /// <summary>A work's several secondary classifications.</summary>
    public const string SecondaryKinds = "secondary-types";

    /// <summary>Whether a pressing is official, promotional, unofficial or fabricated.</summary>
    public const string Provenance = "release-status";

    /// <summary>The company that issued a pressing.</summary>
    public const string Issuer = "label";

    /// <summary>Where a pressing was issued.</summary>
    public const string Country = "country";

    /// <summary>How a pressing was carried, and across how many carriers.</summary>
    public const string CarrierFormat = "format";

    /// <summary>How many carriers a pressing spans.</summary>
    public const string CarrierCount = "media-count";

    /// <summary>How many recordings a pressing or a work contains.</summary>
    public const string RecordingCount = "track-count";

    /// <summary>A recording's position, as an ordinal tuple of carrier and place on it.</summary>
    public const string Position = "position";

    /// <summary>How long a recording runs for.</summary>
    public const string Duration = "duration";

    /// <summary>Whether a recording carries an advisory.</summary>
    public const string Explicit = "explicit";

    /// <summary>The subject's condition, as one of the declared states.</summary>
    public const string State = "state";

    /// <summary>The external catalog's identifier for a subject.</summary>
    public const string CatalogId = "musicbrainz-id";

    /// <summary>An image representing a subject.</summary>
    public const string Artwork = "image";

    /// <summary>The monitor dimension asking whether an item is wanted.</summary>
    public const string Wanted = "wanted";

    /// <summary>The monitor dimension asking what to do with output that appears later.</summary>
    public const string FutureOutput = "future-items";
}

/// <summary>
/// The coordinate components this extension's ordinal spaces are built from.
/// </summary>
/// <remarks>
/// A component here is an element of an ordinal tuple - the mathematical sense of the word. It is what a
/// sequence axis names, what a span constraint constrains and what a search argument may be bound to.
/// </remarks>
public static class MusicComponents
{
    /// <summary>The outermost component: which physical carrier within a pressing.</summary>
    public const string Carrier = "medium";

    /// <summary>The innermost component: the place on that carrier.</summary>
    public const string Position = "track";

    /// <summary>The single component of the flat alias space over the whole pressing.</summary>
    public const string Absolute = "absolute";
}

/// <summary>
/// The selection facets this extension declares - the third profile axis.
/// </summary>
public static class MusicFacets
{
    /// <summary>Which primary classifications of work should exist at all.</summary>
    public const string PrimaryKind = "primary-type";

    /// <summary>Which secondary classifications of work should exist at all.</summary>
    public const string SecondaryKind = "secondary-type";

    /// <summary>Which pressing provenances should exist at all.</summary>
    public const string Provenance = "release-status";
}

/// <summary>
/// The states this extension declares an item may be in.
/// </summary>
/// <remarks>
/// These are values of the <see cref="MusicFields.State"/> field. The platform never interprets them; it
/// reads the tone declared alongside each one and presents that.
/// </remarks>
public static class MusicStates
{
    /// <summary>Wanted, issued, and no file satisfies it.</summary>
    public const string Missing = "missing";

    /// <summary>At least one file satisfies it.</summary>
    public const string Held = "have";

    /// <summary>Satisfied, but by a copy below the configured ceiling.</summary>
    public const string Upgradable = "upgradable";

    /// <summary>Not yet issued, so its absence is not a gap.</summary>
    public const string Unissued = "unissued";
}
