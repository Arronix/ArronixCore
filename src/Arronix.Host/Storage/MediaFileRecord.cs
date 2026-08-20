using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Shape;


namespace Arronix.Host.Storage;

/// <summary>
/// One file the platform knows about.
/// </summary>
/// <remarks>
/// Files are host-owned. An extension never holds a file row, which is why the identifier is an opaque
/// surrogate the store mints rather than anything derived from a path: a path changes when content is
/// renamed or relocated, and every link would have to be rewritten if the identity traveled with it.
/// </remarks>
public sealed record MediaFileRecord
{
    /// <summary>
    /// Gets the file's identifier.
    /// </summary>
    public required MediaFileId Id { get; init; }

    /// <summary>
    /// Gets the item the file row hangs from.
    /// </summary>
    /// <remarks>
    /// The anchor may sit above the units the file satisfies. That is what lets file ownership survive a
    /// change of chosen variant: the file belongs to the work, not to the pressing that happened to be
    /// selected when it arrived.
    /// </remarks>
    public required MediaItemRef Anchor { get; init; }

    /// <summary>
    /// Gets the file's full path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the file's size in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Gets when the file was last written.
    /// </summary>
    public DateTimeOffset? Modified { get; init; }

    /// <summary>
    /// Gets the quality tier the file was judged to be, within its format family's ladder.
    /// </summary>
    public QualityTier? Quality { get; init; }

    /// <summary>
    /// Gets the format family the file belongs to, which is what makes <see cref="Quality"/> comparable.
    /// </summary>
    public string? FormatFamilyId { get; init; }

    /// <summary>
    /// Gets the languages the file carries.
    /// </summary>
    public IReadOnlyList<Language> Languages { get; init; } = [];
}
