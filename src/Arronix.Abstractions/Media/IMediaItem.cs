using System.Diagnostics.CodeAnalysis;

namespace Arronix.Abstractions.Media;

/// <summary>
/// A catalog entity a media kind owns. A marker only: everything about it is read from its properties.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately empty. A base class carrying an identifier would put a host-owned member on a plugin-owned
/// type and force every kind to inherit before it could declare anything; the identity is found by
/// <see cref="IdentityAttribute"/> instead. The interface exists so that generic constraints can name "an
/// item", not so that it can carry behaviour.
/// </para>
/// <para>
/// Implementing it is the whole of the entity half of declaring a media kind. What the properties cannot
/// say — how files bind, what groups exist, how releases are matched — is said by the kind's
/// <see cref="IMediaType{TItem}"/>.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Media, UrlFormat = ExperimentalContracts.UrlFormat)]
public interface IMediaItem;
