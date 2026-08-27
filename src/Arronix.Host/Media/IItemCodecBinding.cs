using Arronix.Abstractions.Media;

namespace Arronix.Host.Media;

/// <summary>
/// A media runtime's generated bridge for storing its own item and reading it back.
/// </summary>
/// <remarks>
/// Host-internal, and implemented only by the runtime the model factory builds. The bridge arrives on the
/// compiled shape catalog the media type's own build produced, so the host holds it rather than looking for
/// it: nothing here reads an assembly's attributes, resolves a type by name, or constructs anything.
/// </remarks>
internal interface IItemCodecBinding
{
    /// <summary>
    /// Gets the bridge, or <see langword="null"/> when the item type's build produced none.
    /// </summary>
    ICompiledItemCodec? ItemCodec { get; }
}
