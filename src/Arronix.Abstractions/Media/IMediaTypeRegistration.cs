using System.ComponentModel;
using Arronix.Abstractions.Identity;
using Arronix.Abstractions.Parsing;

namespace Arronix.Abstractions.Media;

/// <summary>A media definition able to capture its own closed generic contract.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMediaTypeDefinition
{
    /// <summary>Captures this definition for the kind-blind plugin and host pipeline.</summary>
    IMediaTypeRegistration Capture();
}

/// <summary>One typed media definition carried across the kind-blind plugin boundary.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMediaTypeRegistration
{
    /// <summary>Gets the media kind identifier.</summary>
    MediaKindId Kind { get; }

    /// <summary>Gets the durable item type.</summary>
    Type ItemType { get; }

    /// <summary>Gets the acquisition-target type.</summary>
    Type TargetType { get; }

    /// <summary>Gets the interpreted-release type.</summary>
    Type ReleaseType { get; }

    /// <summary>Gets the statically dispatched release parser type.</summary>
    Type ParserType { get; }

    /// <summary>Gets the concrete definition type.</summary>
    Type DeclaringType { get; }

    /// <summary>Reopens the three domain types and their parser type for a host-side binder.</summary>
    TResult Bind<TResult>(IMediaTypeBinder<TResult> binder);
}

/// <summary>The host side of the media-registration double dispatch.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMediaTypeBinder<out TResult>
{
    /// <summary>Binds one typed media definition.</summary>
    TResult Bind<TItem, TTarget, TRelease, TParser>(
        MediaType<TItem, TTarget, TRelease, TParser> definition,
        CompiledShapeCatalog compiledShapes)
        where TItem : class, IMediaItem
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
        where TParser : IReleaseParser<TRelease>;
}

/// <summary>Captures typed media definitions without restating their closed types at registration.</summary>
internal static class MediaTypeRegistration
{
    /// <summary>Captures one already-constructed typed media definition.</summary>
    internal static IMediaTypeRegistration For<TItem, TTarget, TRelease, TParser>(
        MediaType<TItem, TTarget, TRelease, TParser> definition,
        CompiledShapeCatalog compiledShapes)
        where TItem : class, IMediaItem
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
        where TParser : IReleaseParser<TRelease> =>
        new Captured<TItem, TTarget, TRelease, TParser>(definition, compiledShapes);

    private sealed class Captured<TItem, TTarget, TRelease, TParser>(
        MediaType<TItem, TTarget, TRelease, TParser> definition,
        CompiledShapeCatalog compiledShapes) : IMediaTypeRegistration
        where TItem : class, IMediaItem
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
        where TParser : IReleaseParser<TRelease>
    {
        public MediaKindId Kind => definition.Kind;

        public Type ItemType => typeof(TItem);

        public Type TargetType => typeof(TTarget);

        public Type ReleaseType => typeof(TRelease);

        public Type ParserType => typeof(TParser);

        public Type DeclaringType => definition.GetType();

        public TResult Bind<TResult>(IMediaTypeBinder<TResult> binder)
        {
            ArgumentNullException.ThrowIfNull(binder);
            return binder.Bind(definition, compiledShapes);
        }
    }
}
