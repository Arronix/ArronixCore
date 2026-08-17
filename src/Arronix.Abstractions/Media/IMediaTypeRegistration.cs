using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;

namespace Arronix.Abstractions.Media;

/// <summary>
/// One typed media kind, captured at registration, with its two type arguments still recoverable.
/// </summary>
/// <remarks>
/// <para>
/// A media kind is authored as a pair of types — the item and the <see cref="IMediaType{TItem}"/> that
/// declares it — but the registry that captures it and the loader that carries it are both kind-blind by
/// construction, so neither can name either type. This interface is how the pair survives that crossing
/// without either side naming it: the plugin closes the generic at its own call site, and the host reopens
/// it through <see cref="Bind{TResult}"/>.
/// </para>
/// <para>
/// The alternative — capturing two <see cref="Type"/> objects and reflecting a generic method back open —
/// would move a compile-time fact to a runtime one and put a reflection call on the load path of every
/// kind. That is the opposite of what a typed authoring surface is for, and it would be invisible to the
/// trimmer besides.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaTypeRegistration
{
    /// <summary>
    /// Gets the media kind identifier, read from the declaring type's static member.
    /// </summary>
    MediaKindId Kind { get; }

    /// <summary>
    /// Gets the item type, so a caller that only needs the entity does not have to reopen the generic.
    /// </summary>
    Type ItemType { get; }

    /// <summary>
    /// Gets the declaring type, for diagnostics that should name what was registered.
    /// </summary>
    Type DeclaringType { get; }

    /// <summary>
    /// Reopens the generic and hands both type arguments to <paramref name="binder"/>.
    /// </summary>
    /// <typeparam name="TResult">What the binder produces.</typeparam>
    /// <param name="binder">The host-side binder.</param>
    /// <returns>Whatever the binder produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binder"/> is <see langword="null"/>.</exception>
    TResult Bind<TResult>(IMediaTypeBinder<TResult> binder);
}

/// <summary>
/// The host side of <see cref="IMediaTypeRegistration.Bind{TResult}"/>: what to do once the item type and
/// its declaring type are back in scope.
/// </summary>
/// <typeparam name="TResult">What binding produces.</typeparam>
/// <remarks>
/// The visitor half of the double-dispatch. It exists so that the one place in the host that genuinely
/// needs both type arguments — the derivation — can have them statically, while every other participant
/// keeps holding the kind-blind <see cref="IMediaTypeRegistration"/>.
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaTypeBinder<out TResult>
{
    /// <summary>
    /// Binds one typed media kind.
    /// </summary>
    /// <typeparam name="TItem">The kind's item type.</typeparam>
    /// <typeparam name="TType">The type declaring it.</typeparam>
    /// <returns>The result.</returns>
    TResult Bind<TItem, TType>()
        where TItem : IMediaItem
        where TType : IMediaType<TItem>;
}

/// <summary>
/// Captures a typed media kind as an <see cref="IMediaTypeRegistration"/>.
/// </summary>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public static class MediaTypeRegistration
{
    /// <summary>
    /// Captures one typed media kind.
    /// </summary>
    /// <typeparam name="TItem">The kind's item type.</typeparam>
    /// <typeparam name="TType">The type declaring it.</typeparam>
    /// <returns>The captured registration.</returns>
    public static IMediaTypeRegistration For<TItem, TType>()
        where TItem : IMediaItem
        where TType : IMediaType<TItem>
        => Captured<TItem, TType>.Instance;

    /// <summary>
    /// One captured pair. Stateless, so one instance per closed pair is all that is ever needed.
    /// </summary>
    private sealed class Captured<TItem, TType> : IMediaTypeRegistration
        where TItem : IMediaItem
        where TType : IMediaType<TItem>
    {
        internal static readonly Captured<TItem, TType> Instance = new();

        /// <inheritdoc />
        public MediaKindId Kind => TType.Kind;

        /// <inheritdoc />
        public Type ItemType => typeof(TItem);

        /// <inheritdoc />
        public Type DeclaringType => typeof(TType);

        /// <inheritdoc />
        public TResult Bind<TResult>(IMediaTypeBinder<TResult> binder)
        {
            ArgumentNullException.ThrowIfNull(binder);
            return binder.Bind<TItem, TType>();
        }
    }
}
