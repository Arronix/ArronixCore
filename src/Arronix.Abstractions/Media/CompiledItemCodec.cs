using System.ComponentModel;

namespace Arronix.Abstractions.Media;

/// <summary>
/// The compile-time bridge through which a media item is written to durable storage and read back.
/// </summary>
/// <remarks>
/// Generated binding SPI, not an authoring contract. The erasure is no wider than it has to be: every item
/// is an <see cref="IMediaItem"/>, so that is what crosses, and the generated implementation behind it
/// still names the exact item type and checks for it. An author neither implements nor calls it.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ICompiledItemCodec
{
    /// <summary>Gets the exact CLR type this codec writes and constructs.</summary>
    Type ItemType { get; }

    /// <summary>Gets the hash over the member graph this codec's reader accepts.</summary>
    /// <remarks>Two builds that would read one payload differently differ here, whatever their versions say.</remarks>
    string MetadataHash { get; }

    /// <summary>Writes one item into the bytes <see cref="Read"/> accepts.</summary>
    /// <param name="item">A value of <see cref="ItemType"/>.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="item"/> is not of <see cref="ItemType"/>.</exception>
    byte[] Write(IMediaItem item);

    /// <summary>Reads one payload back into the exact item it holds.</summary>
    /// <param name="payload">The bytes a previous <see cref="Write"/> produced.</param>
    /// <returns>The constructed value, whose type is <see cref="ItemType"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">The payload is not a readable item.</exception>
    IMediaItem Read(ReadOnlySpan<byte> payload);
}
