using System.Linq;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Providers;

/// <summary>
/// What one configured release source can do.
/// </summary>
/// <remarks>
/// Fetched rather than declared statically, because for many sources the answer depends on the account:
/// the categories, the page size and the supported arguments are all things the service reports. The host
/// caches it per definition through the session store.
/// </remarks>
public sealed record IndexerProfile
{
    /// <summary>
    /// Gets the argument sets the source accepts.
    /// </summary>
    public required IReadOnlyList<SearchProfile> SearchProfiles { get; init; }

    /// <summary>
    /// Gets every category the source carries, across all of its profiles.
    /// </summary>
    public required IReadOnlyList<CategoryId> Categories { get; init; }

    /// <summary>
    /// Gets a value indicating whether the source can be swept for recently published releases.
    /// </summary>
    public bool SupportsRss { get; init; }

    /// <summary>
    /// Gets a value indicating whether results can be paged through.
    /// </summary>
    public bool SupportsPagination { get; init; }

    /// <summary>
    /// Gets the largest page the source will return.
    /// </summary>
    public int MaxPageSize { get; init; } = 100;

    /// <summary>
    /// Gets the page size to use when nothing else is specified.
    /// </summary>
    public int DefaultPageSize { get; init; } = 100;

    /// <summary>
    /// Gets the source's own labels for its releases — <c>"freeleech"</c>, <c>"internal"</c>. An open
    /// vocabulary the platform carries and never interprets.
    /// </summary>
    public IReadOnlyList<string> Flags { get; init; } = [];

    /// <summary>
    /// Gets how open the source is.
    /// </summary>
    public IndexerPrivacy Privacy { get; init; }

    /// <summary>
    /// Combines two profiles, for a source that aggregates others.
    /// </summary>
    /// <param name="first">The first profile.</param>
    /// <param name="second">The second profile.</param>
    /// <returns>A profile accepting the union of both, and no more capable than the weaker of the two.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static IndexerProfile Union(IndexerProfile first, IndexerProfile second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return new IndexerProfile
        {
            SearchProfiles = [.. first.SearchProfiles, .. second.SearchProfiles],
            Categories = [.. first.Categories.Union(second.Categories)],
            SupportsRss = first.SupportsRss || second.SupportsRss,
            SupportsPagination = first.SupportsPagination && second.SupportsPagination,
            MaxPageSize = Math.Min(first.MaxPageSize, second.MaxPageSize),
            DefaultPageSize = Math.Min(first.DefaultPageSize, second.DefaultPageSize),
            Flags = [.. first.Flags.Union(second.Flags, StringComparer.Ordinal)],
            Privacy = (IndexerPrivacy)Math.Max((int)first.Privacy, (int)second.Privacy)
        };
    }
}

/// <summary>
/// How open a release source is.
/// </summary>
public enum IndexerPrivacy
{
    /// <summary>Anyone can use it.</summary>
    Public = 0,

    /// <summary>An account is needed, but anyone can obtain one.</summary>
    SemiPrivate = 1,

    /// <summary>Membership is closed.</summary>
    Private = 2
}
