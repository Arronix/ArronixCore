using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Media;

/// <summary>
/// The authoring seam a media-kind plugin implements: one type per media kind, declaring what the item's
/// attributes cannot.
/// </summary>
/// <typeparam name="TItem">The kind's item type.</typeparam>
/// <remarks>
/// <para>
/// Static-abstract, and it deliberately does <b>not</b> derive from the non-generic
/// <see cref="IMediaType"/>. The two are the same split a typed data mapper has between the method that
/// configures a model and the model itself: this one is how a kind is written, the other is what the host
/// holds afterwards. A plugin type implementing both would be authoring surface and runtime model at once,
/// which is precisely the conflation this design removes.
/// </para>
/// <para>
/// Static-abstract rather than an instance interface because configuring a kind is not a thing an instance
/// does. There is no per-instance state to hold, nothing to inject, and no reason for the host to
/// construct anything before it can read a kind's declaration.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaType<TItem>
    where TItem : IMediaItem
{
    /// <summary>
    /// Gets the media kind identifier. Must equal the manifest's declared kind.
    /// </summary>
    static abstract MediaKindId Kind { get; }

    /// <summary>
    /// Declares what the item's attributes cannot say: how files bind, what groups and selection policies
    /// exist, how releases are matched, queried, named and summarized.
    /// </summary>
    /// <param name="builder">The builder the declaration is recorded on.</param>
    static abstract void Configure(IMediaTypeBuilder<TItem> builder);
}
