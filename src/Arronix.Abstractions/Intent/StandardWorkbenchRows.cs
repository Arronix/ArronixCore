using Arronix.Abstractions.FileSystem;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Releases;

namespace Arronix.Abstractions.Intent;

/// <summary>One catalog-shaped item offered for admission to the library.</summary>
/// <typeparam name="TItem">The media type's own item shape.</typeparam>
public sealed record CatalogCandidateRow<TItem>
    where TItem : class, IMediaItem
{
    /// <summary>Gets the complete catalog item, including artwork and every media-owned fact.</summary>
    public required TItem Item { get; init; }

    /// <summary>Gets representative artwork for the candidate, without prescribing how it is laid out.</summary>
    [Artwork]
    public ArtworkSet Artwork { get; init; } = ArtworkSet.Empty;

    /// <summary>Gets whether the item should be added.</summary>
    [Editable]
    public bool Add { get; init; }
}

/// <summary>One interpreted release option being considered by hand.</summary>
/// <typeparam name="TTarget">The media-owned acquisition target.</typeparam>
/// <typeparam name="TRelease">The media-owned interpreted release.</typeparam>
public sealed record ReleaseChoiceRow<TTarget, TRelease>
    where TTarget : class, IReleaseTarget
    where TRelease : class, IRelease
{
    /// <summary>Gets the listing, interpreted release and typed coverage judgment as one option.</summary>
    public required ReleaseOption<TTarget, TRelease> Option { get; init; }

    /// <summary>Gets whether the option should be acquired.</summary>
    [Editable]
    public bool Grab { get; init; }
}

/// <summary>One loose file being assigned to a typed acquisition target.</summary>
/// <typeparam name="TTarget">The media-owned target the file may satisfy.</typeparam>
/// <typeparam name="TRelease">The media-owned interpretation of the file or its release name.</typeparam>
public sealed record ManualImportRow<TTarget, TRelease>
    where TTarget : class, IReleaseTarget
    where TRelease : class, IRelease
{
    /// <summary>Gets the path being imported, in the source platform's grammar.</summary>
    public required PlatformPath File { get; init; }

    /// <summary>Gets what the release/file recognizers read, when they could interpret it.</summary>
    public TRelease? Reading { get; init; }

    /// <summary>Gets or sets the target the file should satisfy.</summary>
    [Editable]
    public TTarget? Target { get; init; }

    /// <summary>Gets the current typed coverage judgment for the selected target.</summary>
    public TargetMatch<TTarget>? Match { get; init; }

    /// <summary>Gets whether this row should be imported.</summary>
    [Editable]
    public bool Import { get; init; } = true;
}
