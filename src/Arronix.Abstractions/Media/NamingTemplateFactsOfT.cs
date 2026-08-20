using System.Linq.Expressions;

namespace Arronix.Abstractions.Media;

/// <summary>A host-owned fact about the file being named.</summary>
public enum FileFact
{
    /// <summary>The release name under which the file was acquired.</summary>
    SceneName = 0,

    /// <summary>The file's arriving name, excluding its extension.</summary>
    OriginalFileName = 1
}

/// <summary>The typed view of what a user's naming template mentions.</summary>
public interface INamingTemplateFacts<TItem>
    where TItem : IMediaItem
{
    /// <summary>Reports whether the template mentions a token derived from the property.</summary>
    bool Has<TValue>(Expression<Func<TItem, TValue>> property);

    /// <summary>Reports whether the template mentions a host-owned file fact.</summary>
    bool Has(FileFact fact);
}
