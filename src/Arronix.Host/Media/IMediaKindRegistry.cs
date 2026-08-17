using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Identity;

namespace Arronix.Host.Media;

/// <summary>
/// The host's view of every media kind a loaded extension declared.
/// </summary>
/// <remarks>
/// Held host-side deliberately. An extension declares its own shape and never reads another's, because
/// direct interaction between extensions is forbidden; a registry an extension could query would be a way
/// around that. The promotion trigger is an extension that must read a foreign shape, which would itself
/// need review.
/// </remarks>
public interface IMediaKindRegistry
{
    /// <summary>
    /// Gets every admitted media kind, ordered by identifier so listings are stable.
    /// </summary>
    IReadOnlyList<RegisteredMediaKind> All { get; }

    /// <summary>
    /// Looks up a media kind.
    /// </summary>
    /// <param name="kind">The identifier, which may have come from a request.</param>
    /// <param name="registered">The kind when it is admitted; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the kind is admitted.</returns>
    bool TryGet(MediaKindId kind, [NotNullWhen(true)] out RegisteredMediaKind? registered);

    /// <summary>
    /// Gets a media kind.
    /// </summary>
    /// <param name="kind">The identifier.</param>
    /// <returns>The kind.</returns>
    /// <exception cref="Abstractions.Errors.ArronixException">
    /// No such kind is admitted. Carries <see cref="Abstractions.Health.CoreErrorCode.MediaKindNotFound"/>.
    /// </exception>
    RegisteredMediaKind Require(MediaKindId kind);
}
