#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended implementer.
#pragma warning disable ARX0020 // Media contracts are experimental; a media extension is their intended implementer.

using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Movies;

/// <summary>One loose file being assigned to a movie.</summary>
/// <remarks>
/// A column list and the proposal that fills it used to agree by convention — the same identifier written
/// twice, checked by nothing. The row type is the column set.
/// </remarks>
public sealed record ManualImportRow
{
    /// <summary>The path being assigned.</summary>
    [Prominence(Prominence.Primary), Display(Name = "File")]
    public required string Path { get; init; }

    /// <summary>The title read out of the path.</summary>
    [Prominence(Prominence.Secondary), Display(Name = "Read as")]
    public string? ParsedTitle { get; init; }

    /// <summary>The movie the file is assigned to.</summary>
    [Editable, Prominence(Prominence.Primary)]
    public Movie? Movie { get; init; }

    /// <summary>The quality read out of the path.</summary>
    [Prominence(Prominence.Secondary)]
    public QualityTier? Quality { get; init; }

    /// <summary>The cut or edition read out of the path.</summary>
    public string? Edition { get; init; }
}

/// <summary>One release candidate being considered by hand.</summary>
public sealed record ReleaseChoiceRow
{
    /// <summary>The release title.</summary>
    [Prominence(Prominence.Primary)]
    public required string Release { get; init; }

    /// <summary>The rung it reads onto.</summary>
    [Prominence(Prominence.Secondary)]
    public QualityTier? Quality { get; init; }

    /// <summary>Its size on the wire.</summary>
    [Size, Prominence(Prominence.Secondary)]
    public long Size { get; init; }

    /// <summary>Where it came from.</summary>
    public string? Source { get; init; }

    /// <summary>The movie it would satisfy.</summary>
    [Prominence(Prominence.Secondary)]
    public Movie? Movie { get; init; }

    /// <summary>Whether to grab it.</summary>
    [Editable, Prominence(Prominence.Primary)]
    public bool Grab { get; init; }
}

/// <summary>One catalog entry offered for the library.</summary>
/// <remarks>
/// No artwork column. The host refuses a working-surface column whose value kind is artwork, because an
/// image in a grid is the one place this vocabulary would start to imply a layout.
/// </remarks>
public sealed record CatalogCandidateRow
{
    /// <summary>The film's title.</summary>
    [Prominence(Prominence.Primary)]
    public required string Title { get; init; }

    /// <summary>Its release year.</summary>
    [Prominence(Prominence.Primary)]
    public int? Year { get; init; }

    /// <summary>The production company.</summary>
    public string? Studio { get; init; }

    /// <summary>Its availability.</summary>
    [Prominence(Prominence.Secondary), Display(Name = "Availability")]
    public MovieStatus Status { get; init; }

    /// <summary>The collection it belongs to.</summary>
    [Prominence(Prominence.Secondary)]
    public Collection? Collection { get; init; }

    /// <summary>Whether to take it on.</summary>
    [Editable, Prominence(Prominence.Primary)]
    public bool Add { get; init; }
}
